#include "a_uart.h"

#include "cmsis_os.h"
#include "main.h"

#include "FreeRTOS.h"
#include "task.h"

#include <string.h>

#define UART_FRAME_QUEUE_DEPTH 2U

extern UART_HandleTypeDef hlpuart1;
extern UART_HandleTypeDef huart1;
extern UART_HandleTypeDef huart2;
extern UART_HandleTypeDef huart3;
extern UART_HandleTypeDef huart4;

typedef struct {
    uint8_t data[UART_FRAME_MAX_LENGTH];
    uint16_t length;
} UartFrameSlot_t;

typedef struct {
    UART_HandleTypeDef *huart;
    uint8_t dma_buffer[UART_FRAME_MAX_LENGTH];
    UartFrameSlot_t frames[UART_FRAME_QUEUE_DEPTH];
    uint16_t old_pos;
    uint8_t head;
    uint8_t count;
    uint32_t dropped_frames;
    volatile uint8_t restart_requested;
    void (*set_tx_mode)(void);
    void (*set_rx_mode)(void);
    osSemaphoreId_t tx_done_sem;
} UartRxContext_t;

static UartRxContext_t s_uart_contexts[] = {
    { .huart = &hlpuart1 },
    { .huart = &huart1 },
    { .huart = &huart2 },
    { .huart = &huart3 },
    { .huart = &huart4 },
};

static UartRxContext_t* FindContext( UART_HandleTypeDef *huart )
{
  size_t index;

  if( huart == NULL )
  {
    return NULL;
  }

  for( index = 0U; index < ( sizeof( s_uart_contexts ) / sizeof( s_uart_contexts[ 0 ] ) ); index++ )
  {
    if( s_uart_contexts[ index ].huart == huart )
    {
      return &s_uart_contexts[ index ];
    }
  }

  return NULL;
}

static void StoreFrame( UartRxContext_t *ctx, uint16_t pos )
{
  UBaseType_t saved_mask;
  UartFrameSlot_t *slot;
  uint8_t tail;
  uint16_t length = 0U;

  if( ( ctx == NULL ) || ( pos > UART_FRAME_MAX_LENGTH ) || ( pos == ctx->old_pos ) )
  {
    return;
  }

  saved_mask = taskENTER_CRITICAL_FROM_ISR( );

  if( ctx->count >= UART_FRAME_QUEUE_DEPTH )
  {
    ctx->head = ( uint8_t )( ( ctx->head + 1U ) % UART_FRAME_QUEUE_DEPTH );
    ctx->count = ( uint8_t )( UART_FRAME_QUEUE_DEPTH - 1U );
    ctx->dropped_frames++;
  }

  tail = ( uint8_t )( ( ctx->head + ctx->count ) % UART_FRAME_QUEUE_DEPTH );
  slot = &ctx->frames[ tail ];
  ( void )memset( slot->data, 0, sizeof( slot->data ) );

  if( pos > ctx->old_pos )
  {
    length = ( uint16_t )( pos - ctx->old_pos );
    ( void )memcpy( slot->data, &ctx->dma_buffer[ ctx->old_pos ], length );
  }
  else
  {
    const uint16_t tail_len = ( uint16_t )( UART_FRAME_MAX_LENGTH - ctx->old_pos );
    length = ( uint16_t )( tail_len + pos );
    if( length > UART_FRAME_MAX_LENGTH )
    {
      length = UART_FRAME_MAX_LENGTH;
    }
    ( void )memcpy( slot->data, &ctx->dma_buffer[ ctx->old_pos ], tail_len );
    if( pos > 0U )
    {
      ( void )memcpy( &slot->data[ tail_len ], ctx->dma_buffer, pos );
    }
  }

  ctx->old_pos = pos;
  slot->length = length;
  ctx->count++;

  taskEXIT_CRITICAL_FROM_ISR( saved_mask );
}

void Uart_Init( void )
{
  size_t index;

  for( index = 0U; index < ( sizeof( s_uart_contexts ) / sizeof( s_uart_contexts[ 0 ] ) ); index++ )
  {
    if( s_uart_contexts[ index ].tx_done_sem == NULL )
    {
      s_uart_contexts[ index ].tx_done_sem = osSemaphoreNew( 1U, 0U, NULL );
    }
    Uart_ClearData( s_uart_contexts[ index ].huart );
    ( void )Uart_StartReceive( s_uart_contexts[ index ].huart );
  }
}

void Uart_RegisterDirControl( UART_HandleTypeDef *huart, void ( *set_tx_mode )( void ), void ( *set_rx_mode )( void ) )
{
  UartRxContext_t *ctx = FindContext( huart );

  if( ctx == NULL )
  {
    return;
  }

  ctx->set_tx_mode = set_tx_mode;
  ctx->set_rx_mode = set_rx_mode;
}

/* IT 기반 송신. 전역 IRQ를 차단하지 않으며, 바이트 간 gap은 TXE 인터럽트가
 * 즉시 다음 바이트를 밀어 넣어 최소화된다. DE 하강은 TC 인터럽트 시점에
 * HAL_UART_TxCpltCallback(ISR)에서 수행하므로 마지막 stop bit 이후 즉시
 * 라인이 해제된다. 송신 완료까지 이 함수 안에서 블로킹한다. */
bool Uart_TransmitBlocking( UART_HandleTypeDef *huart, const uint8_t *data, uint16_t length, uint32_t timeout_ms )
{
  UartRxContext_t *ctx = FindContext( huart );

  if( ( ctx == NULL ) || ( data == NULL ) || ( length == 0U ) || ( ctx->tx_done_sem == NULL ) )
  {
    return false;
  }

  /* 이전 송신의 잔여 토큰 제거 */
  ( void )osSemaphoreAcquire( ctx->tx_done_sem, 0U );

  if( ctx->set_tx_mode != NULL )
  {
    ctx->set_tx_mode();
  }

  if( HAL_UART_Transmit_IT( huart, ( uint8_t* )data, length ) != HAL_OK )
  {
    if( ctx->set_rx_mode != NULL )
    {
      ctx->set_rx_mode();
    }
    return false;
  }

  if( osSemaphoreAcquire( ctx->tx_done_sem, timeout_ms ) != osOK )
  {
    ( void )HAL_UART_AbortTransmit( huart );
    if( ctx->set_rx_mode != NULL )
    {
      ctx->set_rx_mode();
    }
    return false;
  }

  return true;
}

bool Uart_StartReceive( UART_HandleTypeDef *huart )
{
  UartRxContext_t *ctx = FindContext( huart );
  HAL_StatusTypeDef status;

  if( ctx == NULL )
  {
    return false;
  }

  /* DMA가 버퍼에 쓰는 중일 수 있으므로 반드시 정지 후 버퍼를 초기화한다 */
  ( void )HAL_UART_DMAStop( huart );
  ctx->old_pos = 0U;
  ( void )memset( ctx->dma_buffer, 0, sizeof( ctx->dma_buffer ) );

  status = HAL_UARTEx_ReceiveToIdle_DMA( huart, ctx->dma_buffer, UART_FRAME_MAX_LENGTH );
  if( ( status == HAL_OK ) && ( huart->hdmarx != NULL ) )
  {
    __HAL_DMA_DISABLE_IT( huart->hdmarx, DMA_IT_HT );
  }

  return status == HAL_OK;
}

void Uart_StopReceive( UART_HandleTypeDef *huart )
{
  UartRxContext_t *ctx = FindContext( huart );

  if( ctx == NULL )
  {
    return;
  }

  ( void )HAL_UART_DMAStop( huart );
  ctx->old_pos = 0U;
}

void Uart_ClearData( UART_HandleTypeDef *huart )
{
  UartRxContext_t *ctx = FindContext( huart );

  if( ctx == NULL )
  {
    return;
  }

  taskENTER_CRITICAL( );
  ctx->head = 0U;
  ctx->count = 0U;
  ctx->dropped_frames = 0U;
  ( void )memset( ctx->frames, 0, sizeof( ctx->frames ) );
  taskEXIT_CRITICAL( );
}

bool Uart_GetData( UART_HandleTypeDef *huart, uint8_t *data, uint16_t capacity, uint16_t *length )
{
  UartRxContext_t *ctx = FindContext( huart );
  UartFrameSlot_t *slot;
  uint16_t copy_len;

  if( length != NULL )
  {
    *length = 0U;
  }

  if( ( ctx == NULL ) || ( data == NULL ) || ( length == NULL ) || ( capacity == 0U ) )
  {
    return false;
  }

  taskENTER_CRITICAL( );
  if( ctx->count == 0U )
  {
    taskEXIT_CRITICAL( );
    return false;
  }

  slot = &ctx->frames[ ctx->head ];
  copy_len = slot->length;
  if( copy_len > capacity )
  {
    copy_len = capacity;
  }

  ( void )memcpy( data, slot->data, copy_len );
  slot->length = 0U;
  ctx->head = ( uint8_t )( ( ctx->head + 1U ) % UART_FRAME_QUEUE_DEPTH );
  ctx->count--;
  taskEXIT_CRITICAL( );

  *length = copy_len;

  return copy_len > 0U;
}

bool Uart_WaitData( UART_HandleTypeDef *huart, uint8_t *data, uint16_t capacity, uint16_t *length, uint32_t timeout_ms )
{
  const uint32_t start_ms = HAL_GetTick();

  while( !Uart_GetData( huart, data, capacity, length ) )
  {
    if( ( timeout_ms > 0U ) && ( ( uint32_t )( HAL_GetTick() - start_ms ) >= timeout_ms ) )
    {
      return false;
    }
    osDelay( 1U );
  }

  return true;
}

void HAL_UARTEx_RxEventCallback( UART_HandleTypeDef *huart, uint16_t Size )
{
  UartRxContext_t *ctx = FindContext( huart );

  if( ctx != NULL )
  {
    StoreFrame( ctx, Size );
  }
}

/* TC(Transmission Complete) 시점에 호출된다. 여기서 DE를 내려야 마지막
 * stop bit가 라인에 실린 직후 정확히 드라이버가 해제된다. */
void HAL_UART_TxCpltCallback( UART_HandleTypeDef *huart )
{
  UartRxContext_t *ctx = FindContext( huart );

  if( ctx == NULL )
  {
    return;
  }

  if( ctx->set_rx_mode != NULL )
  {
    ctx->set_rx_mode();
  }

  if( ctx->tx_done_sem != NULL )
  {
    ( void )osSemaphoreRelease( ctx->tx_done_sem );
  }
}

void HAL_UART_ErrorCallback( UART_HandleTypeDef *huart )
{
  UartRxContext_t *ctx = FindContext( huart );

  if( ctx != NULL )
  {
    ctx->restart_requested = 1U;
  }
}

void Uart_ServiceRecovery( void )
{
  size_t index;

  for( index = 0U; index < ( sizeof( s_uart_contexts ) / sizeof( s_uart_contexts[ 0 ] ) ); index++ )
  {
    ( void )Uart_ServiceRecoveryFor( s_uart_contexts[ index ].huart );
  }
}

/* 각 UART 소유 task가 자기 포트만 복구한다. TX 경로의 Stop/StartReceive와
 * 경합하지 않도록 소유 task 루프에서만 호출해야 한다. */
bool Uart_ServiceRecoveryFor( UART_HandleTypeDef *huart )
{
  UartRxContext_t *ctx = FindContext( huart );

  if( ( ctx == NULL ) || ( ctx->restart_requested == 0U ) )
  {
    return false;
  }

  ctx->restart_requested = 0U;

  return Uart_StartReceive( huart );
}
