#include "a_application.h"

#include "a_at25sf041b.h"
#include "a_bootloader_link.h"
#include "a_comm_config.h"
#include "a_desired_state_mirror.h"
#include "a_gst_protocol.h"
#include "a_modbus_master.h"
#include "a_modbus_slave.h"
#include "a_plc_group_map.h"
#include "a_shared_map.h"
#include "a_test_link.h"
#include "a_uart.h"
#include "cmsis_os.h"
#include "main.h"

/* main.c에서 정의되는 IWDG 핸들. UART4_Task tick마다 먹여서(refresh) 앱이
 * 정상적으로 살아있는 동안은 워치독 리셋이 걸리지 않게 한다. 만약 이 task가
 * (또는 시스템 전체가) 멈추면 정해진 시간 안에 refresh가 안 되어 MCU가
 * 리셋되고, 재부팅 시 부트로더가 PENDING_TEST 미확정 상태를 보고 이전
 * 슬롯으로 자동 롤백한다 - IWDG는 A/B 안전장치의 마지막 방어선이다. */
extern IWDG_HandleTypeDef hiwdg;

#include <stdbool.h>
#include <string.h>
#include "a_log.h"

#define APP_UART4_RX_MAX           256U
#define APP_UART4_TX_MAX           256U
#define APP_UART_TX_TIMEOUT_MS     60U
#define APP_TASK_LOOP_DELAY_MS     1U
#define APP_UART4_WATCHDOG_TIMEOUT_MS 5000U

#define CONFIG_DIO_ID_START_ADDR       110U
#define CONFIG_HEATING_ID_START_ADDR   112U
#define CONFIG_GAS_ID_START_ADDR       115U
#define CONFIG_GAS_TYPE_START_ADDR     118U
#define CONFIG_APPLY_TRIGGER_ADDR      121U
#define CONFIG_APPLY_RESULT_ADDR       122U
#define DIO_OUTPUT_HOLDING_ADDR        129U

#define GST_WRITE_START_ADDR           201U
#define GST_WRITE_END_ADDR             241U
#define GST_RESERVED_START_ADDR        283U
#define GST_RESERVED_END_ADDR          322U

extern UART_HandleTypeDef hlpuart1;
extern UART_HandleTypeDef huart1;
extern UART_HandleTypeDef huart2;
extern UART_HandleTypeDef huart3;
extern UART_HandleTypeDef huart4;

static ModbusMasterPortContext_t s_dio_ctx;
static ModbusMasterPortContext_t s_heating_ctx;
static ModbusMasterPortContext_t s_gas_ctx;
static GstProtocolContext_t s_gst_ctx;

static uint8_t s_uart4_rx[ APP_UART4_RX_MAX ];
static uint8_t s_uart4_tx[ APP_UART4_TX_MAX ];

static osThreadId_t s_uart4_task_handle;
static osThreadId_t s_lpuart1_task_handle;
static osThreadId_t s_usart2_task_handle;
static osThreadId_t s_usart3_task_handle;
static osThreadId_t s_usart1_rx_task_handle;
static osThreadId_t s_usart1_protocol_task_handle;
static osThreadId_t s_usart1_tx_task_handle;
static osMutexId_t s_uart4_tx_mutex;
static uint32_t s_uart4_last_rx_ms;
static bool s_uart4_watchdog_latched;
static uint32_t s_comm_monitor_started_ms;
static uint32_t s_comm_monitor_revision;
static bool s_comm_monitor_grace_active;
static uint32_t s_comm_recovery_started_ms;
static bool s_comm_recovery_timing;
static bool s_comm_fault_active;

static const osThreadAttr_t s_uart4_task_attributes = { .name = "UART4Task", .stack_size = 512U * 4U, .priority =
                                                            ( osPriority_t )osPriorityNormal, };

static const osThreadAttr_t s_lpuart1_task_attributes = { .name = "LPUART1Task", .stack_size = 512U * 4U, .priority =
                                                              ( osPriority_t )osPriorityNormal, };

static const osThreadAttr_t s_usart2_task_attributes = { .name = "USART2Task", .stack_size = 512U * 4U, .priority =
                                                             ( osPriority_t )osPriorityNormal, };

static const osThreadAttr_t s_usart3_task_attributes = { .name = "USART3Task", .stack_size = 512U * 4U, .priority =
                                                             ( osPriority_t )osPriorityNormal, };

static const osThreadAttr_t s_usart1_rx_task_attributes = { .name = "USART1RxTask", .stack_size = 384U * 4U, .priority =
                                                                ( osPriority_t )osPriorityNormal, };

static const osThreadAttr_t s_usart1_protocol_task_attributes = {
    .name = "USART1ProtocolTask", .stack_size = 512U * 4U, .priority = ( osPriority_t )osPriorityNormal, };

static const osThreadAttr_t s_usart1_tx_task_attributes = { .name = "USART1TxTask", .stack_size = 384U * 4U, .priority =
                                                                ( osPriority_t )osPriorityNormal, };

static const osMutexAttr_t s_uart4_tx_mutex_attributes = { .name = "UART4TxMutex", };

static void Uart4_SetTxMode( void )
{
  HAL_GPIO_WritePin( UART4_RE_GPIO_Port, UART4_RE_Pin, GPIO_PIN_RESET );
  HAL_GPIO_WritePin( UART4_DE_GPIO_Port, UART4_DE_Pin, GPIO_PIN_SET );
}

static void Uart4_SetRxMode( void )
{
  HAL_GPIO_WritePin( UART4_DE_GPIO_Port, UART4_DE_Pin, GPIO_PIN_RESET );
  HAL_GPIO_WritePin( UART4_RE_GPIO_Port, UART4_RE_Pin, GPIO_PIN_SET );
}

static void Lpuart1_SetTxMode( void )
{
  HAL_GPIO_WritePin( LPUART1_DE_GPIO_Port, LPUART1_DE_Pin, GPIO_PIN_SET );
}

static void Lpuart1_SetRxMode( void )
{
  HAL_GPIO_WritePin( LPUART1_DE_GPIO_Port, LPUART1_DE_Pin, GPIO_PIN_RESET );
}

static void Usart2_SetTxMode( void )
{
  HAL_GPIO_WritePin( USART2_DE_GPIO_Port, USART2_DE_Pin, GPIO_PIN_SET );
}

static void Usart2_SetRxMode( void )
{
  HAL_GPIO_WritePin( USART2_DE_GPIO_Port, USART2_DE_Pin, GPIO_PIN_RESET );
}

static void Usart3_SetTxMode( void )
{
  HAL_GPIO_WritePin( USART3_DE_GPIO_Port, USART3_DE_Pin, GPIO_PIN_SET );
}

static void Usart3_SetRxMode( void )
{
  HAL_GPIO_WritePin( USART3_DE_GPIO_Port, USART3_DE_Pin, GPIO_PIN_RESET );
}

static void Usart1_SetTxMode( void )
{
  HAL_GPIO_WritePin( USART1_DE_GPIO_Port, USART1_DE_Pin, GPIO_PIN_SET );
}

static void Usart1_SetRxMode( void )
{
  HAL_GPIO_WritePin( USART1_DE_GPIO_Port, USART1_DE_Pin, GPIO_PIN_RESET );
}

static bool Uart4_Transmit( const uint8_t *frame, uint16_t length )
{
  bool ok;
  bool locked = false;

  if( ( frame == NULL ) || ( length == 0U ) )
  {
    return false;
  }

  if( s_uart4_tx_mutex != NULL )
  {
    if( osMutexAcquire( s_uart4_tx_mutex, osWaitForever ) != osOK )
    {
      return false;
    }
    locked = true;
  }
  /* Modbus RTU 최소 프레임 간격 t3.5(115200bps 기준 1.75ms) 보장용 지연 */
  osDelay( 2U );
  Uart_StopReceive( &huart4 );
  ok = Uart_TransmitBlocking( &huart4, frame, length, APP_UART_TX_TIMEOUT_MS );
  ( void )Uart_StartReceive( &huart4 );

  if( locked )
  {
    ( void )osMutexRelease( s_uart4_tx_mutex );
  }

  return ok;
}

static bool IsGstForwardRange( const ModbusSlaveWriteRequest_t *req )
{
  uint32_t end_address;

  if( ( req == NULL ) || ( req->table != SHARED_MAP_TABLE_HOLDING_REGISTER ) || ( req->quantity == 0U ) )
  {
    return false;
  }

  end_address = ( uint32_t )req->address + req->quantity - 1U;

  return ( ( req->address >= GST_WRITE_START_ADDR ) && ( end_address <= GST_WRITE_END_ADDR ) )
         || ( ( req->address >= GST_RESERVED_START_ADDR ) && ( end_address <= GST_RESERVED_END_ADDR ) );
}

static void HandleWriteRequest( ModbusSlaveWriteRequest_t *req )
{
  uint8_t ack_buf[ MODBUS_SLAVE_MAX_ACK_LENGTH ];
  uint16_t ack_len = 0U;

  if( ( req == NULL ) || ( req->valid == 0U ) )
  {
    return;
  }

  if( req->is_global != 0U )
  {
    ModbusSlaveWriteRequest_t gst_req;

    ( void )memcpy( &gst_req, req, sizeof( gst_req ) );
    /* SharedMap 즉시 커밋 + PLC 즉시 ACK */
    ( void )memset( ack_buf, 0, sizeof( ack_buf ) );
    if( !ModbusSlave_ProcessWriteRequest( req, true, ack_buf, sizeof( ack_buf ), &ack_len ) )
    {
      return;
    }
    LOGHEX( g_log_uart4, "[UART4 TX] ", ack_buf, ack_len );
    ( void )Uart4_Transmit( ack_buf, ack_len );

    if( IsGstForwardRange( &gst_req ) )
    {
      /* GST 전달은 비동기 best-effort */
      ( void )GstProtocol_ForwardWrite( &s_gst_ctx, &gst_req );
    }
    return;
  }

  /* SharedMap 즉시 커밋 + ACK 프레임 생성 */
  ( void )memset( ack_buf, 0, sizeof( ack_buf ) );
  if( !ModbusSlave_ProcessWriteRequest( req, true, ack_buf, sizeof( ack_buf ), &ack_len ) )
  {
    return;
  }

  /* 슬레이브 응답을 기다리지 않고 PLC에 즉시 ACK 전송 */
  LOGHEX( g_log_uart4, "[UART4 TX] ", ack_buf, ack_len );
  ( void )Uart4_Transmit( ack_buf, ack_len );

  /* 슬레이브에 실제 값 전달은 서브 task에서 처리 (best-effort) */
  req->valid = 1U;
  ( void )ModbusMaster_ForwardWrite( req );
}

static void EnqueueDioOutputZero( uint8_t slave_id )
{
  ModbusSlaveWriteRequest_t req;

  ( void )memset( &req, 0, sizeof( req ) );
  req.slave_id = slave_id;
  req.function = MODBUS_FC_WRITE_MULTIPLE_REGS;
  req.table = SHARED_MAP_TABLE_HOLDING_REGISTER;
  req.address = DIO_OUTPUT_HOLDING_ADDR;
  req.quantity = 1U;
  req.values[ 0 ] = 0U;
  req.valid = 1U;

  if( SharedMap_SetPendingWrite( req.slave_id, req.table, req.address, req.values[ 0 ] )
      && SharedMap_CommitWrite( req.slave_id, req.table, req.address ) )
  {
    ( void )ModbusMaster_ForwardWrite( &req );
  }
}

static void EnqueueDioFailSafeOutputs( void )
{
  ActiveIdList_t dio_ids;
  uint8_t index;

  if( !CommConfig_GetActiveIds( COMM_DEVICE_GROUP_DIO, &dio_ids ) )
  {
    return;
  }

  for( index = 0U; index < dio_ids.count; index++ )
  {
    EnqueueDioOutputZero( dio_ids.ids[ index ] );
  }
}

static void ServiceUart4Watchdog( void )
{
#if APP_UART4_WATCHDOG_TIMEOUT_MS > 0U
  const uint32_t now = HAL_GetTick();

  if( ( s_uart4_watchdog_latched == false )
      && ( ( uint32_t )( now - s_uart4_last_rx_ms ) >= ( uint32_t )APP_UART4_WATCHDOG_TIMEOUT_MS ) )
  {
    EnqueueDioFailSafeOutputs();
    s_uart4_watchdog_latched = true;
  }
#endif
}

static void ServiceCommFaultOutputs( void )
{
  const uint32_t now = HAL_GetTick();
  const uint32_t revision = CommConfig_GetRevision();
  bool all_healthy = ( ( uint32_t )( now - s_uart4_last_rx_ms ) < COMM_FAULT_DEBOUNCE_MS );
  uint8_t group;
  uint8_t index;
  ActiveIdList_t ids;

  if( revision != s_comm_monitor_revision )
  {
    s_comm_monitor_revision = revision;
    s_comm_monitor_started_ms = now;
    s_comm_monitor_grace_active = true;
    s_comm_recovery_timing = false;
  }
  if( s_comm_monitor_grace_active
      && ( ( uint32_t )( now - s_comm_monitor_started_ms ) >= COMM_FAULT_DEBOUNCE_MS ) )
  {
    s_comm_monitor_grace_active = false;
  }
  if( !s_comm_monitor_grace_active )
  {
    for( group = 0U; group < ( uint8_t )COMM_DEVICE_GROUP_GST; group++ )
    {
      if( !CommConfig_GetActiveIds( ( CommDeviceGroup_t )group, &ids ) )
      {
        all_healthy = false;
        continue;
      }
      for( index = 0U; index < ids.count; index++ )
      {
        if( ( group == ( uint8_t )COMM_DEVICE_GROUP_GAS )
            && ( CommConfig_GetGasTypeByIndex( index ) == COMM_GAS_TYPE_NONE ) )
        {
          continue;
        }
        if( !SharedMap_HasRecentCommSuccess( ( CommDeviceGroup_t )group, index, now, COMM_FAULT_DEBOUNCE_MS ) )
        {
          all_healthy = false;
        }
      }
    }
    if( !SharedMap_HasRecentCommSuccess( COMM_DEVICE_GROUP_GST, 0U, now, COMM_FAULT_DEBOUNCE_MS ) )
    {
      all_healthy = false;
    }

    if( !all_healthy )
    {
      s_comm_fault_active = true;
      s_comm_recovery_timing = false;
    }
    else if( s_comm_fault_active )
    {
      if( !s_comm_recovery_timing )
      {
        s_comm_recovery_started_ms = now;
        s_comm_recovery_timing = true;
      }
      else if( ( uint32_t )( now - s_comm_recovery_started_ms ) >= COMM_FAULT_DEBOUNCE_MS )
      {
        s_comm_fault_active = false;
        s_comm_recovery_timing = false;
      }
    }
  }

  /* Alarm indication only: polling, restore and the PLC DIO fail-safe keep running. */
  /* Energize the NO relay only after the grace period and a healthy check.
   * Fault, startup and loss of module power leave the PLC contact open. */
  if( TestLink_IsFaultOverrideActive() )
  {
    HAL_GPIO_WritePin( FAULT_GPIO_Port, FAULT_Pin, TestLink_GetFaultState() );
  }
  else
  {
    HAL_GPIO_WritePin( FAULT_GPIO_Port, FAULT_Pin,
                       ( s_comm_monitor_grace_active || s_comm_fault_active ) ? GPIO_PIN_RESET : GPIO_PIN_SET );
  }
  /* Both LEDs are on GPIOB; update together to avoid a mixed indication. */
  if( s_comm_fault_active )
  {
    GPIOB->BSRR = ( uint32_t )MODULE_ERROR_Pin | ( ( uint32_t )MODULE_RUN_Pin << 16U );
  }
  else
  {
    GPIOB->BSRR = ( uint32_t )MODULE_RUN_Pin | ( ( uint32_t )MODULE_ERROR_Pin << 16U );
  }
}

static void ServiceHeatingPulseTimeout( void )
{
  ModbusSlaveWriteRequest_t forwards[ PLC_GROUP_POLL_MAX_FORWARDS ];
  const uint8_t forward_count = PlcGroupMap_Poll( forwards, ( uint8_t )PLC_GROUP_POLL_MAX_FORWARDS );
  uint8_t index;

  for( index = 0U; index < forward_count; index++ )
  {
    ( void )ModbusMaster_ForwardWrite( &forwards[ index ] );
  }
}

static bool ProcessUart4Request( void )
{
  uint16_t rx_len = 0U;
  uint16_t tx_len = 0U;
  uint16_t address = 0U;
  uint16_t data = 0U;
  ModbusSlaveWriteRequest_t write_req;
  ModbusSlaveResult_t result;

  ( void )memset( &write_req, 0, sizeof( write_req ) );

  if( !Uart_GetData( &huart4, s_uart4_rx, ( uint16_t )sizeof( s_uart4_rx ), &rx_len ) )
  {
    return false;
  }

  if( rx_len >= 4U )
  {
    address = Modbus_GetU16Be( &s_uart4_rx[ 2 ] );
  }
  if( ( s_uart4_rx[ 1 ] == MODBUS_FC_WRITE_MULTIPLE_REGS ) && ( rx_len >= 9U ) )
  {
    data = Modbus_GetU16Be( &s_uart4_rx[ 7 ] );
  }
  else if( rx_len >= 6U )
  {
    data = Modbus_GetU16Be( &s_uart4_rx[ 4 ] );
  }
  LOG( g_log_uart4, "[UART4] rx=%u id=0x%02X fc=0x%02X addr=%u data=%u\r\n", rx_len, s_uart4_rx[ 0 ], s_uart4_rx[ 1 ],
       address, data );
  LOGHEX( g_log_uart4, "[UART4 RX] ", s_uart4_rx, rx_len );

  /* PROTO-012: PLC 4-그룹 통합 맵 요청(Slave ID 1~8)을 우선 처리한다.
   * 그룹 요청이 아니면 기존 slave 경로로 넘긴다. */
  {
    PlcGroupWriteActions_t group_actions;
    PlcGroupResult_t group_result;
    uint8_t fwd_index;

    ( void )memset( s_uart4_tx, 0, sizeof( s_uart4_tx ) );
    group_result = PlcGroupMap_HandleRequest( s_uart4_rx, rx_len, s_uart4_tx, ( uint16_t )sizeof( s_uart4_tx ), &tx_len,
                                              &group_actions );
    if( group_result == PLC_GROUP_RESULT_RESPONSE_READY )
    {
      LOGHEX( g_log_uart4, "[UART4 TX] ", s_uart4_tx, tx_len );
      ( void )Uart4_Transmit( s_uart4_tx, tx_len );

      /* PLC ACK 이후 하위 장치/GST peer 전달은 best-effort */
      for( fwd_index = 0U; fwd_index < group_actions.forward_count; fwd_index++ )
      {
        ( void )ModbusMaster_ForwardWrite( &group_actions.forwards[ fwd_index ] );
      }
      if( group_actions.gst_forward != 0U )
      {
        ( void )GstProtocol_ForwardWrite( &s_gst_ctx, &group_actions.gst_request );
      }
      return true;
    }
    if( group_result == PLC_GROUP_RESULT_NO_RESPONSE )
    {
      return false;
    }
  }

  ( void )memset( s_uart4_tx, 0, sizeof( s_uart4_tx ) );
  result = ModbusSlave_HandleRequest( s_uart4_rx, rx_len, s_uart4_tx, ( uint16_t )sizeof( s_uart4_tx ), &tx_len, &write_req );
  if( result == MODBUS_SLAVE_RESULT_RESPONSE_READY )
  {
    LOGHEX( g_log_uart4, "[UART4 TX] ", s_uart4_tx, tx_len );
    ( void )Uart4_Transmit( s_uart4_tx, tx_len );
  }
  else if( result == MODBUS_SLAVE_RESULT_WRITE_PENDING )
  {
    LOG( g_log_slave, "[SLAVE] write request id=0x%02X addr=%u\r\n", write_req.slave_id, write_req.address );
    HandleWriteRequest( &write_req );
  }

  return result != MODBUS_SLAVE_RESULT_NO_RESPONSE;
}

static void ServiceConfigApply( void )
{
  uint16_t trigger = 0U;
  uint16_t dio_ids[ MAX_ACTIVE_DIO_ID_COUNT ];
  uint16_t heating_ids[ MAX_ACTIVE_HEATING_ID_COUNT ];
  uint16_t gas_ids[ MAX_ACTIVE_GAS_ID_COUNT ];
  uint16_t gas_types[ MAX_ACTIVE_GAS_ID_COUNT ];
  uint16_t result;
  uint8_t index;

  if( !SharedMap_ReadGlobal( SHARED_MAP_TABLE_HOLDING_REGISTER, CONFIG_APPLY_TRIGGER_ADDR, &trigger )
      || ( trigger != 1U ) )
  {
    return;
  }

  for( index = 0U; index < MAX_ACTIVE_DIO_ID_COUNT; index++ )
  {
    if( !SharedMap_ReadGlobal( SHARED_MAP_TABLE_HOLDING_REGISTER,
                               ( uint16_t )( CONFIG_DIO_ID_START_ADDR + index ), &dio_ids[ index ] ) )
    {
      result = COMM_CONFIG_RESULT_INTERNAL_ERROR;
      goto finish;
    }
  }

  for( index = 0U; index < MAX_ACTIVE_HEATING_ID_COUNT; index++ )
  {
    if( !SharedMap_ReadGlobal( SHARED_MAP_TABLE_HOLDING_REGISTER,
                               ( uint16_t )( CONFIG_HEATING_ID_START_ADDR + index ), &heating_ids[ index ] ) )
    {
      result = COMM_CONFIG_RESULT_INTERNAL_ERROR;
      goto finish;
    }
  }

  for( index = 0U; index < MAX_ACTIVE_GAS_ID_COUNT; index++ )
  {
    if( !SharedMap_ReadGlobal( SHARED_MAP_TABLE_HOLDING_REGISTER,
                               ( uint16_t )( CONFIG_GAS_ID_START_ADDR + index ), &gas_ids[ index ] )
        || !SharedMap_ReadGlobal( SHARED_MAP_TABLE_HOLDING_REGISTER,
                                  ( uint16_t )( CONFIG_GAS_TYPE_START_ADDR + index ), &gas_types[ index ] ) )
    {
      result = COMM_CONFIG_RESULT_INTERNAL_ERROR;
      goto finish;
    }
  }

  {
    ActiveIdList_t old_heating_ids;

    /* 국번이 재배정되는 jacket 슬롯은 EEPROM Save/ALARM Reset rising-edge 상태를
     * 초기화한다 - 안 하면 새 장치에 대한 첫 정상 write가 씹히거나, 이전 값이
     * 의도치 않게 새 장치로 재전송될 수 있다. */
    if( CommConfig_GetActiveIds( COMM_DEVICE_GROUP_HEATING, &old_heating_ids ) )
    {
      for( index = 0U; index < MAX_ACTIVE_HEATING_ID_COUNT; index++ )
      {
        const uint8_t old_id = ( index < old_heating_ids.count ) ? old_heating_ids.ids[ index ] : 0U;

        if( old_id != ( uint8_t )heating_ids[ index ] )
        {
          PlcGroupMap_ResetHeatingEdgeState( index );
        }
      }
    }
  }

  result = CommConfig_ApplyDeviceSettings( dio_ids, heating_ids, gas_ids, gas_types );

finish:
  ( void )SharedMap_WriteGlobal( SHARED_MAP_TABLE_HOLDING_REGISTER, CONFIG_APPLY_RESULT_ADDR, result );
  ( void )SharedMap_WriteGlobal( SHARED_MAP_TABLE_HOLDING_REGISTER, CONFIG_APPLY_TRIGGER_ADDR, 0U );
}

static void ReinitDioContext( void )
{
  const CommConfig_t *config = CommConfig_Get();

  SharedMap_ResetCommHealth( COMM_DEVICE_GROUP_DIO );
  ModbusMaster_InitContext( &s_dio_ctx, &hlpuart1, MODBUS_MASTER_PORT_LPUART1, COMM_DEVICE_GROUP_DIO,
                            config->lpuart1_poll_period_ms, Lpuart1_SetTxMode, Lpuart1_SetRxMode );
}

static void ReinitHeatingContext( void )
{
  const CommConfig_t *config = CommConfig_Get();

  SharedMap_ResetCommHealth( COMM_DEVICE_GROUP_HEATING );
  ModbusMaster_InitContext( &s_heating_ctx, &huart2, MODBUS_MASTER_PORT_USART2, COMM_DEVICE_GROUP_HEATING,
                            config->usart2_poll_period_ms, Usart2_SetTxMode, Usart2_SetRxMode );
}

static void ReinitGasContext( void )
{
  const CommConfig_t *config = CommConfig_Get();

  SharedMap_ResetCommHealth( COMM_DEVICE_GROUP_GAS );
  ModbusMaster_InitContext( &s_gas_ctx, &huart3, MODBUS_MASTER_PORT_USART3, COMM_DEVICE_GROUP_GAS,
                            config->usart3_poll_period_ms, Usart3_SetTxMode, Usart3_SetRxMode );
}

void Application_Init( void )
{
  const CommConfig_t *config;

  CommConfig_InitDefaults();
  SharedMap_Init();
  SharedMap_SetCurrentTimeMs( HAL_GetTick() );
  s_uart4_last_rx_ms = HAL_GetTick();
  s_uart4_watchdog_latched = false;

  s_comm_monitor_started_ms = s_uart4_last_rx_ms;
  s_comm_monitor_revision = CommConfig_GetRevision();
  s_comm_monitor_grace_active = true;
  s_comm_recovery_started_ms = 0U;
  s_comm_recovery_timing = false;
  s_comm_fault_active = false;

  Uart4_SetRxMode();
  Lpuart1_SetRxMode();
  Usart1_SetRxMode();
  Usart2_SetRxMode();
  Usart3_SetRxMode();
  Uart_Init();
  /* IT 송신 완료(TC) 시 ISR에서 DE를 내릴 수 있도록 방향 제어 콜백 등록 */
  Uart_RegisterDirControl( &huart4, Uart4_SetTxMode, Uart4_SetRxMode );
  Uart_RegisterDirControl( &hlpuart1, Lpuart1_SetTxMode, Lpuart1_SetRxMode );
  Uart_RegisterDirControl( &huart2, Usart2_SetTxMode, Usart2_SetRxMode );
  Uart_RegisterDirControl( &huart3, Usart3_SetTxMode, Usart3_SetRxMode );

  s_uart4_tx_mutex = osMutexNew( &s_uart4_tx_mutex_attributes );
  if( s_uart4_tx_mutex == NULL )
  {
    Error_Handler();
  }

  config = CommConfig_Get();

  ReinitDioContext();
  ReinitHeatingContext();
  ReinitGasContext();
  if( !ModbusMaster_InitQueues() )
  {
    Error_Handler();
  }

  /*
   * [누가] IF Application 초기화 코드가
   * [언제] Modbus write queue 생성이 끝난 뒤
   * [어디서] 부팅 초기화 단계에서
   * [무엇을] SPI3 Flash의 checkpoint/delta를 RAM으로 복원한다.
   * [왜] watchdog reset 전의 마지막 Slave 목표 상태를 복구하기 위해서다.
   * [어떻게] Flash 오류가 나도 PLC 통신은 유지하도록 mirror 기능만 사용하지 않는다.
   */
  if( At25Sf041b_Init() )
  {
    ( void )DesiredStateMirror_Init();
  }

  GstProtocol_InitContext( &s_gst_ctx, &huart1, config->usart1_period_ms, Usart1_SetTxMode, Usart1_SetRxMode );
  GstProtocol_SetAckCallback( &s_gst_ctx, Uart4_Transmit );
  if( !GstProtocol_IsReady( &s_gst_ctx ) )
  {
    Error_Handler();
  }

  /* 부트 확정(CONFIRMED)은 여기서 하지 않는다. task 생성과 스케줄러 기동까지
   * 성공해야 정상 부팅으로 볼 수 있으므로, UART4_Task 첫 실행 시점에 확정한다.
   * 그 전에 멈추거나 크래시하면 IWDG가 리셋시키고, 부트로더가 PENDING_TEST
   * 미확정을 보고 이전 슬롯으로 자동 롤백한다. */
}

bool Application_CreateTasks( void )
{
  s_uart4_task_handle = osThreadNew( UART4_Task, NULL, &s_uart4_task_attributes );
  s_lpuart1_task_handle = osThreadNew( LPUART1_Task, NULL, &s_lpuart1_task_attributes );
  s_usart2_task_handle = osThreadNew( USART2_Task, NULL, &s_usart2_task_attributes );
  s_usart3_task_handle = osThreadNew( USART3_Task, NULL, &s_usart3_task_attributes );
  s_usart1_rx_task_handle = osThreadNew( USART1_RxTask, NULL, &s_usart1_rx_task_attributes );
  s_usart1_protocol_task_handle = osThreadNew( USART1_ProtocolTask, NULL, &s_usart1_protocol_task_attributes );
  s_usart1_tx_task_handle = osThreadNew( USART1_TxTask, NULL, &s_usart1_tx_task_attributes );

  return ( s_uart4_task_handle != NULL ) && ( s_lpuart1_task_handle != NULL ) && ( s_usart2_task_handle != NULL )
         && ( s_usart3_task_handle != NULL ) && ( s_usart1_rx_task_handle != NULL ) && ( s_usart1_protocol_task_handle != NULL )
         && ( s_usart1_tx_task_handle != NULL );
}

void UART4_Task( void *argument )
{
  bool handled;

  ( void )argument;

  /* 이 지점에 도달했다는 것은 초기화, task 생성, 스케줄러 기동이 전부
   * 성공했다는 뜻이다. PENDING_TEST(시험 부팅)라면 지금 CONFIRMED로 확정한다. */
  ( void )Bootloader_ConfirmBootOk();

  for( ;; )
  {
    SharedMap_SetCurrentTimeMs( HAL_GetTick() );
    ( void )Uart_ServiceRecoveryFor( &huart4 );
    handled = ProcessUart4Request();
    if( handled )
    {
      s_uart4_last_rx_ms = HAL_GetTick();
      s_uart4_watchdog_latched = false;
    }
    ServiceUart4Watchdog();
    ServiceHeatingPulseTimeout();
    ServiceConfigApply();
    ServiceCommFaultOutputs();
    DesiredStateMirror_Service( HAL_GetTick() );
    DesiredStateMirror_RestoreService();
    ( void )HAL_IWDG_Refresh( &hiwdg );
    osDelay( APP_TASK_LOOP_DELAY_MS );
  }
}

void LPUART1_Task( void *argument )
{
  uint32_t local_revision = CommConfig_GetRevision();

  ( void )argument;

  ModbusMaster_RegisterConfiguredSlaves( &s_dio_ctx );
  DesiredStateMirror_SetPortReady( ( uint8_t )MODBUS_MASTER_PORT_LPUART1 );

  for( ;; )
  {
    SharedMap_SetCurrentTimeMs( HAL_GetTick() );
    if( local_revision != CommConfig_GetRevision() )
    {
      ReinitDioContext();
      ModbusMaster_RegisterConfiguredSlaves( &s_dio_ctx );
      DesiredStateMirror_SetPortReady( ( uint8_t )MODBUS_MASTER_PORT_LPUART1 );
      local_revision = CommConfig_GetRevision();
    }
    ( void )Uart_ServiceRecoveryFor( &hlpuart1 );
    ModbusMaster_TxStep( &s_dio_ctx );
    ModbusMaster_RxStep( &s_dio_ctx );
    ModbusMaster_ProcessStep( &s_dio_ctx );
    osDelay( APP_TASK_LOOP_DELAY_MS );
  }
}

void USART2_Task( void *argument )
{
  uint32_t local_revision = CommConfig_GetRevision();

  ( void )argument;

  ModbusMaster_RegisterConfiguredSlaves( &s_heating_ctx );
  DesiredStateMirror_SetPortReady( ( uint8_t )MODBUS_MASTER_PORT_USART2 );

  for( ;; )
  {
    SharedMap_SetCurrentTimeMs( HAL_GetTick() );
    if( local_revision != CommConfig_GetRevision() )
    {
      ReinitHeatingContext();
      ModbusMaster_RegisterConfiguredSlaves( &s_heating_ctx );
      DesiredStateMirror_SetPortReady( ( uint8_t )MODBUS_MASTER_PORT_USART2 );
      local_revision = CommConfig_GetRevision();
    }
    ( void )Uart_ServiceRecoveryFor( &huart2 );
    ModbusMaster_TxStep( &s_heating_ctx );
    ModbusMaster_RxStep( &s_heating_ctx );
    ModbusMaster_ProcessStep( &s_heating_ctx );
    osDelay( APP_TASK_LOOP_DELAY_MS );
  }
}

void USART3_Task( void *argument )
{
  uint32_t local_revision = CommConfig_GetRevision();

  ( void )argument;

  ModbusMaster_RegisterConfiguredSlaves( &s_gas_ctx );
  DesiredStateMirror_SetPortReady( ( uint8_t )MODBUS_MASTER_PORT_USART3 );

  for( ;; )
  {
    SharedMap_SetCurrentTimeMs( HAL_GetTick() );
    if( local_revision != CommConfig_GetRevision() )
    {
      ReinitGasContext();
      ModbusMaster_RegisterConfiguredSlaves( &s_gas_ctx );
      DesiredStateMirror_SetPortReady( ( uint8_t )MODBUS_MASTER_PORT_USART3 );
      local_revision = CommConfig_GetRevision();
    }
    ( void )Uart_ServiceRecoveryFor( &huart3 );
    ModbusMaster_TxStep( &s_gas_ctx );
    ModbusMaster_RxStep( &s_gas_ctx );
    ModbusMaster_ProcessStep( &s_gas_ctx );
    osDelay( APP_TASK_LOOP_DELAY_MS );
  }
}

void USART1_RxTask( void *argument )
{
  ( void )argument;

  for( ;; )
  {
    SharedMap_SetCurrentTimeMs( HAL_GetTick() );
    ( void )Uart_ServiceRecoveryFor( &huart1 );
    GstProtocol_RxStep( &s_gst_ctx );
    osDelay( APP_TASK_LOOP_DELAY_MS );
  }
}

void USART1_ProtocolTask( void *argument )
{
  ( void )argument;

  for( ;; )
  {
    SharedMap_SetCurrentTimeMs( HAL_GetTick() );
    GstProtocol_ProtocolStep( &s_gst_ctx );
    osDelay( APP_TASK_LOOP_DELAY_MS );
  }
}

void USART1_TxTask( void *argument )
{
  ( void )argument;

  for( ;; )
  {
    SharedMap_SetCurrentTimeMs( HAL_GetTick() );
    GstProtocol_TxStep( &s_gst_ctx );
  }
}
