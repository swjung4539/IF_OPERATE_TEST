#include "a_gst_protocol.h"

#include "a_comm_config.h"
#include "a_shared_map.h"
#include "a_uart.h"
#include "m_modbus.h"

#include <string.h>
#include "a_log.h"

#define GST_HEADER_0              0x47U
#define GST_HEADER_1              0x53U
#define GST_HEADER_2              0x54U
#define GST_TAIL_0                0x53U
#define GST_TAIL_1                0x49U

#define GST_FC_READ               0x01U
#define GST_FC_WRITE              0x02U

#define GST_WRITE_START_ADDR      201U
#define GST_WRITE_END_ADDR        241U
#define GST_READ_START_ADDR       242U
#define GST_READ_END_ADDR         282U
#define GST_RESERVED_START_ADDR   283U
#define GST_RESERVED_END_ADDR     322U

#define GST_REGISTER_COUNT        41U
#define GST_DATA_LENGTH           (GST_REGISTER_COUNT * 2U)
#define GST_CRC_OFFSET(data_len)  (7U + (data_len))
#define GST_FRAME_LENGTH(data_len) (11U + (data_len))
#define GST_MAX_FRAME_LENGTH      GST_FRAME_LENGTH(GST_DATA_LENGTH)

#define GST_RX_QUEUE_DEPTH              4U
#define GST_TX_QUEUE_DEPTH              4U
#define GST_RX_POLL_TIMEOUT_MS          1U
#define GST_TX_RETRY_DELAY_MS           1U
#define GST_RX_REASSEMBLY_TIMEOUT_MS  200U

typedef struct
{
  uint8_t data[ GST_MAX_FRAME_LENGTH ];
  uint16_t length;
  uint8_t expect_response;
} GstFrameMessage_t;


static bool IsElapsed( uint32_t now, uint32_t last, uint16_t period_ms )
{
  return ( period_ms == 0U ) || ( ( uint32_t )( now - last ) >= period_ms );
}

static uint16_t GetTimeoutMs( void )
{
  const CommConfig_t *config = CommConfig_Get();

  if( ( config == NULL ) || ( config->tx_timeout_ms == 0U ) )
  {
    return DEFAULT_TX_TIMEOUT_MS;
  }

  return config->tx_timeout_ms;
}

static uint8_t GetRetryCount( void )
{
  const CommConfig_t *config = CommConfig_Get();

  if( ( config == NULL ) || ( config->retry_count == 0U ) )
  {
    return DEFAULT_RETRY_COUNT;
  }

  return config->retry_count;
}

static void LogFrameSummary( const char *tag, const uint8_t *frame, uint16_t frame_len )
{
  uint16_t addr = 0U;
  uint16_t data = 0U;
  uint8_t fc = 0U;
  uint8_t len = 0U;

  if( ( frame != NULL ) && ( frame_len >= ( uint16_t )GST_FRAME_LENGTH( 0U ) ) )
  {
    fc = frame[ 3 ];
    addr = Modbus_GetU16Be( &frame[ 4 ] );
    len = frame[ 6 ];
    if( len >= 2U )
    {
      data = ( uint16_t )frame[ 7 ] | ( ( uint16_t )frame[ 8 ] << 8U );
    }
  }

  LOG( g_log_gst, "%s fc=0x%02X addr=%u len=%u data=%u\r\n", tag, fc, addr, len, data );
}

static bool IsGlobalHoldingPending( const ModbusSlaveWriteRequest_t *pending )
{
  return ( pending != NULL ) && ( pending->valid != 0U ) && ( pending->is_global != 0U )
         && ( pending->table == SHARED_MAP_TABLE_HOLDING_REGISTER ) && ( pending->quantity > 0U );
}

static bool PendingRangeWithin( const ModbusSlaveWriteRequest_t *pending, uint16_t start_address, uint16_t end_address )
{
  const uint32_t pending_start = pending->address;
  const uint32_t pending_end = pending_start + pending->quantity - 1U;

  return ( pending_start >= start_address ) && ( pending_end <= end_address );
}

static bool PendingOverlaysAddress( const ModbusSlaveWriteRequest_t *pending, uint16_t address, uint16_t *value )
{
  uint16_t offset;

  if( ( pending == NULL ) || ( value == NULL ) || !IsGlobalHoldingPending( pending ) )
  {
    return false;
  }

  if( ( address < pending->address ) || ( ( uint32_t )address >= ( ( uint32_t )pending->address + pending->quantity ) ) )
  {
    return false;
  }

  offset = ( uint16_t )( address - pending->address );
  *value = pending->values[ offset ];

  return true;
}

static bool BuildWriteFrame( const ModbusSlaveWriteRequest_t *pending, uint8_t *frame, uint16_t frame_capacity,
                             uint16_t *frame_len )
{
  uint16_t offset;
  uint16_t crc;

  if( frame_len != NULL )
  {
    *frame_len = 0U;
  }

  if( ( frame == NULL ) || ( frame_len == NULL ) || ( frame_capacity < GST_MAX_FRAME_LENGTH ) )
  {
    return false;
  }

  frame[ 0 ] = GST_HEADER_0;
  frame[ 1 ] = GST_HEADER_1;
  frame[ 2 ] = GST_HEADER_2;
  frame[ 3 ] = GST_FC_WRITE;
  Modbus_PutU16Be( &frame[ 4 ], GST_WRITE_START_ADDR );
  frame[ 6 ] = ( uint8_t )GST_DATA_LENGTH;

  for( offset = 0U; offset < GST_REGISTER_COUNT; offset++ )
  {
    const uint16_t address = ( uint16_t )( GST_WRITE_START_ADDR + offset );
    uint16_t value = 0U;

    if( !SharedMap_ReadGlobal( SHARED_MAP_TABLE_HOLDING_REGISTER, address, &value ) )
    {
      return false;
    }

    if( PendingOverlaysAddress( pending, address, &value ) )
    {
      /* pending value intentionally overrides the committed map value */
    }

    /* GST 디바이스는 Little-endian (Lo, Hi) */
    frame[ 7U + ( offset * 2U ) ]      = ( uint8_t )( value & 0xFFU );
    frame[ 7U + ( offset * 2U ) + 1U ] = ( uint8_t )( value >> 8U );
  }

  crc = CRC16( frame, GST_CRC_OFFSET( GST_DATA_LENGTH ) );
  frame[ GST_CRC_OFFSET( GST_DATA_LENGTH ) ] = ( uint8_t )( crc & 0x00FFU );
  frame[ GST_CRC_OFFSET(GST_DATA_LENGTH) + 1U ] = ( uint8_t )( crc >> 8U );
  frame[ GST_CRC_OFFSET(GST_DATA_LENGTH) + 2U ] = GST_TAIL_0;
  frame[ GST_CRC_OFFSET(GST_DATA_LENGTH) + 3U ] = GST_TAIL_1;

  *frame_len = GST_MAX_FRAME_LENGTH;

  return true;
}

static bool ValidateFrame( const uint8_t *frame, uint16_t frame_len, uint16_t *data_len )
{
  uint16_t expected_len;
  uint16_t crc_offset;
  uint16_t expected_crc;
  uint16_t actual_crc;

  if( data_len != NULL )
  {
    *data_len = 0U;
  }

  if( ( frame == NULL ) || ( data_len == NULL ) || ( frame_len < GST_FRAME_LENGTH( 0U ) ) )
  {
    return false;
  }

  if( ( frame[ 0 ] != GST_HEADER_0 ) || ( frame[ 1 ] != GST_HEADER_1 ) || ( frame[ 2 ] != GST_HEADER_2 )
      || ( ( frame[ 3 ] != GST_FC_READ ) && ( frame[ 3 ] != GST_FC_WRITE ) ) )
  {
    return false;
  }

  *data_len = frame[ 6 ];
  expected_len = GST_FRAME_LENGTH( *data_len );

  if( ( frame_len != expected_len ) || ( ( *data_len % 2U ) != 0U ) )
  {
    return false;
  }

  crc_offset = GST_CRC_OFFSET( *data_len );
  if( ( frame[ crc_offset + 2U ] != GST_TAIL_0 ) || ( frame[ crc_offset + 3U ] != GST_TAIL_1 ) )
  {
    return false;
  }

  expected_crc = CRC16( frame, crc_offset );
  actual_crc = ( uint16_t )frame[ crc_offset ] | ( uint16_t )( ( uint16_t )frame[ crc_offset + 1U ] << 8U );

  return expected_crc == actual_crc;
}

static bool ApplyReadFrame( const uint8_t *frame, uint16_t frame_len )
{
  uint16_t data_len;
  uint16_t quantity;
  uint16_t offset;

  if( !ValidateFrame( frame, frame_len, &data_len ) || ( data_len == 0U ) )
  {
    return false;
  }

  quantity = ( uint16_t )( data_len / 2U );
  if( ( quantity > GST_REGISTER_COUNT )
      || ( ( uint16_t )( GST_READ_START_ADDR + quantity - 1U ) > GST_READ_END_ADDR ) )
  {
    return false;
  }

  for( offset = 0U; offset < quantity; offset++ )
  {
    /* GST 디바이스는 Little-endian (Lo, Hi) */
    const uint16_t value = ( uint16_t )frame[ 7U + ( offset * 2U ) ]
                           | ( ( uint16_t )frame[ 7U + ( offset * 2U ) + 1U ] << 8U );

    if( !SharedMap_WriteGlobal( SHARED_MAP_TABLE_HOLDING_REGISTER, ( uint16_t )( GST_READ_START_ADDR + offset ), value ) )
    {
      return false;
    }
  }

  return true;
}

static bool CreateQueues( GstProtocolContext_t *ctx )
{
  if( ctx == NULL )
  {
    return false;
  }

  ctx->rx_queue = osMessageQueueNew( GST_RX_QUEUE_DEPTH, sizeof(GstFrameMessage_t), NULL );
  ctx->tx_queue = osMessageQueueNew( GST_TX_QUEUE_DEPTH, sizeof(GstFrameMessage_t), NULL );

  return ( ctx->rx_queue != NULL ) && ( ctx->tx_queue != NULL );
}

static bool EnqueueTxFrame( GstProtocolContext_t *ctx, const GstFrameMessage_t *message, bool priority )
{
  if( ( ctx == NULL ) || ( ctx->tx_queue == NULL ) || ( message == NULL ) || ( message->length == 0U ) )
  {
    return false;
  }

  if( osMessageQueuePut( ctx->tx_queue, message, 0U, 0U ) == osOK )
  {
    return true;
  }

  if( priority )
  {
    ( void )osMessageQueueReset( ctx->tx_queue );
    return osMessageQueuePut( ctx->tx_queue, message, 0U, 0U ) == osOK;
  }

  return false;
}

static bool QueueWriteFrame( GstProtocolContext_t *ctx, const ModbusSlaveWriteRequest_t *pending,
bool priority,
                             bool expect_response )
{
  GstFrameMessage_t message;

  ( void )memset( &message, 0, sizeof( message ) );
  if( !BuildWriteFrame( pending, message.data, ( uint16_t )sizeof( message.data ), &message.length ) )
  {
    return false;
  }
  message.expect_response = expect_response ? 1U : 0U;

  return EnqueueTxFrame( ctx, &message, priority );
}

static bool CompleteModbusPending( GstProtocolContext_t *ctx, ModbusSlaveWriteRequest_t *pending,
bool success )
{
  uint8_t ack_response[ MODBUS_SLAVE_MAX_ACK_LENGTH ];
  uint16_t ack_len = 0U;

  ( void )memset( ack_response, 0, sizeof( ack_response ) );

  if( !ModbusSlave_ProcessWriteRequest( pending, success, ack_response, ( uint16_t )sizeof( ack_response ), &ack_len ) )
  {
    return false;
  }

  if( ( ack_len > 0U ) && ( ctx != NULL ) && ( ctx->ack_callback != NULL ) )
  {
    LOGHEX( g_log_uart4, "[UART4 TX] ", ack_response, ack_len );
    return ctx->ack_callback( ack_response, ack_len );
  }

  return true;
}

static void CompletePendingTransfer( GstProtocolContext_t *ctx, bool success )
{
  ModbusSlaveWriteRequest_t pending_copy;
  bool has_pending = false;

  if( ctx == NULL )
  {
    return;
  }

  ( void )memset( &pending_copy, 0, sizeof( pending_copy ) );
  if( ctx->pending_write.valid != 0U )
  {
    ( void )memcpy( &pending_copy, &ctx->pending_write, sizeof( pending_copy ) );
    has_pending = true;
  }

  ctx->pending_waiting_response = 0U;
  ctx->pending_tx_queued = 0U;
  ctx->pending_active = 0U;
  ctx->pending_success = success ? 1U : 0U;
  ctx->pending_done = 1U;
  ( void )memset( &ctx->pending_write, 0, sizeof( ctx->pending_write ) );

  if( has_pending )
  {
    ( void )CompleteModbusPending( ctx, &pending_copy, success );
  }
}

static void ServicePendingTransfer( GstProtocolContext_t *ctx, uint32_t now )
{
  const uint16_t timeout_ms = GetTimeoutMs();

  if( ( ctx == NULL ) || ( ctx->pending_active == 0U ) )
  {
    return;
  }

  if( ( ctx->pending_waiting_response == 0U ) && ( ctx->pending_tx_queued == 0U ) )
  {
    if( ctx->pending_attempts >= GetRetryCount() )
    {
      ( void )SharedMap_SetCommStatus( COMM_DEVICE_GROUP_GST, false );
      CompletePendingTransfer( ctx, false );
      return;
    }

    if( QueueWriteFrame( ctx, &ctx->pending_write, true, true ) )
    {
      ctx->pending_tx_queued = 1U;
      ctx->pending_attempts++;
    }
    else
    {
      ( void )SharedMap_SetCommStatus( COMM_DEVICE_GROUP_GST, false );
      CompletePendingTransfer( ctx, false );
    }
    return;
  }

  if( ( ctx->pending_waiting_response != 0U ) && ( ( uint32_t )( now - ctx->pending_started_ms ) >= timeout_ms ) )
  {
    ( void )SharedMap_SetCommStatus( COMM_DEVICE_GROUP_GST, false );
    ctx->pending_waiting_response = 0U;
    ctx->pending_tx_queued = 0U;
  }
}

static void ServicePeriodicTransfer( GstProtocolContext_t *ctx, uint32_t now )
{
  if( ( ctx == NULL ) || ( ctx->pending_active != 0U ) )
  {
    return;
  }

  if( !IsElapsed( now, ctx->last_transfer_ms, ctx->period_ms ) )
  {
    return;
  }

  if( QueueWriteFrame( ctx, NULL, false, false ) )
  {
    ctx->last_transfer_ms = now;
  }
}

static void ServiceLinkTimeout( GstProtocolContext_t *ctx, uint32_t now )
{
  uint32_t timeout_ms;

  if( ctx == NULL )
  {
    return;
  }

  timeout_ms = ( uint32_t )GetTimeoutMs() * ( uint32_t )GetRetryCount();
  if( timeout_ms < ( uint32_t )ctx->period_ms )
  {
    timeout_ms = ( uint32_t )ctx->period_ms * ( uint32_t )GetRetryCount();
  }

  if( ( ctx->last_rx_ms != 0U ) && ( ( uint32_t )( now - ctx->last_rx_ms ) > timeout_ms ) )
  {
    ( void )SharedMap_SetCommStatus( COMM_DEVICE_GROUP_GST, false );
    ctx->last_rx_ms = now;
  }
}

void GstProtocol_InitContext( GstProtocolContext_t *ctx, UART_HandleTypeDef *huart, uint16_t period_ms,
                              void (*set_tx_mode)( void ), void (*set_rx_mode)( void ) )
{
  if( ctx == NULL )
  {
    return;
  }

  ( void )memset( ctx, 0, sizeof( *ctx ) );
  ctx->huart = huart;
  ctx->period_ms = period_ms;
  ctx->set_tx_mode = set_tx_mode;
  ctx->set_rx_mode = set_rx_mode;
  ctx->last_transfer_ms = HAL_GetTick();
  ctx->last_rx_ms = HAL_GetTick();

  if( ctx->set_tx_mode != NULL )
  {
    ctx->set_tx_mode();
  }

  ( void )CreateQueues( ctx );
}

bool GstProtocol_IsReady( const GstProtocolContext_t *ctx )
{
  return ( ctx != NULL ) && ( ctx->rx_queue != NULL ) && ( ctx->tx_queue != NULL );
}

void GstProtocol_SetAckCallback( GstProtocolContext_t *ctx, GstProtocolAckCallback_t callback )
{
  if( ctx != NULL )
  {
    ctx->ack_callback = callback;
  }
}

bool GstProtocol_ForwardWrite( GstProtocolContext_t *ctx, ModbusSlaveWriteRequest_t *pending )
{
  if( ( ctx == NULL ) || !IsGlobalHoldingPending( pending ) )
  {
    return false;
  }

  if( PendingRangeWithin( pending, GST_RESERVED_START_ADDR, GST_RESERVED_END_ADDR ) )
  {
    return CompleteModbusPending( ctx, pending, true );
  }

  if( !PendingRangeWithin( pending, GST_WRITE_START_ADDR, GST_WRITE_END_ADDR ) )
  {
    return false;
  }

  if( ( ctx->rx_queue == NULL ) || ( ctx->tx_queue == NULL ) || ( ctx->pending_active != 0U ) )
  {
    return false;
  }

  Uart_ClearData( ctx->huart );
  ( void )osMessageQueueReset( ctx->rx_queue );
  ( void )osMessageQueueReset( ctx->tx_queue );
  ctx->reassembly_len = 0U;

  ( void )memcpy( &ctx->pending_write, pending, sizeof( ctx->pending_write ) );
  ctx->pending_write.valid = 0U;  /* ACK는 호출자가 이미 전송, 중복 ACK 방지 */
  ctx->pending_active = 1U;
  ctx->pending_tx_queued = 0U;
  ctx->pending_waiting_response = 0U;
  ctx->pending_done = 0U;
  ctx->pending_success = 0U;
  ctx->pending_attempts = 0U;
  ctx->pending_started_ms = HAL_GetTick();

  return true;
}

void GstProtocol_RxStep( GstProtocolContext_t *ctx )
{
  uint8_t fragment[ GST_MAX_FRAME_LENGTH ];
  uint16_t fragment_len = 0U;
  uint16_t expected_len;
  uint32_t now;

  if( ( ctx == NULL ) || ( ctx->huart == NULL ) || ( ctx->rx_queue == NULL ) )
  {
    osDelay( GST_RX_POLL_TIMEOUT_MS );
    return;
  }

  now = HAL_GetTick();

  ( void )memset( fragment, 0, sizeof( fragment ) );
  if( !Uart_GetData( ctx->huart, fragment, ( uint16_t )sizeof( fragment ), &fragment_len ) )
  {
    if( ( ctx->reassembly_len > 0U ) && IsElapsed( now, ctx->reassembly_start_ms, GST_RX_REASSEMBLY_TIMEOUT_MS ) )
    {
      ctx->reassembly_len = 0U;
    }
    return;
  }

  LOGHEX( g_log_gst, "[GST RX] ", fragment, fragment_len );

  if( ctx->reassembly_len == 0U )
  {
    ctx->reassembly_start_ms = now;
  }

  if( ( ctx->reassembly_len + fragment_len ) > ( uint16_t )sizeof( ctx->reassembly_buf ) )
  {
    ctx->reassembly_len = 0U;
  }

  ( void )memcpy( &ctx->reassembly_buf[ ctx->reassembly_len ], fragment, fragment_len );
  ctx->reassembly_len = ( uint16_t )( ctx->reassembly_len + fragment_len );

  /* GST 헤더 시작 위치까지 건너뜀 */
  while( ( ctx->reassembly_len >= 1U ) && ( ctx->reassembly_buf[ 0 ] != GST_HEADER_0 ) )
  {
    ( void )memmove( ctx->reassembly_buf, &ctx->reassembly_buf[ 1 ], ( size_t )( ctx->reassembly_len - 1U ) );
    ctx->reassembly_len--;
  }

  if( ctx->reassembly_len < ( uint16_t )GST_FRAME_LENGTH( 0U ) )
  {
    return;
  }

  expected_len = ( uint16_t )GST_FRAME_LENGTH( ctx->reassembly_buf[ 6 ] );
  if( expected_len > ( uint16_t )sizeof( ctx->reassembly_buf ) )
  {
    ctx->reassembly_len = 0U;
    return;
  }

  if( ctx->reassembly_len < expected_len )
  {
    return;
  }

  {
    GstFrameMessage_t message;
    ( void )memset( &message, 0, sizeof( message ) );
    ( void )memcpy( message.data, ctx->reassembly_buf, expected_len );
    message.length = expected_len;
    LogFrameSummary( "[GST RX]", message.data, message.length );

    if( osMessageQueuePut( ctx->rx_queue, &message, 0U, 0U ) != osOK )
    {
      ( void )osMessageQueueReset( ctx->rx_queue );
      ( void )osMessageQueuePut( ctx->rx_queue, &message, 0U, 0U );
    }
  }

  if( ctx->reassembly_len > expected_len )
  {
    ( void )memmove( ctx->reassembly_buf, &ctx->reassembly_buf[ expected_len ],
                     ( size_t )( ctx->reassembly_len - expected_len ) );
    ctx->reassembly_len = ( uint16_t )( ctx->reassembly_len - expected_len );
  }
  else
  {
    ctx->reassembly_len = 0U;
  }
}

void GstProtocol_ProtocolStep( GstProtocolContext_t *ctx )
{
  GstFrameMessage_t message;
  uint32_t now;

  if( ( ctx == NULL ) || ( ctx->rx_queue == NULL ) || ( ctx->tx_queue == NULL ) )
  {
    osDelay( 1U );
    return;
  }

  now = HAL_GetTick();
  while( osMessageQueueGet( ctx->rx_queue, &message, NULL, 0U ) == osOK )
  {
    if( ApplyReadFrame( message.data, message.length ) )
    {
      ctx->last_rx_ms = now;
      ( void )SharedMap_SetCommStatus( COMM_DEVICE_GROUP_GST, true );
      if( ctx->pending_waiting_response != 0U )
      {
        CompletePendingTransfer( ctx, true );
      }
    }
    else
    {
      ( void )SharedMap_SetCommStatus( COMM_DEVICE_GROUP_GST, false );
    }
  }

  ServicePendingTransfer( ctx, now );
  ServicePeriodicTransfer( ctx, now );
  ServiceLinkTimeout( ctx, now );
}

void GstProtocol_TxStep( GstProtocolContext_t *ctx )
{
  GstFrameMessage_t message;
  HAL_StatusTypeDef status;
  uint8_t attempt = 0U;

  if( ( ctx == NULL ) || ( ctx->huart == NULL ) || ( ctx->tx_queue == NULL ) )
  {
    osDelay( 1U );
    return;
  }

  if( osMessageQueueGet( ctx->tx_queue, &message, NULL, osWaitForever ) != osOK )
  {
    return;
  }

  LOGHEX( g_log_gst, "[GST TX] ", message.data, message.length );
  LogFrameSummary( "[GST TX]", message.data, message.length );

  do
  {
    /* RS422 full-duplex: keep RX DMA active and DE asserted. */
    if( ctx->set_tx_mode != NULL )
    {
      ctx->set_tx_mode();
    }

    if( message.expect_response != 0U )
    {
      ctx->reassembly_len = 0U;
    }

    status = HAL_UART_Transmit( ctx->huart, message.data, message.length, GetTimeoutMs() );

    if( status != HAL_OK )
    {
      ( void )SharedMap_SetCommStatus( COMM_DEVICE_GROUP_GST, false );
      osDelay( GST_TX_RETRY_DELAY_MS );
    }
    attempt++;
  } while( ( status != HAL_OK ) && ( attempt < GetRetryCount() ) );

  if( status != HAL_OK )
  {
    if( ( message.expect_response != 0U ) && ( ctx->pending_active != 0U ) )
    {
      CompletePendingTransfer( ctx, false );
    }
    return;
  }

  if( ( message.expect_response != 0U ) && ( ctx->pending_active != 0U ) )
  {
    ctx->pending_started_ms = HAL_GetTick();
    ctx->pending_tx_queued = 0U;
    ctx->pending_waiting_response = 1U;
  }
}
