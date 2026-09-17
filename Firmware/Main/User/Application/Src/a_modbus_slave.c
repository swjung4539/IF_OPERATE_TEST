#include "a_modbus_slave.h"

#include "a_poll_table.h"

#include <stddef.h>
#include <string.h>

#define MODBUS_SLAVE_ID_MAX 247U
#define CONFIG_HOLDING_START 110U
#define CONFIG_HOLDING_END   125U
#define SLAVE_STATUS_HOLDING_START 131U
#define SLAVE_STATUS_HOLDING_END   145U
#define GST_GLOBAL_HOLDING_START   199U
#define GST_GLOBAL_HOLDING_END     322U

static bool IsValidSlaveId( uint8_t slave_id )
{
  return slave_id <= MODBUS_SLAVE_ID_MAX;
}

static bool IsGlobalHoldingRange( SharedMapTable_t table, uint16_t address, uint16_t quantity )
{
  const uint32_t end_address = ( uint32_t )address + quantity - 1U;

  if( ( quantity == 0U ) || ( table != SHARED_MAP_TABLE_HOLDING_REGISTER ) )
  {
    return false;
  }

  if( ( address >= SLAVE_STATUS_HOLDING_START ) && ( end_address <= SLAVE_STATUS_HOLDING_END ) )
  {
    return true;
  }

  if( ( address >= CONFIG_HOLDING_START ) && ( end_address <= CONFIG_HOLDING_END ) )
  {
    return true;
  }

  return ( address >= GST_GLOBAL_HOLDING_START ) && ( end_address <= GST_GLOBAL_HOLDING_END );
}

static bool GetReadTable( uint8_t function, SharedMapTable_t *table )
{
  if( table == NULL )
  {
    return false;
  }

  switch( function )
  {
    case MODBUS_FC_READ_COILS:
      *table = SHARED_MAP_TABLE_COIL;
      return true;
    case MODBUS_FC_READ_DISCRETE_INPUTS:
      *table = SHARED_MAP_TABLE_DISCRETE_INPUT;
      return true;
    case MODBUS_FC_READ_HOLDING_REGISTERS:
      *table = SHARED_MAP_TABLE_HOLDING_REGISTER;
      return true;
    case MODBUS_FC_READ_INPUT_REGISTERS:
      *table = SHARED_MAP_TABLE_INPUT_REGISTER;
      return true;
    default:
      return false;
  }
}

static bool GetWriteTable( uint8_t function, SharedMapTable_t *table )
{
  if( table == NULL )
  {
    return false;
  }

  switch( function )
  {
    case MODBUS_FC_WRITE_SINGLE_COIL:
    case MODBUS_FC_WRITE_MULTIPLE_COILS:
      *table = SHARED_MAP_TABLE_COIL;
      return true;
    case MODBUS_FC_WRITE_SINGLE_REGISTER:
    case MODBUS_FC_WRITE_MULTIPLE_REGS:
      *table = SHARED_MAP_TABLE_HOLDING_REGISTER;
      return true;
    default:
      return false;
  }
}

static bool FunctionMatchesWriteItem( const PollTableItem_t *item, uint8_t function )
{
  return ( item != NULL ) && ( ( ( uint8_t )item->function == function ) || ( ( uint8_t )item->alternate_function == function ) );
}

static bool HasWriteItem( uint8_t slave_id, uint8_t function, SharedMapTable_t table, uint16_t address )
{
  const CommDeviceGroup_t non_gas_groups[ ] = { COMM_DEVICE_GROUP_DIO, COMM_DEVICE_GROUP_HEATING };
  size_t index;

  for( index = 0U; index < ( sizeof( non_gas_groups ) / sizeof( non_gas_groups[ 0 ] ) ); index++ )
  {
    const PollTableProfile_t profile = PollTable_GetProfileForGroupId( non_gas_groups[ index ], slave_id );
    const PollTableItem_t *item;

    if( profile == POLL_TABLE_PROFILE_UNKNOWN )
    {
      continue;
    }

    item = PollTable_FindWriteItem( profile, table, address );
    if( FunctionMatchesWriteItem( item, function ) )
    {
      return true;
    }
  }

  if( CommConfig_IsActiveId( COMM_DEVICE_GROUP_GAS, slave_id ) )
  {
    if( FunctionMatchesWriteItem( PollTable_FindWriteItem( POLL_TABLE_PROFILE_H2, table, address ), function ) )
    {
      return true;
    }
    if( FunctionMatchesWriteItem( PollTable_FindWriteItem( POLL_TABLE_PROFILE_H2_TFT, table, address ), function ) )
    {
      return true;
    }
  }

  return false;
}

static SharedMapEntry_t* FindTargetEntry( uint8_t slave_id, SharedMapTable_t table, uint16_t address,
bool global )
{
  if( global )
  {
    return SharedMap_FindGlobal( table, address );
  }

  return SharedMap_Find( slave_id, table, address );
}

static bool ReadRange( uint8_t slave_id, SharedMapTable_t table, uint16_t address, uint16_t quantity, uint16_t *values,
bool global )
{
  uint16_t offset;

  if( ( values == NULL ) || ( quantity == 0U ) )
  {
    return false;
  }

  for( offset = 0U; offset < quantity; offset++ )
  {
    const uint16_t current_address = ( uint16_t )( address + offset );
    const bool ok =
        global ?
            SharedMap_ReadGlobal( table, current_address, &values[ offset ] ) :
            SharedMap_Read( slave_id, table, current_address, &values[ offset ] );

    if( !ok )
    {
      return false;
    }
  }

  return true;
}

static bool ValidateWriteRange( uint8_t slave_id, uint8_t function, SharedMapTable_t table, uint16_t address, uint16_t quantity,
bool global )
{
  uint16_t offset;

  if( ( quantity == 0U ) || ( quantity > MODBUS_SLAVE_MAX_WRITE_VALUES ) )
  {
    return false;
  }

  for( offset = 0U; offset < quantity; offset++ )
  {
    const uint16_t current_address = ( uint16_t )( address + offset );
    SharedMapEntry_t *entry = FindTargetEntry( slave_id, table, current_address, global );

    if( ( entry == NULL ) || ( entry->writable == 0U ) )
    {
      return false;
    }

    if( !global && !HasWriteItem( slave_id, function, table, current_address ) )
    {
      return false;
    }
  }

  return true;
}

static bool SetPendingRange( ModbusSlaveWriteRequest_t *pending )
{
  uint16_t offset;

  if( ( pending == NULL ) || ( pending->quantity == 0U ) )
  {
    return false;
  }

  for( offset = 0U; offset < pending->quantity; offset++ )
  {
    const uint16_t current_address = ( uint16_t )( pending->address + offset );
    const bool ok =
        ( pending->is_global != 0U ) ?
            SharedMap_SetPendingWriteGlobal( pending->table, current_address, pending->values[ offset ] ) :
            SharedMap_SetPendingWrite( pending->slave_id, pending->table, current_address, pending->values[ offset ] );

    if( !ok )
    {
      uint16_t rollback;

      for( rollback = 0U; rollback < offset; rollback++ )
      {
        const uint16_t rollback_address = ( uint16_t )( pending->address + rollback );

        if( pending->is_global != 0U )
        {
          ( void )SharedMap_DiscardPendingWriteGlobal( pending->table, rollback_address );
        }
        else
        {
          ( void )SharedMap_DiscardPendingWrite( pending->slave_id, pending->table, rollback_address );
        }
      }

      return false;
    }
  }

  pending->valid = 1U;
  return true;
}

static void DiscardPendingRange( const ModbusSlaveWriteRequest_t *pending )
{
  uint16_t offset;

  if( ( pending == NULL ) || ( pending->quantity == 0U ) )
  {
    return;
  }

  for( offset = 0U; offset < pending->quantity; offset++ )
  {
    const uint16_t current_address = ( uint16_t )( pending->address + offset );

    if( pending->is_global != 0U )
    {
      ( void )SharedMap_DiscardPendingWriteGlobal( pending->table, current_address );
    }
    else
    {
      ( void )SharedMap_DiscardPendingWrite( pending->slave_id, pending->table, current_address );
    }
  }
}

static bool PendingRangeReadyToCommit( const ModbusSlaveWriteRequest_t *pending )
{
  uint16_t offset;

  if( ( pending == NULL ) || ( pending->quantity == 0U ) || ( pending->valid == 0U ) )
  {
    return false;
  }

  for( offset = 0U; offset < pending->quantity; offset++ )
  {
    const uint16_t current_address = ( uint16_t )( pending->address + offset );
    const SharedMapEntry_t *entry = FindTargetEntry( pending->slave_id, pending->table, current_address,
                                                     pending->is_global != 0U );

    if( ( entry == NULL ) || ( entry->writable == 0U ) || ( entry->dirty == 0U ) )
    {
      return false;
    }
  }

  return true;
}

static bool CommitPendingRange( const ModbusSlaveWriteRequest_t *pending )
{
  uint16_t offset;

  if( !PendingRangeReadyToCommit( pending ) )
  {
    return false;
  }

  for( offset = 0U; offset < pending->quantity; offset++ )
  {
    const uint16_t current_address = ( uint16_t )( pending->address + offset );
    const bool ok =
        ( pending->is_global != 0U ) ?
            SharedMap_CommitWriteGlobal( pending->table, current_address ) :
            SharedMap_CommitWrite( pending->slave_id, pending->table, current_address );

    if( !ok )
    {
      return false;
    }
  }

  return true;
}

static ModbusSlaveResult_t HandleReadRequest( const uint8_t *request, uint16_t request_len, uint8_t *response,
                                              uint16_t response_capacity, uint16_t *response_len )
{
  uint16_t values[ MODBUS_SLAVE_MAX_WRITE_VALUES ];
  SharedMapTable_t table;
  const uint8_t slave_id = request[ 0 ];
  const uint8_t function = request[ 1 ];
  const uint16_t address = Modbus_GetU16Be( &request[ 2 ] );
  const uint16_t quantity = Modbus_GetU16Be( &request[ 4 ] );
  const bool global = IsGlobalHoldingRange( SHARED_MAP_TABLE_HOLDING_REGISTER, address, quantity )
      && ( function == MODBUS_FC_READ_HOLDING_REGISTERS );

  if( ( request_len != MODBUS_RTU_READ_REQUEST_LENGTH ) || !GetReadTable( function, &table ) || ( quantity == 0U )
      || ( quantity > MODBUS_SLAVE_MAX_WRITE_VALUES ) )
  {
    return MODBUS_SLAVE_RESULT_NO_RESPONSE;
  }

  if( !global && IsGlobalHoldingRange( table, address, quantity ) )
  {
    return MODBUS_SLAVE_RESULT_NO_RESPONSE;
  }

  if( !ReadRange( slave_id, table, address, quantity, values, global ) )
  {
    return MODBUS_SLAVE_RESULT_NO_RESPONSE;
  }

  if( !Modbus_BuildReadResponse( slave_id, function, values, quantity, response, response_capacity, response_len ) )
  {
    return MODBUS_SLAVE_RESULT_NO_RESPONSE;
  }

  ( void )SharedMap_IncrementPlcHeartbeat();

  return MODBUS_SLAVE_RESULT_RESPONSE_READY;
}

static bool StorePendingHeader( ModbusSlaveWriteRequest_t *pending, uint8_t slave_id, uint8_t function, SharedMapTable_t table,
                                uint16_t address, uint16_t quantity,
                                bool global )
{
  if( ( pending == NULL ) || ( quantity == 0U ) || ( quantity > MODBUS_SLAVE_MAX_WRITE_VALUES ) )
  {
    return false;
  }

  ( void )memset( pending, 0, sizeof( *pending ) );
  pending->slave_id = slave_id;
  pending->function = function;
  pending->table = table;
  pending->address = address;
  pending->quantity = quantity;
  pending->is_global = global ? 1U : 0U;

  return true;
}

static ModbusSlaveResult_t HandleWriteSingleRequest( const uint8_t *request, uint16_t request_len,
                                                     ModbusSlaveWriteRequest_t *pending )
{
  SharedMapTable_t table;
  const uint8_t slave_id = request[ 0 ];
  const uint8_t function = request[ 1 ];
  const uint16_t address = Modbus_GetU16Be( &request[ 2 ] );
  const uint16_t raw_value = Modbus_GetU16Be( &request[ 4 ] );
  const bool global = IsGlobalHoldingRange( SHARED_MAP_TABLE_HOLDING_REGISTER, address, 1U )
      && ( function == MODBUS_FC_WRITE_SINGLE_REGISTER );
  uint16_t map_value = raw_value;

  if( ( request_len != MODBUS_RTU_READ_REQUEST_LENGTH ) || !GetWriteTable( function, &table ) || ( pending == NULL ) )
  {
    return MODBUS_SLAVE_RESULT_NO_RESPONSE;
  }

  if( function == MODBUS_FC_WRITE_SINGLE_COIL )
  {
    if( raw_value == 0xFF00U )
    {
      map_value = 1U;
    }
    else if( raw_value == 0x0000U )
    {
      map_value = 0U;
    }
    else
    {
      return MODBUS_SLAVE_RESULT_NO_RESPONSE;
    }
  }

  if( !global && IsGlobalHoldingRange( table, address, 1U ) )
  {
    return MODBUS_SLAVE_RESULT_NO_RESPONSE;
  }

  if( !ValidateWriteRange( slave_id, function, table, address, 1U, global ) || !StorePendingHeader( pending, slave_id, function,
                                                                                                    table, address, 1U, global ) )
  {
    return MODBUS_SLAVE_RESULT_NO_RESPONSE;
  }

  pending->values[ 0 ] = map_value;

  if( !Modbus_BuildWriteSingleAck( slave_id, function, address, raw_value, pending->ack_frame, sizeof( pending->ack_frame ),
                                   &pending->ack_length )
      || !SetPendingRange( pending ) )
  {
    return MODBUS_SLAVE_RESULT_NO_RESPONSE;
  }

  return MODBUS_SLAVE_RESULT_WRITE_PENDING;
}

static ModbusSlaveResult_t HandleWriteMultipleCoilsRequest( const uint8_t *request, uint16_t request_len,
                                                            ModbusSlaveWriteRequest_t *pending )
{
  const uint8_t slave_id = request[ 0 ];
  const uint8_t function = request[ 1 ];
  const uint16_t address = Modbus_GetU16Be( &request[ 2 ] );
  const uint16_t quantity = Modbus_GetU16Be( &request[ 4 ] );
  const uint8_t byte_count = request[ 6 ];
  const uint16_t expected_byte_count = ( uint16_t )( ( quantity + 7U ) / 8U );
  uint16_t offset;

  if( ( pending == NULL ) || ( quantity == 0U ) || ( quantity > MODBUS_SLAVE_MAX_WRITE_VALUES )
      || ( byte_count != expected_byte_count ) || ( request_len != ( uint16_t )( 9U + byte_count ) ) )
  {
    return MODBUS_SLAVE_RESULT_NO_RESPONSE;
  }

  if( !ValidateWriteRange( slave_id, function, SHARED_MAP_TABLE_COIL, address, quantity,
  false )
      || !StorePendingHeader( pending, slave_id, function, SHARED_MAP_TABLE_COIL, address, quantity,
      false ) )
  {
    return MODBUS_SLAVE_RESULT_NO_RESPONSE;
  }

  for( offset = 0U; offset < quantity; offset++ )
  {
    const uint8_t packed = request[ 7U + ( offset / 8U ) ];

    pending->values[ offset ] = ( ( packed & ( uint8_t )( 1U << ( offset % 8U ) ) ) != 0U ) ? 1U : 0U;
  }

  if( !Modbus_BuildWriteMultipleAck( slave_id, function, address, quantity, pending->ack_frame, sizeof( pending->ack_frame ),
                                     &pending->ack_length )
      || !SetPendingRange( pending ) )
  {
    return MODBUS_SLAVE_RESULT_NO_RESPONSE;
  }

  return MODBUS_SLAVE_RESULT_WRITE_PENDING;
}

static ModbusSlaveResult_t HandleWriteMultipleRegistersRequest( const uint8_t *request, uint16_t request_len,
                                                                ModbusSlaveWriteRequest_t *pending )
{
  const uint8_t slave_id = request[ 0 ];
  const uint8_t function = request[ 1 ];
  const uint16_t address = Modbus_GetU16Be( &request[ 2 ] );
  const uint16_t quantity = Modbus_GetU16Be( &request[ 4 ] );
  const uint8_t byte_count = request[ 6 ];
  const bool global = IsGlobalHoldingRange( SHARED_MAP_TABLE_HOLDING_REGISTER, address, quantity );
  uint16_t offset;

  if( ( pending == NULL ) || ( quantity == 0U ) || ( quantity > MODBUS_SLAVE_MAX_WRITE_REGISTERS )
      || ( byte_count != ( uint8_t )( quantity * 2U ) ) || ( request_len != ( uint16_t )( 9U + byte_count ) ) )
  {
    return MODBUS_SLAVE_RESULT_NO_RESPONSE;
  }

  if( !ValidateWriteRange( slave_id, function, SHARED_MAP_TABLE_HOLDING_REGISTER, address, quantity, global ) || !StorePendingHeader(
      pending, slave_id, function, SHARED_MAP_TABLE_HOLDING_REGISTER, address, quantity, global ) )
  {
    return MODBUS_SLAVE_RESULT_NO_RESPONSE;
  }

  for( offset = 0U; offset < quantity; offset++ )
  {
    pending->values[ offset ] = Modbus_GetU16Be( &request[ 7U + ( offset * 2U ) ] );
  }

  if( !Modbus_BuildWriteMultipleAck( slave_id, function, address, quantity, pending->ack_frame, sizeof( pending->ack_frame ),
                                     &pending->ack_length )
      || !SetPendingRange( pending ) )
  {
    return MODBUS_SLAVE_RESULT_NO_RESPONSE;
  }

  return MODBUS_SLAVE_RESULT_WRITE_PENDING;
}

ModbusSlaveResult_t ModbusSlave_HandleRequest( const uint8_t *request, uint16_t request_len, uint8_t *response,
                                               uint16_t response_capacity, uint16_t *response_len,
                                               ModbusSlaveWriteRequest_t *pending )
{
  uint8_t function;

  if( response_len != NULL )
  {
    *response_len = 0U;
  }

  if( ( pending != NULL ) && ( pending->valid != 0U ) )
  {
    DiscardPendingRange( pending );
  }

  if( pending != NULL )
  {
    pending->valid = 0U;
  }

  if( ( request == NULL ) || ( request_len < MODBUS_RTU_READ_REQUEST_LENGTH ) || ( response_len == NULL )
      || !IsValidSlaveId( request[ 0 ] ) || !Modbus_VerifyCrc( request, request_len ) )
  {
    return MODBUS_SLAVE_RESULT_NO_RESPONSE;
  }

  function = request[ 1 ];

  switch( function )
  {
    case MODBUS_FC_READ_COILS:
    case MODBUS_FC_READ_DISCRETE_INPUTS:
    case MODBUS_FC_READ_HOLDING_REGISTERS:
    case MODBUS_FC_READ_INPUT_REGISTERS:
      return HandleReadRequest( request, request_len, response, response_capacity, response_len );

    case MODBUS_FC_WRITE_SINGLE_COIL:
    case MODBUS_FC_WRITE_SINGLE_REGISTER:
      return HandleWriteSingleRequest( request, request_len, pending );

    case MODBUS_FC_WRITE_MULTIPLE_COILS:
      return HandleWriteMultipleCoilsRequest( request, request_len, pending );

    case MODBUS_FC_WRITE_MULTIPLE_REGS:
      return HandleWriteMultipleRegistersRequest( request, request_len, pending );

    default:
      return MODBUS_SLAVE_RESULT_NO_RESPONSE;
  }
}

bool ModbusSlave_ProcessWriteRequest( ModbusSlaveWriteRequest_t *pending,
bool success,
                                      uint8_t *response, uint16_t response_capacity, uint16_t *response_len )
{
  if( response_len != NULL )
  {
    *response_len = 0U;
  }

  if( ( pending == NULL ) || ( response_len == NULL ) || ( pending->valid == 0U ) )
  {
    return false;
  }

  if( !success )
  {
    DiscardPendingRange( pending );
    pending->valid = 0U;
    return false;
  }

  if( ( response == NULL ) || ( response_capacity < pending->ack_length ) )
  {
    DiscardPendingRange( pending );
    pending->valid = 0U;
    return false;
  }

  if( !CommitPendingRange( pending ) )
  {
    DiscardPendingRange( pending );
    pending->valid = 0U;
    return false;
  }

  ( void )SharedMap_IncrementPlcHeartbeat();

  ( void )memcpy( response, pending->ack_frame, pending->ack_length );
  *response_len = pending->ack_length;
  pending->valid = 0U;

  return true;
}
