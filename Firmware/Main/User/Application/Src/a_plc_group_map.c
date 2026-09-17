#include "a_plc_group_map.h"

#include "a_comm_config.h"
#include "a_shared_map.h"
#include "m_modbus.h"
#include "main.h"

#include <string.h>

/* ---- PLC 그룹 Slave ID ---- */
#define PLC_GROUP_READ_DIO       1U
#define PLC_GROUP_READ_HEATING   2U
#define PLC_GROUP_READ_GAS       3U
#define PLC_GROUP_READ_BACKUP    4U
#define PLC_GROUP_WRITE_DIO      5U
#define PLC_GROUP_WRITE_HEATING  6U
#define PLC_GROUP_WRITE_BACKUP   7U
#define PLC_GROUP_WRITE_CONFIG   8U

/* ---- Read 주소 범위 (사양서 절대주소) ---- */
#define READ_DIO_START           1U
#define READ_DIO_END             2U
#define READ_HEATING_START       3U
#define READ_HEATING_END         110U
#define READ_GAS_START           111U
#define READ_GAS_END             122U
#define READ_BACKUP_START        123U
#define READ_BACKUP_END          164U

/* ---- Write 주소 범위 (사양서 절대주소) ---- */
#define WRITE_DIO_START          201U
#define WRITE_DIO_END            202U
#define WRITE_HEATING_START      203U
#define WRITE_HEATING_END        265U
#define WRITE_BACKUP_START       266U
#define WRITE_BACKUP_END         305U
#define WRITE_CONFIG_START       306U
#define WRITE_CONFIG_END         317U

/* ---- 물리 장치 map 주소 ---- */
#define DIO_INPUT_HOLDING_ADDR      130U  /* DI 읽기 */
#define DIO_OUTPUT_HOLDING_ADDR     129U  /* DO 쓰기 */
#define HEATING_TEMP_BASE           1000U /* IR, CH1~18 온도 */
#define HEATING_ALARM_BASE          1018U /* IR, CH1~18 알람 */
#define HEATING_HOLDING_BASE        2000U /* HR, Machine/EEPROM/Reset/SV1~18 */
#define HEATING_REGS_PER_JACKET     21U
#define HEATING_EEPROM_SAVE_OFFSET  1U    /* jacket 내 offset: EEPROM Save */
#define HEATING_ALARM_RESET_OFFSET  2U    /* jacket 내 offset: ALARM Reset */
#define HEATING_ITEMS_PER_JACKET    36U   /* 온도 18 + 알람 18 */
#define GAS_REGS_PER_TYPE_SLOT      2U    /* 농도(1) + 상태(1) */
#define GAS_TFT_BLOCK_OFFSET        6U    /* STD 블록 3슬롯x2 = 6, 그 다음부터 TFT 블록 */
#define GAS_STD_CONC_ADDR           30000U /* IR */
#define GAS_STD_STATUS_BASE         10001U /* DI, 8bit */
#define GAS_TFT_CONC_UPPER_ADDR     5U     /* IR, 농도 Upper word */
#define GAS_TFT_STATUS_BASE         0U     /* DI, 8bit */
#define BACKUP_READ_DATA_BASE       242U   /* global HR, User Read Data 01~40 */
#define BACKUP_HEARTBEAT_ADDR       199U   /* global HR */
#define BACKUP_HEARTBEAT_PLC_ADDR   200U   /* global HR */
#define BACKUP_WRITE_DATA_BASE      201U   /* global HR, User Write Data 01~40 */
#define CONFIG_HOLDING_BASE         110U   /* global HR, Slave ID/Type 설정 + apply trigger(121) */

#define GAS_STATUS_BIT_COUNT        8U

static uint8_t GetActiveIdBySlot( CommDeviceGroup_t group, uint8_t slot )
{
  ActiveIdList_t ids;

  if( !CommConfig_GetActiveIds( group, &ids ) || ( slot >= ids.count ) )
  {
    return 0U;
  }

  return ids.ids[ slot ];
}

/* 값이 없는 주소(미장착/비활성 슬롯)는 0으로 채운다 - 사양 확정 사항 */
static uint16_t ReadOrZero( uint8_t id, SharedMapTable_t table, uint16_t address )
{
  uint16_t value = 0U;

  if( ( id == 0U ) || !SharedMap_Read( id, table, address, &value ) )
  {
    return 0U;
  }

  return value;
}

static uint16_t ReadGlobalOrZero( uint16_t address )
{
  uint16_t value = 0U;

  if( !SharedMap_ReadGlobal( SHARED_MAP_TABLE_HOLDING_REGISTER, address, &value ) )
  {
    return 0U;
  }

  return value;
}

static uint16_t PackStatusBits( uint8_t id, uint16_t base_address )
{
  uint16_t packed = 0U;
  uint16_t bit_value;
  uint8_t bit;

  if( id == 0U )
  {
    return 0U;
  }

  for( bit = 0U; bit < GAS_STATUS_BIT_COUNT; bit++ )
  {
    bit_value = 0U;
    if( SharedMap_Read( id, SHARED_MAP_TABLE_DISCRETE_INPUT, ( uint16_t )( base_address + bit ), &bit_value )
        && ( bit_value != 0U ) )
    {
      packed |= ( uint16_t )( 1U << bit );
    }
  }

  return packed;
}

static uint16_t ReadDioValue( uint16_t plc_addr )
{
  const uint8_t slot = ( uint8_t )( plc_addr - READ_DIO_START );

  return ReadOrZero( GetActiveIdBySlot( COMM_DEVICE_GROUP_DIO, slot ), SHARED_MAP_TABLE_HOLDING_REGISTER,
                     DIO_INPUT_HOLDING_ADDR );
}

static uint16_t ReadHeatingValue( uint16_t plc_addr )
{
  const uint16_t offset = ( uint16_t )( plc_addr - READ_HEATING_START );
  const uint8_t jacket = ( uint8_t )( offset / HEATING_ITEMS_PER_JACKET );
  const uint16_t item = ( uint16_t )( offset % HEATING_ITEMS_PER_JACKET );
  const uint8_t id = GetActiveIdBySlot( COMM_DEVICE_GROUP_HEATING, jacket );
  const uint16_t map_addr = ( item < 18U ) ? ( uint16_t )( HEATING_TEMP_BASE + item )
                                           : ( uint16_t )( HEATING_ALARM_BASE + ( item - 18U ) );

  return ReadOrZero( id, SHARED_MAP_TABLE_INPUT_REGISTER, map_addr );
}

/* Gas 슬롯의 STD/TFT 칸 중 실제 Type과 일치하는 칸만 값이 채워지고,
 * 나머지 칸(및 비활성 슬롯)은 0으로 올라간다 */
static uint16_t ReadGasValue( uint16_t plc_addr )
{
  const uint16_t offset = ( uint16_t )( plc_addr - READ_GAS_START );
  const bool is_tft_block = ( offset >= GAS_TFT_BLOCK_OFFSET );
  const uint16_t block_offset = is_tft_block ? ( uint16_t )( offset - GAS_TFT_BLOCK_OFFSET ) : offset;
  const uint8_t slot = ( uint8_t )( block_offset / GAS_REGS_PER_TYPE_SLOT );
  const uint8_t field = ( uint8_t )( block_offset % GAS_REGS_PER_TYPE_SLOT ); /* 0=농도, 1=상태 */
  const uint8_t id = GetActiveIdBySlot( COMM_DEVICE_GROUP_GAS, slot );
  const CommGasType_t type = CommConfig_GetGasTypeByIndex( slot );

  if( !is_tft_block )
  {
    if( type != COMM_GAS_TYPE_H2 )
    {
      return 0U;
    }
    return ( field == 0U ) ? ReadOrZero( id, SHARED_MAP_TABLE_INPUT_REGISTER, GAS_STD_CONC_ADDR )
                            : PackStatusBits( id, GAS_STD_STATUS_BASE );
  }

  if( type != COMM_GAS_TYPE_H2_TFT )
  {
    return 0U;
  }
  return ( field == 0U ) ? ReadOrZero( id, SHARED_MAP_TABLE_INPUT_REGISTER, GAS_TFT_CONC_UPPER_ADDR )
                          : PackStatusBits( id, GAS_TFT_STATUS_BASE );
}

static uint16_t ReadBackupValue( uint16_t plc_addr )
{
  if( plc_addr == ( uint16_t )( READ_BACKUP_END - 1U ) )
  {
    return ReadGlobalOrZero( BACKUP_HEARTBEAT_ADDR );
  }
  if( plc_addr == READ_BACKUP_END )
  {
    return ReadGlobalOrZero( BACKUP_HEARTBEAT_PLC_ADDR );
  }

  return ReadGlobalOrZero( ( uint16_t )( BACKUP_READ_DATA_BASE + ( plc_addr - READ_BACKUP_START ) ) );
}

static bool GetReadRange( uint8_t group_id, uint16_t *start, uint16_t *end )
{
  switch( group_id )
  {
    case PLC_GROUP_READ_DIO:
      *start = READ_DIO_START;
      *end = READ_DIO_END;
      return true;
    case PLC_GROUP_READ_HEATING:
      *start = READ_HEATING_START;
      *end = READ_HEATING_END;
      return true;
    case PLC_GROUP_READ_GAS:
      *start = READ_GAS_START;
      *end = READ_GAS_END;
      return true;
    case PLC_GROUP_READ_BACKUP:
      *start = READ_BACKUP_START;
      *end = READ_BACKUP_END;
      return true;
    default:
      return false;
  }
}

static bool GetWriteRange( uint8_t group_id, uint16_t *start, uint16_t *end )
{
  switch( group_id )
  {
    case PLC_GROUP_WRITE_DIO:
      *start = WRITE_DIO_START;
      *end = WRITE_DIO_END;
      return true;
    case PLC_GROUP_WRITE_HEATING:
      *start = WRITE_HEATING_START;
      *end = WRITE_HEATING_END;
      return true;
    case PLC_GROUP_WRITE_BACKUP:
      *start = WRITE_BACKUP_START;
      *end = WRITE_BACKUP_END;
      return true;
    case PLC_GROUP_WRITE_CONFIG:
      *start = WRITE_CONFIG_START;
      *end = WRITE_CONFIG_END;
      return true;
    default:
      return false;
  }
}

static uint16_t ReadGroupValue( uint8_t group_id, uint16_t plc_addr )
{
  switch( group_id )
  {
    case PLC_GROUP_READ_DIO:
      return ReadDioValue( plc_addr );
    case PLC_GROUP_READ_HEATING:
      return ReadHeatingValue( plc_addr );
    case PLC_GROUP_READ_GAS:
      return ReadGasValue( plc_addr );
    case PLC_GROUP_READ_BACKUP:
    default:
      return ReadBackupValue( plc_addr );
  }
}

static PlcGroupResult_t HandleGroupRead( const uint8_t *request, uint16_t request_len, uint8_t *response,
                                         uint16_t response_capacity, uint16_t *response_len )
{
  uint16_t values[ MODBUS_SLAVE_MAX_WRITE_VALUES ];
  const uint8_t group_id = request[ 0 ];
  const uint16_t address = Modbus_GetU16Be( &request[ 2 ] );
  const uint16_t quantity = Modbus_GetU16Be( &request[ 4 ] );
  uint16_t start;
  uint16_t end;
  uint16_t offset;

  if( ( request_len != MODBUS_RTU_READ_REQUEST_LENGTH ) || !GetReadRange( group_id, &start, &end ) )
  {
    return PLC_GROUP_RESULT_NO_RESPONSE;
  }

  if( ( quantity == 0U ) || ( quantity > MODBUS_SLAVE_MAX_WRITE_VALUES ) || ( address < start )
      || ( ( ( uint32_t )address + quantity - 1U ) > end ) )
  {
    return PLC_GROUP_RESULT_NO_RESPONSE;
  }

  for( offset = 0U; offset < quantity; offset++ )
  {
    values[ offset ] = ReadGroupValue( group_id, ( uint16_t )( address + offset ) );
  }

  if( !Modbus_BuildReadResponse( group_id, MODBUS_FC_READ_HOLDING_REGISTERS, values, quantity, response,
                                 response_capacity, response_len ) )
  {
    return PLC_GROUP_RESULT_NO_RESPONSE;
  }

  ( void )SharedMap_IncrementPlcHeartbeat();

  return PLC_GROUP_RESULT_RESPONSE_READY;
}

/* DIO write: PLC 주소 201/202 → 슬롯 0/1 물리 장치의 HR129 */
static void ApplyDioWrite( uint16_t address, uint16_t quantity, const uint16_t *values, PlcGroupWriteActions_t *actions )
{
  uint16_t offset;

  for( offset = 0U; offset < quantity; offset++ )
  {
    const uint8_t slot = ( uint8_t )( ( address + offset ) - WRITE_DIO_START );
    const uint8_t id = GetActiveIdBySlot( COMM_DEVICE_GROUP_DIO, slot );
    ModbusSlaveWriteRequest_t *fwd;

    if( id == 0U )
    {
      continue;
    }

    if( SharedMap_SetPendingWrite( id, SHARED_MAP_TABLE_HOLDING_REGISTER, DIO_OUTPUT_HOLDING_ADDR, values[ offset ] )
        && SharedMap_CommitWrite( id, SHARED_MAP_TABLE_HOLDING_REGISTER, DIO_OUTPUT_HOLDING_ADDR )
        && ( actions->forward_count < PLC_GROUP_MAX_FORWARDS ) )
    {
      fwd = &actions->forwards[ actions->forward_count ];
      ( void )memset( fwd, 0, sizeof( *fwd ) );
      fwd->slave_id = id;
      fwd->function = MODBUS_FC_WRITE_MULTIPLE_REGS;
      fwd->table = SHARED_MAP_TABLE_HOLDING_REGISTER;
      fwd->address = DIO_OUTPUT_HOLDING_ADDR;
      fwd->quantity = 1U;
      fwd->values[ 0 ] = values[ offset ];
      fwd->valid = 1U;
      actions->forward_count++;
    }
  }
}

/* EEPROM Save/ALARM Reset은 PLC가 쓴 값이 0→1로 바뀌는 순간에만 슬레이브로 전달한다.
 * PLC가 값을 1로 유지한 채 반복 write하면 슬레이브가 매번 재동작하므로 rising edge만 반영 */
static uint16_t s_heating_eeprom_save_prev[ MAX_ACTIVE_HEATING_ID_COUNT ];
static uint16_t s_heating_alarm_reset_prev[ MAX_ACTIVE_HEATING_ID_COUNT ];

/* PLC는 EEPROM Save/ALARM Reset을 0으로 되돌려 쓰지 않고, 명령이 끝나면 그냥 write를 멈춘다.
 * 마지막 write(값=1) 이후 HEATING_EEPROM_ALARM_TIMEOUT_MS 동안 재write가 없으면
 * PLC의 명령이 끝난 것으로 보고 PlcGroupMap_Poll()이 슬레이브에 0을 대신 write한다. */
#define HEATING_EEPROM_ALARM_TIMEOUT_MS 5000U
static uint32_t s_heating_eeprom_save_last_write_ms[ MAX_ACTIVE_HEATING_ID_COUNT ];
static uint32_t s_heating_alarm_reset_last_write_ms[ MAX_ACTIVE_HEATING_ID_COUNT ];

void PlcGroupMap_ResetHeatingEdgeState( uint8_t jacket )
{
  if( jacket >= MAX_ACTIVE_HEATING_ID_COUNT )
  {
    return;
  }

  s_heating_eeprom_save_prev[ jacket ] = 0U;
  s_heating_alarm_reset_prev[ jacket ] = 0U;
  s_heating_eeprom_save_last_write_ms[ jacket ] = 0U;
  s_heating_alarm_reset_last_write_ms[ jacket ] = 0U;
}

/* Heating write: PLC 주소 203~265 → Jacket별 HR2000~2020.
 * Jacket 내부에서 물리 주소가 연속이므로 Jacket당 1개의 forward로 합친다 */
static void ApplyHeatingWrite( uint16_t address, uint16_t quantity, const uint16_t *values,
                               PlcGroupWriteActions_t *actions )
{
  uint8_t jacket;

  for( jacket = 0U; jacket < MAX_ACTIVE_HEATING_ID_COUNT; jacket++ )
  {
    const uint16_t jacket_start = ( uint16_t )( WRITE_HEATING_START + ( ( uint16_t )jacket * HEATING_REGS_PER_JACKET ) );
    const uint16_t jacket_end = ( uint16_t )( jacket_start + HEATING_REGS_PER_JACKET - 1U );
    const uint16_t req_end = ( uint16_t )( address + quantity - 1U );
    const uint8_t id = GetActiveIdBySlot( COMM_DEVICE_GROUP_HEATING, jacket );
    uint16_t span_start;
    uint16_t span_end;
    uint16_t span_len;
    uint16_t offset;
    ModbusSlaveWriteRequest_t *fwd;
    bool committed = true;
    bool held_from_shared_map[ HEATING_REGS_PER_JACKET ] = { false };

    if( ( id == 0U ) || ( req_end < jacket_start ) || ( address > jacket_end ) )
    {
      continue;
    }

    span_start = ( address > jacket_start ) ? address : jacket_start;
    span_end = ( req_end < jacket_end ) ? req_end : jacket_end;
    span_len = ( uint16_t )( span_end - span_start + 1U );

    for( offset = 0U; offset < span_len; offset++ )
    {
      const uint16_t plc_addr = ( uint16_t )( span_start + offset );
      const uint16_t rel = ( uint16_t )( plc_addr - jacket_start );
      const uint16_t map_addr = ( uint16_t )( HEATING_HOLDING_BASE + rel );
      const uint16_t value = values[ plc_addr - address ];
      bool is_rising_edge = true;

      if( rel == HEATING_EEPROM_SAVE_OFFSET )
      {
        is_rising_edge = ( value == 1U ) && ( s_heating_eeprom_save_prev[ jacket ] == 0U );
        s_heating_eeprom_save_prev[ jacket ] = value;
        if( value == 1U )
        {
          s_heating_eeprom_save_last_write_ms[ jacket ] = HAL_GetTick();
        }
      }
      else if( rel == HEATING_ALARM_RESET_OFFSET )
      {
        is_rising_edge = ( value == 1U ) && ( s_heating_alarm_reset_prev[ jacket ] == 0U );
        s_heating_alarm_reset_prev[ jacket ] = value;
        if( value == 1U )
        {
          s_heating_alarm_reset_last_write_ms[ jacket ] = HAL_GetTick();
        }
      }

      if( !is_rising_edge )
      {
        /* rising edge가 아니면 SharedMap/forward에 값을 다시 밀어넣지 않는다 */
        held_from_shared_map[ offset ] = true;
        continue;
      }

      if( !SharedMap_SetPendingWrite( id, SHARED_MAP_TABLE_HOLDING_REGISTER, map_addr, value )
          || !SharedMap_CommitWrite( id, SHARED_MAP_TABLE_HOLDING_REGISTER, map_addr ) )
      {
        committed = false;
      }
    }

    if( committed && ( actions->forward_count < PLC_GROUP_MAX_FORWARDS ) )
    {
      fwd = &actions->forwards[ actions->forward_count ];
      ( void )memset( fwd, 0, sizeof( *fwd ) );
      fwd->slave_id = id;
      fwd->function = MODBUS_FC_WRITE_MULTIPLE_REGS;
      fwd->table = SHARED_MAP_TABLE_HOLDING_REGISTER;
      fwd->address = ( uint16_t )( HEATING_HOLDING_BASE + ( span_start - jacket_start ) );
      fwd->quantity = span_len;
      for( offset = 0U; offset < span_len; offset++ )
      {
        uint16_t fwd_value = values[ ( span_start + offset ) - address ];

        if( held_from_shared_map[ offset ] )
        {
          const uint16_t plc_addr = ( uint16_t )( span_start + offset );
          const uint16_t map_addr = ( uint16_t )( HEATING_HOLDING_BASE + ( plc_addr - jacket_start ) );

          /* Save/Reset이 rising edge가 아니면 마지막으로 SharedMap에 반영된 값을 그대로 forward */
          ( void )SharedMap_Read( id, SHARED_MAP_TABLE_HOLDING_REGISTER, map_addr, &fwd_value );
        }

        fwd->values[ offset ] = fwd_value;
      }
      fwd->valid = 1U;
      actions->forward_count++;
    }
  }
}

/* Backup write: PLC 주소 266~305 → global HR 201~240 (GST peer로 전달) */
static void ApplyBackupWrite( uint16_t address, uint16_t quantity, const uint16_t *values,
                              PlcGroupWriteActions_t *actions )
{
  uint16_t offset;

  for( offset = 0U; offset < quantity; offset++ )
  {
    const uint16_t map_addr = ( uint16_t )( BACKUP_WRITE_DATA_BASE + ( ( address + offset ) - WRITE_BACKUP_START ) );

    ( void )SharedMap_WriteGlobal( SHARED_MAP_TABLE_HOLDING_REGISTER, map_addr, values[ offset ] );
  }

  ( void )memset( &actions->gst_request, 0, sizeof( actions->gst_request ) );
  actions->gst_request.slave_id = 0U;
  actions->gst_request.function = MODBUS_FC_WRITE_MULTIPLE_REGS;
  actions->gst_request.table = SHARED_MAP_TABLE_HOLDING_REGISTER;
  actions->gst_request.address = ( uint16_t )( BACKUP_WRITE_DATA_BASE + ( address - WRITE_BACKUP_START ) );
  actions->gst_request.quantity = quantity;
  ( void )memcpy( actions->gst_request.values, values, ( size_t )quantity * sizeof( uint16_t ) );
  actions->gst_request.is_global = 1U;
  actions->gst_request.valid = 1U;
  actions->gst_forward = 1U;
}

/* Config write: PLC 주소 306~317 → global HR 110~121(ID/Type 설정 + apply trigger).
 * 317(=HR121, apply trigger)을 PLC가 명시적으로 write해야 ServiceConfigApply()가 반영을 시도한다 - Discovery 없음 */
static void ApplyConfigWrite( uint16_t address, uint16_t quantity, const uint16_t *values )
{
  uint16_t offset;

  for( offset = 0U; offset < quantity; offset++ )
  {
    const uint16_t map_addr = ( uint16_t )( CONFIG_HOLDING_BASE + ( ( address + offset ) - WRITE_CONFIG_START ) );

    ( void )SharedMap_WriteGlobal( SHARED_MAP_TABLE_HOLDING_REGISTER, map_addr, values[ offset ] );
  }
}

static PlcGroupResult_t HandleGroupWrite( const uint8_t *request, uint16_t request_len, uint8_t *response,
                                          uint16_t response_capacity, uint16_t *response_len,
                                          PlcGroupWriteActions_t *actions )
{
  uint16_t values[ MODBUS_SLAVE_MAX_WRITE_VALUES ];
  const uint8_t group_id = request[ 0 ];
  const uint16_t address = Modbus_GetU16Be( &request[ 2 ] );
  const uint16_t quantity = Modbus_GetU16Be( &request[ 4 ] );
  const uint8_t byte_count = request[ 6 ];
  uint16_t start;
  uint16_t end;
  uint16_t offset;
  uint16_t ack_len = 0U;

  if( !GetWriteRange( group_id, &start, &end ) )
  {
    return PLC_GROUP_RESULT_NO_RESPONSE;
  }

  if( ( quantity == 0U ) || ( quantity > MODBUS_SLAVE_MAX_WRITE_VALUES )
      || ( byte_count != ( uint8_t )( quantity * 2U ) ) || ( request_len != ( uint16_t )( 9U + byte_count ) )
      || ( address < start ) || ( ( ( uint32_t )address + quantity - 1U ) > end ) )
  {
    return PLC_GROUP_RESULT_NO_RESPONSE;
  }

  for( offset = 0U; offset < quantity; offset++ )
  {
    values[ offset ] = Modbus_GetU16Be( &request[ 7U + ( offset * 2U ) ] );
  }

  switch( group_id )
  {
    case PLC_GROUP_WRITE_DIO:
      ApplyDioWrite( address, quantity, values, actions );
      break;
    case PLC_GROUP_WRITE_HEATING:
      ApplyHeatingWrite( address, quantity, values, actions );
      break;
    case PLC_GROUP_WRITE_BACKUP:
      ApplyBackupWrite( address, quantity, values, actions );
      break;
    case PLC_GROUP_WRITE_CONFIG:
    default:
      ApplyConfigWrite( address, quantity, values );
      break;
  }

  if( !Modbus_BuildWriteMultipleAck( group_id, MODBUS_FC_WRITE_MULTIPLE_REGS, address, quantity, response,
                                     response_capacity, &ack_len ) )
  {
    return PLC_GROUP_RESULT_NO_RESPONSE;
  }

  *response_len = ack_len;
  ( void )SharedMap_IncrementPlcHeartbeat();

  return PLC_GROUP_RESULT_RESPONSE_READY;
}

PlcGroupResult_t PlcGroupMap_HandleRequest( const uint8_t *request, uint16_t request_len, uint8_t *response,
                                            uint16_t response_capacity, uint16_t *response_len,
                                            PlcGroupWriteActions_t *actions )
{
  uint8_t group_id;
  uint8_t function;

  if( response_len != NULL )
  {
    *response_len = 0U;
  }

  if( ( request == NULL ) || ( request_len < MODBUS_RTU_READ_REQUEST_LENGTH ) || ( response == NULL )
      || ( response_len == NULL ) || ( actions == NULL ) )
  {
    return PLC_GROUP_RESULT_NOT_HANDLED;
  }

  ( void )memset( actions, 0, sizeof( *actions ) );

  group_id = request[ 0 ];
  function = request[ 1 ];

  /* 그룹 ID(1~8) 이외는 기존 slave 경로에 넘긴다 */
  if( ( group_id < PLC_GROUP_READ_DIO ) || ( group_id > PLC_GROUP_WRITE_CONFIG ) )
  {
    return PLC_GROUP_RESULT_NOT_HANDLED;
  }

  if( !Modbus_VerifyCrc( request, request_len ) )
  {
    return PLC_GROUP_RESULT_NO_RESPONSE;
  }

  if( ( group_id <= PLC_GROUP_READ_BACKUP ) && ( function == MODBUS_FC_READ_HOLDING_REGISTERS ) )
  {
    return HandleGroupRead( request, request_len, response, response_capacity, response_len );
  }

  if( ( group_id >= PLC_GROUP_WRITE_DIO ) && ( function == MODBUS_FC_WRITE_MULTIPLE_REGS ) )
  {
    return HandleGroupWrite( request, request_len, response, response_capacity, response_len, actions );
  }

  /* 그룹 ID인데 function code가 사양과 다르면 응답하지 않는다 */
  return PLC_GROUP_RESULT_NO_RESPONSE;
}

/* prev==1 상태에서 HEATING_EEPROM_ALARM_TIMEOUT_MS 동안 재write가 없으면
 * PLC 명령이 끝난 것으로 보고 SharedMap을 0으로 되돌리고 슬레이브 forward를 채운다. */
static void CheckHeatingPulseTimeout( uint8_t id, uint16_t rel, uint32_t now, uint16_t *prev,
                                      uint32_t *last_write_ms, ModbusSlaveWriteRequest_t *forwards,
                                      uint8_t *forward_count, uint8_t max_forwards )
{
  const uint16_t map_addr = ( uint16_t )( HEATING_HOLDING_BASE + rel );
  ModbusSlaveWriteRequest_t *fwd;

  if( ( *prev != 1U ) || ( ( uint32_t )( now - *last_write_ms ) < ( uint32_t )HEATING_EEPROM_ALARM_TIMEOUT_MS ) )
  {
    return;
  }

  if( !SharedMap_SetPendingWrite( id, SHARED_MAP_TABLE_HOLDING_REGISTER, map_addr, 0U )
      || !SharedMap_CommitWrite( id, SHARED_MAP_TABLE_HOLDING_REGISTER, map_addr ) )
  {
    return;
  }

  *prev = 0U;

  if( *forward_count >= max_forwards )
  {
    return;
  }

  fwd = &forwards[ *forward_count ];
  ( void )memset( fwd, 0, sizeof( *fwd ) );
  fwd->slave_id = id;
  fwd->function = MODBUS_FC_WRITE_MULTIPLE_REGS;
  fwd->table = SHARED_MAP_TABLE_HOLDING_REGISTER;
  fwd->address = map_addr;
  fwd->quantity = 1U;
  fwd->values[ 0 ] = 0U;
  fwd->valid = 1U;
  ( *forward_count )++;
}

/* PLC가 EEPROM Save/ALARM Reset을 1로 유지한 채 write를 멈춘 경우를 감지해
 * 슬레이브로 0을 대신 write하도록 forward 목록을 채운다. 메인 루프에서 주기 호출한다. */
uint8_t PlcGroupMap_Poll( ModbusSlaveWriteRequest_t *forwards, uint8_t max_forwards )
{
  const uint32_t now = HAL_GetTick();
  uint8_t forward_count = 0U;
  uint8_t jacket;

  if( ( forwards == NULL ) || ( max_forwards == 0U ) )
  {
    return 0U;
  }

  for( jacket = 0U; jacket < MAX_ACTIVE_HEATING_ID_COUNT; jacket++ )
  {
    const uint8_t id = GetActiveIdBySlot( COMM_DEVICE_GROUP_HEATING, jacket );

    if( id == 0U )
    {
      continue;
    }

    CheckHeatingPulseTimeout( id, HEATING_EEPROM_SAVE_OFFSET, now, &s_heating_eeprom_save_prev[ jacket ],
                              &s_heating_eeprom_save_last_write_ms[ jacket ], forwards, &forward_count,
                              max_forwards );
    CheckHeatingPulseTimeout( id, HEATING_ALARM_RESET_OFFSET, now, &s_heating_alarm_reset_prev[ jacket ],
                              &s_heating_alarm_reset_last_write_ms[ jacket ], forwards, &forward_count,
                              max_forwards );
  }

  return forward_count;
}
