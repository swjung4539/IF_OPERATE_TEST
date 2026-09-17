#include "a_modbus_master.h"

#include "a_desired_state_mirror.h"
#include "a_poll_table.h"
#include "a_shared_map.h"
#include "a_uart.h"
#include "cmsis_os.h"

#include <stddef.h>
#include <string.h>
#include "a_log.h"

#define MODBUS_MASTER_MAX_FRAME_LENGTH  256U
#define MODBUS_MASTER_WRITE_QUEUE_DEPTH 4U
#define MODBUS_MASTER_RX_QUEUE_DEPTH    4U

typedef struct
{
  uint8_t frame[ MODBUS_MASTER_MAX_FRAME_LENGTH ];
  uint16_t frame_len;
} ModbusMasterRxMsg_t;

static osMessageQueueId_t s_tx_queues[ MODBUS_MASTER_PORT_COUNT ];
static osMessageQueueId_t s_rx_queues[ MODBUS_MASTER_PORT_COUNT ];

static uint16_t GetTxTimeoutMs( void )
{
  const CommConfig_t *config = CommConfig_Get();

  if( ( config == NULL ) || ( config->tx_timeout_ms == 0U ) )
  {
    return DEFAULT_TX_TIMEOUT_MS;
  }

  return config->tx_timeout_ms;
}

static uint16_t GetResponseTimeoutMs( const ModbusMasterPortContext_t *ctx )
{
  const CommConfig_t *config = CommConfig_Get();

  if( config == NULL )
  {
    return 200U;
  }

  switch( ctx->port_id )
  {
    case MODBUS_MASTER_PORT_LPUART1:
      return config->lpuart1_response_timeout_ms;
    case MODBUS_MASTER_PORT_USART2:
      return config->usart2_response_timeout_ms;
    case MODBUS_MASTER_PORT_USART3:
      return config->usart3_response_timeout_ms;
    default:
      return 200U;
  }
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

static uint8_t PortLog( const ModbusMasterPortContext_t *ctx )
{
  switch( ctx->port_id )
  {
    case MODBUS_MASTER_PORT_LPUART1:
      return g_log_dio;
    case MODBUS_MASTER_PORT_USART2:
      return g_log_heating;
    case MODBUS_MASTER_PORT_USART3:
      return g_log_gas;
    default:
      return g_log_master;
  }
}

static bool IsElapsed( uint32_t now, uint32_t last, uint16_t period_ms )
{
  return ( period_ms == 0U ) || ( ( uint32_t )( now - last ) >= period_ms );
}

static bool IsReadFunction( PollTableFunction_t function )
{
  return ( function == POLL_TABLE_FUNCTION_FC01 ) || ( function == POLL_TABLE_FUNCTION_FC02 )
         || ( function == POLL_TABLE_FUNCTION_FC03 ) || ( function == POLL_TABLE_FUNCTION_FC04 );
}

static bool IsWriteFunction( uint8_t function )
{
  return ( function == MODBUS_FC_WRITE_SINGLE_COIL ) || ( function == MODBUS_FC_WRITE_SINGLE_REGISTER )
         || ( function == MODBUS_FC_WRITE_MULTIPLE_COILS ) || ( function == MODBUS_FC_WRITE_MULTIPLE_REGS );
}

static bool IsValidPortId( ModbusMasterPortId_t port_id )
{
  return ( ( uint32_t )port_id < ( uint32_t )MODBUS_MASTER_PORT_COUNT );
}

static PollTableProfile_t GetSlaveProfile( const ModbusMasterPortContext_t *ctx, uint8_t slave_id )
{
  ActiveIdList_t active_ids;
  uint8_t i;

  if( !CommConfig_GetActiveIds( ctx->group, &active_ids ) )
  {
    return POLL_TABLE_PROFILE_UNKNOWN;
  }

  for( i = 0U; i < active_ids.count; i++ )
  {
    if( active_ids.ids[ i ] == slave_id )
    {
      return ( PollTableProfile_t )ctx->slot_profiles[ i ];
    }
  }

  return POLL_TABLE_PROFILE_UNKNOWN;
}

static PollTableProfile_t GetConfiguredGasProfile( uint8_t index )
{
  switch( CommConfig_GetGasTypeByIndex( index ) )
  {
    case COMM_GAS_TYPE_H2:
      return POLL_TABLE_PROFILE_H2;
    case COMM_GAS_TYPE_H2_TFT:
      return POLL_TABLE_PROFILE_H2_TFT;
    case COMM_GAS_TYPE_NONE:
    default:
      /* 동작 안 함 - polling 대상에서 제외 */
      return POLL_TABLE_PROFILE_UNKNOWN;
  }
}

static bool FunctionMatchesItem( const PollTableItem_t *item, uint8_t function )
{
  return ( item != NULL ) && ( ( ( uint8_t )item->function == function ) || ( ( uint8_t )item->alternate_function == function ) );
}

static bool IsPendingForContext( const ModbusMasterPortContext_t *ctx, const ModbusSlaveWriteRequest_t *pending )
{
  PollTableProfile_t profile;

  if( ( ctx == NULL ) || ( pending == NULL ) || ( pending->valid == 0U ) || ( pending->is_global != 0U ) )
  {
    return false;
  }

  if( !CommConfig_IsActiveId( ctx->group, pending->slave_id ) )
  {
    return false;
  }

  profile = GetSlaveProfile( ctx, pending->slave_id );
  return PollTable_FindWriteItem( profile, pending->table, pending->address ) != NULL;
}

static bool FindPendingWritePort( const ModbusSlaveWriteRequest_t *pending, ModbusMasterPortId_t *port_id )
{
  static const struct
  {
    CommDeviceGroup_t group;
    ModbusMasterPortId_t port_id;
  } routes[ ] = {
      { COMM_DEVICE_GROUP_DIO, MODBUS_MASTER_PORT_LPUART1 }, { COMM_DEVICE_GROUP_HEATING, MODBUS_MASTER_PORT_USART2 }, {
          COMM_DEVICE_GROUP_GAS, MODBUS_MASTER_PORT_USART3 }, };
  size_t index;

  if( ( pending == NULL ) || ( port_id == NULL ) )
  {
    return false;
  }

  for( index = 0U; index < ( sizeof( routes ) / sizeof( routes[ 0 ] ) ); index++ )
  {
    if( !CommConfig_IsActiveId( routes[ index ].group, pending->slave_id ) )
    {
      continue;
    }

    if( routes[ index ].group == COMM_DEVICE_GROUP_GAS )
    {
      if( ( PollTable_FindWriteItem( POLL_TABLE_PROFILE_H2, pending->table, pending->address ) != NULL ) || ( PollTable_FindWriteItem(
          POLL_TABLE_PROFILE_H2_TFT, pending->table, pending->address )
                                                                                                              != NULL ) )
      {
        *port_id = routes[ index ].port_id;
        return true;
      }
    }
    else
    {
      const PollTableProfile_t profile = PollTable_GetProfileForGroupId( routes[ index ].group, pending->slave_id );
      if( PollTable_FindWriteItem( profile, pending->table, pending->address ) != NULL )
      {
        *port_id = routes[ index ].port_id;
        return true;
      }
    }
  }

  return false;
}

static uint16_t GetReadResponseLength( const PollTableItem_t *item )
{
  uint16_t byte_count;

  if( item == NULL )
  {
    return 0U;
  }

  if( ( item->function == POLL_TABLE_FUNCTION_FC01 ) || ( item->function == POLL_TABLE_FUNCTION_FC02 ) )
  {
    byte_count = ( uint16_t )( ( item->quantity + 7U ) / 8U );
  }
  else
  {
    byte_count = ( uint16_t )( item->quantity * 2U );
  }

  return ( uint16_t )( 3U + byte_count + MODBUS_RTU_CRC_LENGTH );
}

static bool GetWireAddress( const PollTableItem_t *item, uint16_t map_address, uint16_t *wire_address )
{
  int32_t converted_address;

  if( ( item == NULL ) || ( wire_address == NULL ) )
  {
    return false;
  }

  converted_address = ( int32_t )map_address + item->wire_address_offset;
  if( ( converted_address < 0 ) || ( converted_address > UINT16_MAX ) )
  {
    return false;
  }

  *wire_address = ( uint16_t )converted_address;
  return true;
}

static bool BuildReadRequest( uint8_t slave_id, const PollTableItem_t *item, uint8_t *request, uint16_t request_capacity,
                              uint16_t *request_len )
{
  uint16_t wire_address;

  if( ( item == NULL ) || ( request == NULL ) || ( request_len == NULL ) || ( request_capacity < MODBUS_RTU_READ_REQUEST_LENGTH )
      || !IsReadFunction( item->function ) || ( item->quantity == 0U )
      || !GetWireAddress( item, item->address, &wire_address ) )
  {
    return false;
  }

  request[ 0 ] = slave_id;
  request[ 1 ] = ( uint8_t )item->function;
  Modbus_PutU16Be( &request[ 2 ], wire_address );
  Modbus_PutU16Be( &request[ 4 ], item->quantity );
  Modbus_AppendCrc( request, 6U );
  *request_len = MODBUS_RTU_READ_REQUEST_LENGTH;

  return true;
}

static bool BuildWriteRequest( const ModbusSlaveWriteRequest_t *pending, uint8_t function, uint8_t *request,
                               uint16_t request_capacity, uint16_t *request_len )
{
  uint16_t payload_len;
  uint16_t offset;

  if( ( pending == NULL ) || ( request == NULL ) || ( request_len == NULL ) || ( pending->quantity == 0U )
      || !IsWriteFunction( function ) )
  {
    return false;
  }

  request[ 0 ] = pending->slave_id;
  request[ 1 ] = function;
  Modbus_PutU16Be( &request[ 2 ], pending->address );

  switch( function )
  {
    case MODBUS_FC_WRITE_SINGLE_COIL:
      if( ( pending->table != SHARED_MAP_TABLE_COIL ) || ( pending->quantity != 1U )
          || ( request_capacity < MODBUS_RTU_READ_REQUEST_LENGTH ) )
      {
        return false;
      }
      Modbus_PutU16Be( &request[ 4 ], ( pending->values[ 0 ] != 0U ) ? 0xFF00U : 0x0000U );
      Modbus_AppendCrc( request, 6U );
      *request_len = MODBUS_RTU_READ_REQUEST_LENGTH;
      return true;

    case MODBUS_FC_WRITE_SINGLE_REGISTER:
      if( ( pending->table != SHARED_MAP_TABLE_HOLDING_REGISTER ) || ( pending->quantity != 1U )
          || ( request_capacity < MODBUS_RTU_READ_REQUEST_LENGTH ) )
      {
        return false;
      }
      Modbus_PutU16Be( &request[ 4 ], pending->values[ 0 ] );
      Modbus_AppendCrc( request, 6U );
      *request_len = MODBUS_RTU_READ_REQUEST_LENGTH;
      return true;

    case MODBUS_FC_WRITE_MULTIPLE_COILS:
      if( pending->table != SHARED_MAP_TABLE_COIL )
      {
        return false;
      }
      payload_len = ( uint16_t )( ( pending->quantity + 7U ) / 8U );
      if( request_capacity < ( uint16_t )( 9U + payload_len ) )
      {
        return false;
      }
      Modbus_PutU16Be( &request[ 4 ], pending->quantity );
      request[ 6 ] = ( uint8_t )payload_len;
      ( void )memset( &request[ 7 ], 0, payload_len );
      for( offset = 0U; offset < pending->quantity; offset++ )
      {
        if( pending->values[ offset ] != 0U )
        {
          request[ 7U + ( offset / 8U ) ] |= ( uint8_t )( 1U << ( offset % 8U ) );
        }
      }
      Modbus_AppendCrc( request, ( uint16_t )( 7U + payload_len ) );
      *request_len = ( uint16_t )( 9U + payload_len );
      return true;

    case MODBUS_FC_WRITE_MULTIPLE_REGS:
      if( ( pending->table != SHARED_MAP_TABLE_HOLDING_REGISTER ) || ( pending->quantity > MODBUS_SLAVE_MAX_WRITE_REGISTERS ) )
      {
        return false;
      }
      payload_len = ( uint16_t )( pending->quantity * 2U );
      if( request_capacity < ( uint16_t )( 9U + payload_len ) )
      {
        return false;
      }
      Modbus_PutU16Be( &request[ 4 ], pending->quantity );
      request[ 6 ] = ( uint8_t )payload_len;
      for( offset = 0U; offset < pending->quantity; offset++ )
      {
        Modbus_PutU16Be( &request[ 7U + ( offset * 2U ) ], pending->values[ offset ] );
      }
      Modbus_AppendCrc( request, ( uint16_t )( 7U + payload_len ) );
      *request_len = ( uint16_t )( 9U + payload_len );
      return true;

    default:
      return false;
  }
}

static bool TransmitOnly( ModbusMasterPortContext_t *ctx, const uint8_t *request, uint16_t request_len )
{
  bool ok;

  if( ( ctx == NULL ) || ( ctx->huart == NULL ) || ( request == NULL ) || ( request_len == 0U ) )
  {
    return false;
  }

  Uart_ClearData( ctx->huart );

  /* DE 상승/하강은 a_uart의 IT 송신 경로(TC 시점 ISR)에서 처리한다 */
  ok = Uart_TransmitBlocking( ctx->huart, request, request_len, GetTxTimeoutMs() );

  LOGHEX( PortLog( ctx ), "[TX] ", request, request_len );

  return ok;
}

static bool ParseReadResponse( uint8_t slave_id, const PollTableItem_t *item, const uint8_t *response, uint16_t response_len )
{
  uint16_t expected_byte_count;
  uint16_t offset;

  if( ( item == NULL ) || ( response == NULL ) || ( response_len != GetReadResponseLength( item ) )
      || !Modbus_VerifyCrc( response, response_len ) || ( response[ 0 ] != slave_id )
      || ( response[ 1 ] != ( uint8_t )item->function ) )
  {
    return false;
  }

  if( ( item->function == POLL_TABLE_FUNCTION_FC01 ) || ( item->function == POLL_TABLE_FUNCTION_FC02 ) )
  {
    expected_byte_count = ( uint16_t )( ( item->quantity + 7U ) / 8U );
  }
  else
  {
    expected_byte_count = ( uint16_t )( item->quantity * 2U );
  }

  if( response[ 2 ] != ( uint8_t )expected_byte_count )
  {
    return false;
  }

  if( ( item->function == POLL_TABLE_FUNCTION_FC01 ) || ( item->function == POLL_TABLE_FUNCTION_FC02 ) )
  {
    for( offset = 0U; offset < item->quantity; offset++ )
    {
      const uint8_t packed = response[ 3U + ( offset / 8U ) ];
      const uint16_t value = ( ( packed & ( uint8_t )( 1U << ( offset % 8U ) ) ) != 0U ) ? 1U : 0U;

      if( !SharedMap_UpdateFromPoll( slave_id, item->table, ( uint16_t )( item->address + offset ), value ) )
      {
        return false;
      }
    }
  }
  else
  {
    for( offset = 0U; offset < item->quantity; offset++ )
    {
      const uint16_t value = Modbus_GetU16Be( &response[ 3U + ( offset * 2U ) ] );

      if( !SharedMap_UpdateFromPoll( slave_id, item->table, ( uint16_t )( item->address + offset ), value ) )
      {
        return false;
      }
    }
  }

  return true;
}

static uint8_t SelectWriteFunction( const ModbusSlaveWriteRequest_t *pending, const PollTableItem_t *item )
{
  if( FunctionMatchesItem( item, pending->function ) )
  {
    return pending->function;
  }

  if( item != NULL )
  {
    if( ( pending->quantity == 1U ) && ( item->alternate_function != POLL_TABLE_FUNCTION_NONE ) )
    {
      return ( uint8_t )item->alternate_function;
    }

    return ( uint8_t )item->function;
  }

  return pending->function;
}

static bool ProcessPendingAttempt( ModbusMasterPortContext_t *ctx, const ModbusSlaveWriteRequest_t *pending )
{
  uint8_t request[ MODBUS_MASTER_MAX_FRAME_LENGTH ];
  uint16_t request_len = 0U;
  ModbusSlaveWriteRequest_t wire_request;
  const PollTableProfile_t profile = GetSlaveProfile( ctx, pending->slave_id );
  const PollTableItem_t *item = PollTable_FindWriteItem( profile, pending->table, pending->address );
  const uint8_t function = SelectWriteFunction( pending, item );

  if( item == NULL )
  {
    return false;
  }

  wire_request = *pending;
  if( !GetWireAddress( item, pending->address, &wire_request.address )
      || !BuildWriteRequest( &wire_request, function, request, sizeof( request ), &request_len ) )
  {
    return false;
  }

  return TransmitOnly( ctx, request, request_len );
}

static void AdvancePollCursor( ModbusMasterPortContext_t *ctx, size_t poll_count, uint8_t active_count )
{
  uint8_t i;

  if( ( ctx == NULL ) || ( poll_count == 0U ) || ( active_count == 0U ) )
  {
    return;
  }

  ctx->poll_item_index++;
  if( ctx->poll_item_index >= ( uint16_t )poll_count )
  {
    ctx->poll_item_index = 0U;
    for( i = 0U; i < active_count; i++ )
    {
      ctx->active_id_index = ( ctx->active_id_index + 1U ) % active_count;
      if( !ctx->slaves_registered || ( ( ctx->enabled_mask & ( 1U << ctx->active_id_index ) ) != 0U ) )
      {
        break;
      }
    }
  }
}

void ModbusMaster_RegisterConfiguredSlaves( ModbusMasterPortContext_t *ctx )
{
  ActiveIdList_t active_ids;
  uint8_t i;

  if( ctx == NULL )
  {
    return;
  }

  if( !CommConfig_GetActiveIds( ctx->group, &active_ids ) )
  {
    ctx->slaves_registered = true;
    return;
  }

  ctx->enabled_mask = 0U;
  for( i = 0U; i < ( uint8_t )MAX_ACTIVE_ID_COUNT; i++ )
  {
    ctx->slot_profiles[ i ] = ( uint8_t )POLL_TABLE_PROFILE_UNKNOWN;
  }

  for( i = 0U; i < active_ids.count; i++ )
  {
    const uint8_t slave_id = active_ids.ids[ i ];
    const PollTableProfile_t profile =
        ( ctx->group == COMM_DEVICE_GROUP_GAS ) ? GetConfiguredGasProfile( i )
        : ( ctx->group == COMM_DEVICE_GROUP_HEATING ) ? POLL_TABLE_PROFILE_HEATING
        : POLL_TABLE_PROFILE_DIO;

    if( profile == POLL_TABLE_PROFILE_UNKNOWN )
    {
      /* Gas Type=0(동작 안 함) 슬롯은 polling/등록 대상에서 제외 */
      LOG( PortLog( ctx ), "[MASTER] slot %u id=0x%02X disabled\r\n", i, slave_id );
      continue;
    }

    ctx->enabled_mask |= ( 1U << i );
    ctx->slot_profiles[ i ] = ( uint8_t )profile;
    LOG( PortLog( ctx ), "[MASTER] register id=0x%02X profile=%u\r\n", slave_id, ( unsigned int )profile );
  }

  /* Sort enabled IDs ascending (lowest ID = highest poll priority) */
  {
    uint8_t sorted_ids[ MAX_ACTIVE_ID_COUNT ];
    uint8_t sorted_count = 0U;
    uint8_t j;
    uint8_t tmp;

    for( i = 0U; i < active_ids.count; i++ )
    {
      if( ( ctx->enabled_mask & ( 1U << i ) ) != 0U )
      {
        sorted_ids[ sorted_count++ ] = active_ids.ids[ i ];
      }
    }

    for( i = 0U; i < sorted_count; i++ )
    {
      for( j = ( uint8_t )( i + 1U ); j < sorted_count; j++ )
      {
        if( sorted_ids[ j ] < sorted_ids[ i ] )
        {
          tmp = sorted_ids[ i ];
          sorted_ids[ i ] = sorted_ids[ j ];
          sorted_ids[ j ] = tmp;
        }
      }
    }

    /* 직전에 등록했던 ID 중 새 목록에 더 이상 없는 ID는 SharedMap 엔트리를 반환한다.
     * (국번 변경 시 예전 ID의 엔트리를 그대로 두면 고정 용량 풀이 고갈되어
     *  새 ID용 엔트리를 만들지 못하는 문제가 있었다.) */
    for( i = 0U; i < ctx->registered_count; i++ )
    {
      const uint8_t old_id = ctx->registered_ids[ i ];
      bool still_active = false;

      for( j = 0U; j < sorted_count; j++ )
      {
        if( sorted_ids[ j ] == old_id )
        {
          still_active = true;
          break;
        }
      }

      if( !still_active )
      {
        SharedMap_UnregisterSlave( old_id, ctx->group );
      }
    }

    for( i = 0U; i < sorted_count; i++ )
    {
      SharedMap_RegisterSlave( sorted_ids[ i ], ctx->group );
    }

    for( i = 0U; i < sorted_count; i++ )
    {
      ctx->registered_ids[ i ] = sorted_ids[ i ];
    }
    ctx->registered_count = sorted_count;
  }

  ctx->slaves_registered = true;
}

void ModbusMaster_InitContext( ModbusMasterPortContext_t *ctx, UART_HandleTypeDef *huart, ModbusMasterPortId_t port_id,
                               CommDeviceGroup_t group, uint16_t poll_period_ms, void (*set_tx_mode)( void ),
                               void (*set_rx_mode)( void ) )
{
  uint8_t registered_ids[ MAX_ACTIVE_ID_COUNT ];
  uint8_t registered_count;

  if( ctx == NULL )
  {
    return;
  }

  /* 국번(ID) 변경으로 재초기화될 때도 RegisterConfiguredSlaves()가 이전 ID 목록과
   * 비교해 더 이상 쓰이지 않는 ID를 SharedMap에서 UnregisterSlave할 수 있어야 한다.
   * memset으로 지우기 전에 보존했다가 복원한다 */
  ( void )memcpy( registered_ids, ctx->registered_ids, sizeof( registered_ids ) );
  registered_count = ctx->registered_count;

  ( void )memset( ctx, 0, sizeof( *ctx ) );
  ctx->huart = huart;
  ctx->port_id = port_id;
  ctx->group = group;
  ctx->poll_period_ms = poll_period_ms;
  ctx->set_tx_mode = set_tx_mode;
  ctx->set_rx_mode = set_rx_mode;

  ( void )memcpy( ctx->registered_ids, registered_ids, sizeof( registered_ids ) );
  ctx->registered_count = registered_count;
}

bool ModbusMaster_InitQueues( void )
{
  uint32_t index;

  for( index = 0U; index < ( uint32_t )MODBUS_MASTER_PORT_COUNT; index++ )
  {
    if( s_tx_queues[ index ] == NULL )
    {
      s_tx_queues[ index ] = osMessageQueueNew( MODBUS_MASTER_WRITE_QUEUE_DEPTH, sizeof(ModbusSlaveWriteRequest_t),
      NULL );
    }
    if( s_tx_queues[ index ] == NULL )
    {
      return false;
    }

    if( s_rx_queues[ index ] == NULL )
    {
      s_rx_queues[ index ] = osMessageQueueNew( MODBUS_MASTER_RX_QUEUE_DEPTH, sizeof(ModbusMasterRxMsg_t),
      NULL );
    }
    if( s_rx_queues[ index ] == NULL )
    {
      return false;
    }
  }

  return true;
}

/*
 * [누가] Modbus master가 PLC 일반 write와 Flash 복구 write를 받을 때
 * [언제] 물리 Slave port가 결정된 뒤 queue에 넣기 직전에
 * [어디서] 하위 Slave forwarding 공통 처리부에서
 * [무엇을] 일반 write만 Desired-State Mirror에 추적하고 두 write를 같은 queue에 넣는다.
 * [왜] 통신 경로는 재사용하면서 복구 write의 Flash 재기록을 막기 위해서다.
 * [어떻게] track_for_recovery flag로 TrackWrite 호출 여부를 구분한다.
 */
static bool ModbusMaster_ForwardWriteInternal( const ModbusSlaveWriteRequest_t *pending, bool track_for_recovery )
{
  ModbusMasterPortId_t port_id;

  if( ( pending == NULL ) || ( pending->valid == 0U ) || ( pending->is_global != 0U )
      || !FindPendingWritePort( pending, &port_id ) || !IsValidPortId( port_id ) || ( s_tx_queues[ port_id ] == NULL ) )
  {
    return false;
  }

  if( track_for_recovery )
  {
    DesiredStateMirror_TrackWrite( ( uint8_t )port_id, pending );
  }

  if( osMessageQueuePut( s_tx_queues[ port_id ], pending, 0U, 0U ) != osOK )
  {
    LOG( g_log_master, "[MASTER] tx queue full port=%u id=0x%02X addr=%u\r\n", ( unsigned int )port_id, pending->slave_id,
         pending->address );
    return false;
  }

  LOG( g_log_master, "[MASTER] enqueue write port=%u id=0x%02X addr=%u\r\n", ( unsigned int )port_id, pending->slave_id,
       pending->address );

  return true;
}

bool ModbusMaster_ForwardWrite( const ModbusSlaveWriteRequest_t *pending )
{
  return ModbusMaster_ForwardWriteInternal( pending, true );
}

bool ModbusMaster_ForwardRecoveredWrite( const ModbusSlaveWriteRequest_t *pending )
{
  return ModbusMaster_ForwardWriteInternal( pending, false );
}

static void SendPollRequest( ModbusMasterPortContext_t *ctx, uint8_t slave_id, const PollTableItem_t *item, size_t poll_count,
                             uint8_t active_count )
{
  uint16_t request_len = 0U;
  bool ok;

  if( !BuildReadRequest( slave_id, item, ctx->pending_request, ( uint16_t )sizeof( ctx->pending_request ), &request_len ) )
  {
    return;
  }

  LOG( PortLog( ctx ), "[MASTER] poll id=0x%02X fc=%u addr=%u qty=%u\r\n", slave_id, ( uint8_t )item->function, item->address,
       item->quantity );

  Uart_ClearData( ctx->huart );
  ok = Uart_TransmitBlocking( ctx->huart, ctx->pending_request, request_len, GetTxTimeoutMs() );

  LOGHEX( PortLog( ctx ), "[MASTER REQ] ", ctx->pending_request, request_len );

  if( !ok )
  {
    return;
  }

  ctx->pending_request_len = request_len;
  ctx->pending_slave_id = slave_id;
  ctx->pending_item = *item;
  ctx->pending_response_len = GetReadResponseLength( item );
  ctx->pending_poll_count = poll_count;
  ctx->pending_active_count = active_count;
  ctx->waiting_response = 1U;
  ctx->tx_sent_ms = HAL_GetTick();
}

static void RetryOrGivePoll( ModbusMasterPortContext_t *ctx )
{
  bool ok;

  ( void )SharedMap_SetSlaveStatus( ctx->group, ctx->active_id_index, false );
  ctx->pending_attempts++;

  if( ctx->pending_attempts < GetRetryCount() )
  {
    Uart_ClearData( ctx->huart );
    ok = Uart_TransmitBlocking( ctx->huart, ctx->pending_request, ctx->pending_request_len, GetTxTimeoutMs() );
    LOGHEX( PortLog( ctx ), "[MASTER RETRY] ", ctx->pending_request, ctx->pending_request_len );
    if( ok )
    {
      ctx->tx_sent_ms = HAL_GetTick();
    }
  }
  else
  {
    LOG( PortLog( ctx ), "[MASTER] poll give up\r\n" );
    AdvancePollCursor( ctx, ctx->pending_poll_count, ctx->pending_active_count );
    ctx->last_poll_ms = HAL_GetTick();
    ctx->waiting_response = 0U;
    ctx->pending_attempts = 0U;
  }
}

void ModbusMaster_TxStep( ModbusMasterPortContext_t *ctx )
{
  ActiveIdList_t active_ids;
  PollTableProfile_t profile;
  const PollTableItem_t *poll_items;
  size_t poll_count = 0U;
  uint8_t slave_id;
  ModbusSlaveWriteRequest_t write_req;
  uint16_t unused_ack_len;
  uint32_t now;

  if( ( ctx == NULL ) || ( ctx->huart == NULL ) )
  {
    return;
  }

  if( ctx->waiting_response != 0U )
  {
    if( ctx->pending_is_write )
    {
      /* write forward는 best-effort: 응답 대기 1회만, 재시도 없이 다음 큐 항목으로 진행 (충돌 방지용 최소 대기) */
      if( IsElapsed( HAL_GetTick(), ctx->tx_sent_ms, GetResponseTimeoutMs( ctx ) ) )
      {
        LOG( PortLog( ctx ), "[MASTER] write ack timeout (best-effort, no retry)\r\n" );
        ctx->waiting_response = 0U;
        ctx->pending_is_write = 0U;
        ctx->last_poll_ms = HAL_GetTick();
      }
      return;
    }

    if( IsElapsed( HAL_GetTick(), ctx->tx_sent_ms, GetResponseTimeoutMs( ctx ) ) )
    {
      LOG( PortLog( ctx ), "[MASTER] poll timeout attempt=%u\r\n", ctx->pending_attempts + 1U );
      RetryOrGivePoll( ctx );
    }
    return;
  }

  if( IsValidPortId( ctx->port_id ) && ( s_tx_queues[ ctx->port_id ] != NULL ) )
  {
    ( void )memset( &write_req, 0, sizeof( write_req ) );
    if( osMessageQueueGet( s_tx_queues[ ctx->port_id ], &write_req, NULL, 0U ) == osOK )
    {
      if( IsPendingForContext( ctx, &write_req ) )
      {
        LOG( PortLog( ctx ), "[MASTER] process write id=0x%02X addr=%u\r\n", write_req.slave_id, write_req.address );
        if( ProcessPendingAttempt( ctx, &write_req ) )
        {
          /* write forward 성공/실패는 그룹 comm 비트에 반영하지 않는다(폴링 결과만 반영, 노이즈 방지).
             다음 forward가 이 응답과 버스에서 충돌하지 않도록 응답 대기 상태로 전환 */
          ctx->waiting_response = 1U;
          ctx->pending_is_write = 1U;
          ctx->tx_sent_ms = HAL_GetTick();
        }
      }
      else
      {
        unused_ack_len = 0U;
        ( void )ModbusSlave_ProcessWriteRequest( &write_req, false, NULL, 0U, &unused_ack_len );
      }
      ctx->last_poll_ms = HAL_GetTick();
      return;
    }
  }

  now = HAL_GetTick();
  if( !IsElapsed( now, ctx->last_poll_ms, ctx->poll_period_ms ) )
  {
    return;
  }

  if( !CommConfig_GetActiveIds( ctx->group, &active_ids ) || ( active_ids.count == 0U ) )
  {
    return;
  }

  if( ctx->active_id_index >= active_ids.count )
  {
    ctx->active_id_index = 0U;
  }

  if( ctx->slaves_registered )
  {
    uint8_t tries;
    for( tries = 0U; tries < active_ids.count; tries++ )
    {
      if( ( ctx->enabled_mask & ( 1U << ctx->active_id_index ) ) != 0U )
      {
        break;
      }
      ctx->active_id_index = ( ctx->active_id_index + 1U ) % active_ids.count;
      ctx->poll_item_index = 0U;
    }
    if( ( ctx->enabled_mask & ( 1U << ctx->active_id_index ) ) == 0U )
    {
      ctx->last_poll_ms = now;
      return;
    }
  }

  slave_id = active_ids.ids[ ctx->active_id_index ];
  profile = GetSlaveProfile( ctx, slave_id );
  poll_items = PollTable_GetPollItems( profile, &poll_count );

  if( ( poll_items == NULL ) || ( poll_count == 0U ) )
  {
    ctx->active_id_index++;
    ctx->poll_item_index = 0U;
    if( ctx->active_id_index >= active_ids.count )
    {
      ctx->active_id_index = 0U;
    }
    ctx->last_poll_ms = now;
    return;
  }

  if( ctx->poll_item_index >= poll_count )
  {
    ctx->poll_item_index = 0U;
  }

  ctx->pending_attempts = 0U;
  SendPollRequest( ctx, slave_id, &poll_items[ ctx->poll_item_index ], poll_count, active_ids.count );
  ctx->last_poll_ms = now;
}

void ModbusMaster_RxStep( ModbusMasterPortContext_t *ctx )
{
  ModbusMasterRxMsg_t rx_msg;

  if( ( ctx == NULL ) || ( ctx->waiting_response == 0U ) )
  {
    return;
  }
  if( !IsValidPortId( ctx->port_id ) || ( s_rx_queues[ ctx->port_id ] == NULL ) )
  {
    return;
  }

  if( !Uart_GetData( ctx->huart, rx_msg.frame, ( uint16_t )sizeof( rx_msg.frame ), &rx_msg.frame_len ) )
  {
    return;
  }

  ( void )osMessageQueuePut( s_rx_queues[ ctx->port_id ], &rx_msg, 0U, 0U );
}

void ModbusMaster_ProcessStep( ModbusMasterPortContext_t *ctx )
{
  ModbusMasterRxMsg_t rx_msg;

  if( ( ctx == NULL ) || ( ctx->waiting_response == 0U ) )
  {
    return;
  }
  if( !IsValidPortId( ctx->port_id ) || ( s_rx_queues[ ctx->port_id ] == NULL ) )
  {
    return;
  }
  if( osMessageQueueGet( s_rx_queues[ ctx->port_id ], &rx_msg, NULL, 0U ) != osOK )
  {
    return;
  }

  LOGHEX( PortLog( ctx ), "[MASTER RSP] ", rx_msg.frame, rx_msg.frame_len );

  if( ctx->pending_is_write )
  {
    LOG( PortLog( ctx ), "[MASTER] write ack rx\r\n" );
    ctx->waiting_response = 0U;
    ctx->pending_is_write = 0U;
    ctx->last_poll_ms = HAL_GetTick();
    return;
  }

  if( ParseReadResponse( ctx->pending_slave_id, &ctx->pending_item, rx_msg.frame, rx_msg.frame_len ) )
  {
    LOG( PortLog( ctx ), "[MASTER] poll OK\r\n" );
    ( void )SharedMap_SetSlaveStatus( ctx->group, ctx->active_id_index, true );
    AdvancePollCursor( ctx, ctx->pending_poll_count, ctx->pending_active_count );
    ctx->last_poll_ms = HAL_GetTick();
    ctx->waiting_response = 0U;
    ctx->pending_attempts = 0U;
  }
  else
  {
    LOG( PortLog( ctx ), "[MASTER] poll parse FAIL\r\n" );
    RetryOrGivePoll( ctx );
  }
}
