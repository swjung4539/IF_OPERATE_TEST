#include "a_shared_map.h"

#include <string.h>

#if defined(__has_include)
#if __has_include("FreeRTOS.h") && __has_include("task.h")
#include "FreeRTOS.h"
#include "task.h"
#define SHARED_MAP_HAS_FREERTOS 1
#endif
#endif

#ifndef SHARED_MAP_HAS_FREERTOS
#define taskENTER_CRITICAL() do { } while (0)
#define taskEXIT_CRITICAL()  do { } while (0)
#endif

#define SLAVE_STATUS_START       131U
#define SLAVE_STATUS_END         145U
#define SLAVE_STATUS_PER_GROUP   5U

#define CONFIG_HOLDING_START     110U
#define CONFIG_HOLDING_END       122U

#define FW_VERSION_MAJOR_ADDR    123U
#define FW_VERSION_MINOR_ADDR    124U
#define FW_VERSION_PATCH_ADDR    125U
#define FW_VERSION_MAJOR         0U
#define FW_VERSION_MINOR         1U
#define FW_VERSION_PATCH         0U

#define GST_HOLDING_START  199U
#define GST_HOLDING_END    322U
#define GST_STATUS_ADDR    199U
#define GST_HEARTBEAT_ADDR 200U

/* Per-slot entry counts */
#define DIO_ENTRIES_PER_SLOT      2U    /* HR 129, 130 */
#define HEATING_ENTRIES_PER_SLOT  57U   /* IR 1000~1035(36) + HR 2000~2020(21) */
#define GAS_ENTRIES_PER_SLOT      110U  /* union of H2 and TFT (see RegisterSlave) */

/* Slot counts */
#define DIO_SLOT_COUNT     MAX_ACTIVE_DIO_ID_COUNT      /* 2 */
#define HEATING_SLOT_COUNT MAX_ACTIVE_HEATING_ID_COUNT  /* 3 */
#define GAS_SLOT_COUNT     MAX_ACTIVE_GAS_ID_COUNT      /* 3 */

/* Total pool capacity */
#define SLAVE_STATUS_ENTRY_COUNT (SLAVE_STATUS_END - SLAVE_STATUS_START + 1U)   /* 15  */
#define GST_ENTRY_COUNT          (GST_HOLDING_END - GST_HOLDING_START + 1U)     /* 124 */
#define DIO_ENTRY_COUNT          (DIO_SLOT_COUNT * DIO_ENTRIES_PER_SLOT)         /* 4   */
#define HEATING_ENTRY_COUNT      (HEATING_SLOT_COUNT * HEATING_ENTRIES_PER_SLOT) /* 171 */
#define GAS_ENTRY_COUNT          (GAS_SLOT_COUNT * GAS_ENTRIES_PER_SLOT)         /* 330 */
#define CONFIG_ENTRY_COUNT       (CONFIG_HOLDING_END - CONFIG_HOLDING_START + 1U) /* 13 */
#define FW_VERSION_ENTRY_COUNT   3U
#define GLOBAL_ENTRY_COUNT       (SLAVE_STATUS_ENTRY_COUNT + GST_ENTRY_COUNT + \
                                  CONFIG_ENTRY_COUNT + FW_VERSION_ENTRY_COUNT)

#define SHARED_MAP_ENTRY_CAPACITY (DIO_ENTRY_COUNT + HEATING_ENTRY_COUNT + \
                                   GAS_ENTRY_COUNT + GLOBAL_ENTRY_COUNT)

_Static_assert(SHARED_MAP_ENTRY_CAPACITY == 660U, "Unexpected shared map entry count");

#define COMM_STATUS_FAILURE_THRESHOLD 3U
#define SHARED_MAP_RAM2_SECTION __attribute__((section(".sharedmap_ram2"), aligned(4)))

static SharedMapEntry_t s_entries[ SHARED_MAP_ENTRY_CAPACITY ] SHARED_MAP_RAM2_SECTION;
static size_t s_entry_count SHARED_MAP_RAM2_SECTION;
static bool s_initialized SHARED_MAP_RAM2_SECTION;
static uint32_t s_current_time_ms SHARED_MAP_RAM2_SECTION;
static uint8_t s_comm_failure_counts[ COMM_DEVICE_GROUP_UNKNOWN ] SHARED_MAP_RAM2_SECTION;
static uint32_t s_comm_last_success_ms[ COMM_DEVICE_GROUP_UNKNOWN ][ SLAVE_STATUS_PER_GROUP ];
static bool s_comm_seen[ COMM_DEVICE_GROUP_UNKNOWN ][ SLAVE_STATUS_PER_GROUP ];

static SharedMapEntry_t* FindEntry( uint8_t id, SharedMapTable_t table, uint16_t address,
bool global )
{
  size_t index;
  SharedMapEntry_t *found = NULL;

  if( global )
  {
    const bool in_slave_status = ( address >= SLAVE_STATUS_START ) && ( address <= SLAVE_STATUS_END );
    const bool in_config_range = ( address >= CONFIG_HOLDING_START ) && ( address <= FW_VERSION_PATCH_ADDR );
    const bool in_gst_range = ( address >= GST_HOLDING_START ) && ( address <= GST_HOLDING_END );

    if( ( table != SHARED_MAP_TABLE_HOLDING_REGISTER ) || ( !in_slave_status && !in_config_range && !in_gst_range ) )
    {
      return NULL;
    }
  }

  taskENTER_CRITICAL();
  for( index = 0U; index < s_entry_count; index++ )
  {
    SharedMapEntry_t *entry = &s_entries[ index ];

    if( ( entry->valid != 0U ) && ( entry->global == ( global ? 1U : 0U ) ) && ( entry->table == ( uint8_t )table )
        && ( entry->address == address ) && ( global || ( entry->id == id ) ) )
    {
      found = entry;
      break;
    }
  }
  taskEXIT_CRITICAL();

  return found;
}

static void AddEntry( uint8_t id, SharedMapTable_t table, uint16_t address,
bool readable,
                      bool writable,
                      bool global )
{
  SharedMapEntry_t *entry;
  SharedMapEntry_t *free_slot = NULL;
  size_t index;

  for( index = 0U; index < s_entry_count; index++ )
  {
    entry = &s_entries[ index ];
    if( entry->valid == 0U )
    {
      /* 국번(ID) 변경 등으로 UnregisterSlave가 비워둔 슬롯은 재사용한다 */
      if( free_slot == NULL )
      {
        free_slot = entry;
      }
      continue;
    }

    if( ( entry->global == ( global ? 1U : 0U ) ) && ( entry->id == id )
        && ( entry->table == ( uint8_t )table ) && ( entry->address == address ) )
    {
      entry->readable = readable ? 1U : 0U;
      entry->writable = writable ? 1U : 0U;
      return;
    }
  }

  if( free_slot != NULL )
  {
    entry = free_slot;
  }
  else
  {
    if( s_entry_count >= SHARED_MAP_ENTRY_CAPACITY )
    {
      return;
    }

    entry = &s_entries[ s_entry_count++ ];
  }

  entry->id = id;
  entry->table = ( uint8_t )table;
  entry->address = address;
  entry->value = 0U;
  entry->pending_value = 0U;
  entry->valid = 1U;
  entry->dirty = 0U;
  entry->writable = writable ? 1U : 0U;
  entry->readable = readable ? 1U : 0U;
  entry->global = global ? 1U : 0U;
  entry->last_update_ms = 0U;
}

static void AddEntryWithValue( uint8_t id, SharedMapTable_t table, uint16_t address,
                               bool readable, bool writable, bool global, uint16_t value )
{
  SharedMapEntry_t *entry;

  AddEntry( id, table, address, readable, writable, global );
  entry = FindEntry( id, table, address, global );
  if( entry != NULL )
  {
    entry->value = value;
  }
}

static void AddRange( uint8_t id, SharedMapTable_t table, uint16_t start_address, uint16_t end_address,
bool readable,
                      bool writable,
                      bool global )
{
  uint16_t address;

  for( address = start_address; address <= end_address; address++ )
  {
    AddEntry( id, table, address, readable, writable, global );
  }
}

static void AddTftRegisterBlocks( uint8_t id, SharedMapTable_t table, bool readable, bool writable )
{
  AddRange( id, table, 1U, 15U, readable, writable, false );
  AddRange( id, table, 21U, 35U, readable, writable, false );
  AddRange( id, table, 85U, 86U, readable, writable, false );
  AddEntry( id, table, 89U, readable, writable, false );
  AddRange( id, table, 91U, 96U, readable, writable, false );
}

static void RemoveEntry( uint8_t id, SharedMapTable_t table, uint16_t address, bool global )
{
  SharedMapEntry_t *entry = FindEntry( id, table, address, global );

  if( entry != NULL )
  {
    entry->valid = 0U;
    entry->value = 0U;
    entry->pending_value = 0U;
    entry->dirty = 0U;
  }
}

static void RemoveRange( uint8_t id, SharedMapTable_t table, uint16_t start_address, uint16_t end_address,
                         bool global )
{
  uint16_t address;

  for( address = start_address; address <= end_address; address++ )
  {
    RemoveEntry( id, table, address, global );
  }
}

static void RemoveTftRegisterBlocks( uint8_t id, SharedMapTable_t table )
{
  RemoveRange( id, table, 1U, 15U, false );
  RemoveRange( id, table, 21U, 35U, false );
  RemoveRange( id, table, 85U, 86U, false );
  RemoveEntry( id, table, 89U, false );
  RemoveRange( id, table, 91U, 96U, false );
}

static void BuildStaticEntries( void )
{
  /* PLC device config registers: 110~122 */
  AddRange( 0U, SHARED_MAP_TABLE_HOLDING_REGISTER, CONFIG_HOLDING_START, CONFIG_HOLDING_END, true, true, true );
  AddEntryWithValue( 0U, SHARED_MAP_TABLE_HOLDING_REGISTER, 110U, true, true, true, 1U );
  AddEntryWithValue( 0U, SHARED_MAP_TABLE_HOLDING_REGISTER, 111U, true, true, true, 2U );
  AddEntryWithValue( 0U, SHARED_MAP_TABLE_HOLDING_REGISTER, 112U, true, true, true, 20U );
  AddEntryWithValue( 0U, SHARED_MAP_TABLE_HOLDING_REGISTER, 113U, true, true, true, 21U );
  AddEntryWithValue( 0U, SHARED_MAP_TABLE_HOLDING_REGISTER, 114U, true, true, true, 22U );
  AddEntryWithValue( 0U, SHARED_MAP_TABLE_HOLDING_REGISTER, 115U, true, true, true, 31U );
  AddEntryWithValue( 0U, SHARED_MAP_TABLE_HOLDING_REGISTER, 116U, true, true, true, 40U );
  AddEntryWithValue( 0U, SHARED_MAP_TABLE_HOLDING_REGISTER, 117U, true, true, true, 30U );

  /* Firmware version registers: 123~125 */
  AddEntryWithValue( 0U, SHARED_MAP_TABLE_HOLDING_REGISTER, FW_VERSION_MAJOR_ADDR, true, false, true, FW_VERSION_MAJOR );
  AddEntryWithValue( 0U, SHARED_MAP_TABLE_HOLDING_REGISTER, FW_VERSION_MINOR_ADDR, true, false, true, FW_VERSION_MINOR );
  AddEntryWithValue( 0U, SHARED_MAP_TABLE_HOLDING_REGISTER, FW_VERSION_PATCH_ADDR, true, false, true, FW_VERSION_PATCH );

  /* Slave status registers (global, read-only): 131~145 = 15 entries */
  AddRange( 0U, SHARED_MAP_TABLE_HOLDING_REGISTER, SLAVE_STATUS_START, SLAVE_STATUS_END, true, false, true );

  /* GST holding registers (global): 199~322 = 124 entries */
  AddEntry( 0U, SHARED_MAP_TABLE_HOLDING_REGISTER, 199U, true, false, true );
  AddEntry( 0U, SHARED_MAP_TABLE_HOLDING_REGISTER, 200U, true, false, true );
  AddRange( 0U, SHARED_MAP_TABLE_HOLDING_REGISTER, 201U, 241U, true, true, true );
  AddRange( 0U, SHARED_MAP_TABLE_HOLDING_REGISTER, 242U, 282U, true, false, true );
  AddRange( 0U, SHARED_MAP_TABLE_HOLDING_REGISTER, 283U, 322U, true, true, true );
}

static void BuildStaticEntriesUnlocked( void )
{
  s_initialized = false;
  ( void )memset( s_entries, 0, sizeof( s_entries ) );
  ( void )memset( s_comm_failure_counts, 0, sizeof( s_comm_failure_counts ) );
  ( void )memset( s_comm_last_success_ms, 0, sizeof( s_comm_last_success_ms ) );
  ( void )memset( s_comm_seen, 0, sizeof( s_comm_seen ) );
  s_entry_count = 0U;
  s_current_time_ms = 0U;
  BuildStaticEntries();
  s_initialized = true;
}

static void EnsureInitialized( void )
{
  taskENTER_CRITICAL();
  if( !s_initialized )
  {
    BuildStaticEntriesUnlocked();
  }
  taskEXIT_CRITICAL();
}

static bool ReadEntry( SharedMapEntry_t *entry, uint16_t *value )
{
  if( ( entry == NULL ) || ( value == NULL ) || ( entry->readable == 0U ) )
  {
    return false;
  }

  taskENTER_CRITICAL();
  *value = entry->value;
  taskEXIT_CRITICAL();

  return true;
}

static bool SetPendingEntry( SharedMapEntry_t *entry, uint16_t value )
{
  if( ( entry == NULL ) || ( entry->writable == 0U ) )
  {
    return false;
  }

  taskENTER_CRITICAL();
  entry->pending_value = value;
  entry->dirty = 1U;
  taskEXIT_CRITICAL();

  return true;
}

static bool CommitEntry( SharedMapEntry_t *entry )
{
  if( ( entry == NULL ) || ( entry->writable == 0U ) || ( entry->dirty == 0U ) )
  {
    return false;
  }

  taskENTER_CRITICAL();
  entry->value = entry->pending_value;
  entry->pending_value = 0U;
  entry->dirty = 0U;
  entry->last_update_ms = s_current_time_ms;
  taskEXIT_CRITICAL();

  return true;
}

static bool DiscardEntry( SharedMapEntry_t *entry )
{
  if( ( entry == NULL ) || ( entry->writable == 0U ) )
  {
    return false;
  }

  taskENTER_CRITICAL();
  entry->pending_value = 0U;
  entry->dirty = 0U;
  taskEXIT_CRITICAL();

  return true;
}

void SharedMap_Init( void )
{
  taskENTER_CRITICAL();
  BuildStaticEntriesUnlocked();
  taskEXIT_CRITICAL();
}

void SharedMap_SetCurrentTimeMs( uint32_t now_ms )
{
  taskENTER_CRITICAL();
  s_current_time_ms = now_ms;
  taskEXIT_CRITICAL();
}

void SharedMap_RegisterSlave( uint8_t id, CommDeviceGroup_t group )
{
  EnsureInitialized();

  taskENTER_CRITICAL();

  switch( group )
  {
    case COMM_DEVICE_GROUP_DIO:
      /* HR 129(write-only), HR 130(read-only) */
      AddEntry( id, SHARED_MAP_TABLE_HOLDING_REGISTER, 129U, false, true, false );
      AddEntry( id, SHARED_MAP_TABLE_HOLDING_REGISTER, 130U, true, false, false );
      break;

    case COMM_DEVICE_GROUP_HEATING:
      /* IR 1000~1035(read), HR 2000~2020(read/write) */
      AddRange( id, SHARED_MAP_TABLE_INPUT_REGISTER, 1000U, 1035U, true, false, false );
      AddRange( id, SHARED_MAP_TABLE_HOLDING_REGISTER, 2000U, 2020U, true, true, false );
      break;

    case COMM_DEVICE_GROUP_GAS:
      /* Union of H2 and TFT register sets (no duplicates) */
      /* Coil 1~4: TFT(1~4) covers H2(2~3) */
      AddRange( id, SHARED_MAP_TABLE_COIL, 1U, 4U, true, true, false );
      /* DI 0~15: TFT */
      AddRange( id, SHARED_MAP_TABLE_DISCRETE_INPUT, 0U, 15U, true, false, false );
      /* DI 10001~10008: H2 */
      AddRange( id, SHARED_MAP_TABLE_DISCRETE_INPUT, 10001U, 10008U, true, false, false );
      /* IR TFT blocks */
      AddTftRegisterBlocks( id, SHARED_MAP_TABLE_INPUT_REGISTER, true, false );
      /* IR 30000~30003: H2 */
      AddRange( id, SHARED_MAP_TABLE_INPUT_REGISTER, 30000U, 30003U, true, false, false );
      /* HR TFT blocks */
      AddTftRegisterBlocks( id, SHARED_MAP_TABLE_HOLDING_REGISTER, true, true );
      break;

    default:
      break;
  }

  taskEXIT_CRITICAL();
}

void SharedMap_UnregisterSlave( uint8_t id, CommDeviceGroup_t group )
{
  EnsureInitialized();

  taskENTER_CRITICAL();

  switch( group )
  {
    case COMM_DEVICE_GROUP_DIO:
      RemoveEntry( id, SHARED_MAP_TABLE_HOLDING_REGISTER, 129U, false );
      RemoveEntry( id, SHARED_MAP_TABLE_HOLDING_REGISTER, 130U, false );
      break;

    case COMM_DEVICE_GROUP_HEATING:
      RemoveRange( id, SHARED_MAP_TABLE_INPUT_REGISTER, 1000U, 1035U, false );
      RemoveRange( id, SHARED_MAP_TABLE_HOLDING_REGISTER, 2000U, 2020U, false );
      break;

    case COMM_DEVICE_GROUP_GAS:
      RemoveRange( id, SHARED_MAP_TABLE_COIL, 1U, 4U, false );
      RemoveRange( id, SHARED_MAP_TABLE_DISCRETE_INPUT, 0U, 15U, false );
      RemoveRange( id, SHARED_MAP_TABLE_DISCRETE_INPUT, 10001U, 10008U, false );
      RemoveTftRegisterBlocks( id, SHARED_MAP_TABLE_INPUT_REGISTER );
      RemoveRange( id, SHARED_MAP_TABLE_INPUT_REGISTER, 30000U, 30003U, false );
      RemoveTftRegisterBlocks( id, SHARED_MAP_TABLE_HOLDING_REGISTER );
      break;

    default:
      break;
  }

  taskEXIT_CRITICAL();
}

SharedMapEntry_t* SharedMap_Find( uint8_t id, SharedMapTable_t table, uint16_t address )
{
  EnsureInitialized();

  return FindEntry( id, table, address, false );
}

SharedMapEntry_t* SharedMap_FindGlobal( SharedMapTable_t table, uint16_t address )
{
  EnsureInitialized();

  return FindEntry( 0U, table, address, true );
}

bool SharedMap_Read( uint8_t id, SharedMapTable_t table, uint16_t address, uint16_t *value )
{
  return ReadEntry( SharedMap_Find( id, table, address ), value );
}

bool SharedMap_ReadGlobal( SharedMapTable_t table, uint16_t address, uint16_t *value )
{
  return ReadEntry( SharedMap_FindGlobal( table, address ), value );
}

bool SharedMap_WriteGlobal( SharedMapTable_t table, uint16_t address, uint16_t value )
{
  SharedMapEntry_t *entry = SharedMap_FindGlobal( table, address );

  if( entry == NULL )
  {
    return false;
  }

  taskENTER_CRITICAL();
  entry->value = value;
  entry->pending_value = 0U;
  entry->dirty = 0U;
  entry->last_update_ms = s_current_time_ms;
  taskEXIT_CRITICAL();

  return true;
}

bool SharedMap_SetPendingWrite( uint8_t id, SharedMapTable_t table, uint16_t address, uint16_t value )
{
  return SetPendingEntry( SharedMap_Find( id, table, address ), value );
}

bool SharedMap_SetPendingWriteGlobal( SharedMapTable_t table, uint16_t address, uint16_t value )
{
  return SetPendingEntry( SharedMap_FindGlobal( table, address ), value );
}

bool SharedMap_CommitWrite( uint8_t id, SharedMapTable_t table, uint16_t address )
{
  return CommitEntry( SharedMap_Find( id, table, address ) );
}

bool SharedMap_CommitWriteGlobal( SharedMapTable_t table, uint16_t address )
{
  return CommitEntry( SharedMap_FindGlobal( table, address ) );
}

bool SharedMap_DiscardPendingWrite( uint8_t id, SharedMapTable_t table, uint16_t address )
{
  return DiscardEntry( SharedMap_Find( id, table, address ) );
}

bool SharedMap_DiscardPendingWriteGlobal( SharedMapTable_t table, uint16_t address )
{
  return DiscardEntry( SharedMap_FindGlobal( table, address ) );
}

bool SharedMap_UpdateFromPoll( uint8_t id, SharedMapTable_t table, uint16_t address, uint16_t value )
{
  SharedMapEntry_t *entry = SharedMap_Find( id, table, address );

  if( ( entry == NULL ) || ( entry->readable == 0U ) )
  {
    return false;
  }

  taskENTER_CRITICAL();
  entry->value = value;
  entry->last_update_ms = s_current_time_ms;
  taskEXIT_CRITICAL();

  return true;
}

bool SharedMap_SetCommStatus( CommDeviceGroup_t group, bool ok )
{
  SharedMapEntry_t *entry = SharedMap_FindGlobal( SHARED_MAP_TABLE_HOLDING_REGISTER, GST_STATUS_ADDR );
  uint16_t mask;
  uint16_t old_value;

  if( entry == NULL )
  {
    return false;
  }

  switch( group )
  {
    case COMM_DEVICE_GROUP_DIO:
      mask = 0x0001U;
      break;
    case COMM_DEVICE_GROUP_HEATING:
      mask = 0x0002U;
      break;
    case COMM_DEVICE_GROUP_GAS:
      mask = 0x0004U;
      break;
    case COMM_DEVICE_GROUP_GST:
      mask = 0x0008U;
      break;
    case COMM_DEVICE_GROUP_UNKNOWN:
    default:
      return false;
  }

  taskENTER_CRITICAL();
  old_value = entry->value;

  if( ok )
  {
    s_comm_failure_counts[ group ] = 0U;
    s_comm_last_success_ms[ group ][ 0 ] = s_current_time_ms;
    s_comm_seen[ group ][ 0 ] = true;
    entry->value = ( uint16_t )( entry->value | mask );
  }
  else
  {
    if( s_comm_failure_counts[ group ] < COMM_STATUS_FAILURE_THRESHOLD )
    {
      s_comm_failure_counts[ group ]++;
    }

    if( s_comm_failure_counts[ group ] >= COMM_STATUS_FAILURE_THRESHOLD )
    {
      entry->value = ( uint16_t )( entry->value & ( uint16_t )~mask );
    }
  }

  if( ok || ( entry->value != old_value ) )
  {
    entry->last_update_ms = s_current_time_ms;
  }
  taskEXIT_CRITICAL();

  return true;
}

static const uint16_t s_slave_status_bases[ ] = { 131U, /* COMM_DEVICE_GROUP_DIO     */
                                                   136U, /* COMM_DEVICE_GROUP_HEATING */
                                                   141U, /* COMM_DEVICE_GROUP_GAS     */
                                                   0U, /* COMM_DEVICE_GROUP_GST     */
};

/*
 * 그룹 비트(HR199)는 폴링 결과 하나가 오는 대로 세팅되는 것이 아니라, 구성된(active) 슬레이브가
 * 전부 online일 때만 1이 되고, 하나라도 offline이면 계속 0을 유지하도록 슬레이브 상태를 AND로 재계산한다.
 * GST는 슬레이브 인덱스 개념이 없는 단일 디바이스라 SharedMap_SetCommStatus()가 직접 관리한다.
 */
static void RecomputeGroupCommBit( CommDeviceGroup_t group )
{
  static const uint16_t s_group_masks[ ] = { 0x0001U, /* COMM_DEVICE_GROUP_DIO     */
                                              0x0002U, /* COMM_DEVICE_GROUP_HEATING */
                                              0x0004U, /* COMM_DEVICE_GROUP_GAS     */
                                              0x0008U, /* COMM_DEVICE_GROUP_GST     */
  };
  ActiveIdList_t active_ids;
  SharedMapEntry_t *group_entry;
  SharedMapEntry_t *slave_entry;
  bool all_online;
  uint8_t i;

  if( ( ( uint32_t )group >= ( uint32_t )COMM_DEVICE_GROUP_UNKNOWN ) || ( s_slave_status_bases[ group ] == 0U ) )
  {
    return;
  }

  if( !CommConfig_GetActiveIds( group, &active_ids ) || ( active_ids.count == 0U ) )
  {
    return;
  }

  group_entry = SharedMap_FindGlobal( SHARED_MAP_TABLE_HOLDING_REGISTER, GST_STATUS_ADDR );
  if( group_entry == NULL )
  {
    return;
  }

  all_online = true;
  for( i = 0U; i < active_ids.count; i++ )
  {
    slave_entry = SharedMap_FindGlobal( SHARED_MAP_TABLE_HOLDING_REGISTER, ( uint16_t )( s_slave_status_bases[ group ] + i ) );
    if( ( slave_entry == NULL ) || ( ( slave_entry->value & 0x0100U ) == 0U ) )
    {
      all_online = false;
      break;
    }
  }

  taskENTER_CRITICAL();
  if( all_online )
  {
    group_entry->value = ( uint16_t )( group_entry->value | s_group_masks[ group ] );
  }
  else
  {
    group_entry->value = ( uint16_t )( group_entry->value & ( uint16_t )~s_group_masks[ group ] );
  }
  group_entry->last_update_ms = s_current_time_ms;
  taskEXIT_CRITICAL();
}

bool SharedMap_SetSlaveStatus( CommDeviceGroup_t group, uint8_t slave_index, bool ok )
{
  uint16_t address;
  SharedMapEntry_t *entry;
  uint16_t current;
  uint8_t fail;

  if( ( ( uint32_t )group >= ( uint32_t )COMM_DEVICE_GROUP_UNKNOWN ) || ( s_slave_status_bases[ group ] == 0U )
      || ( slave_index >= SLAVE_STATUS_PER_GROUP ) )
  {
    return false;
  }

  address = ( uint16_t )( s_slave_status_bases[ group ] + slave_index );
  entry = SharedMap_FindGlobal( SHARED_MAP_TABLE_HOLDING_REGISTER, address );
  if( entry == NULL )
  {
    return false;
  }

  taskENTER_CRITICAL();
  current = entry->value;

  if( ok )
  {
    entry->value = 0x0100U;
    s_comm_last_success_ms[ group ][ slave_index ] = s_current_time_ms;
    s_comm_seen[ group ][ slave_index ] = true;
  }
  else
  {
    fail = ( uint8_t )( current & 0x00FFU );
    if( fail < 0xFFU )
    {
      fail++;
    }
    entry->value = ( fail >= COMM_STATUS_FAILURE_THRESHOLD ) ? ( uint16_t )( fail ) : ( uint16_t )( 0x0100U | fail );
  }

  entry->last_update_ms = s_current_time_ms;
  taskEXIT_CRITICAL();

  RecomputeGroupCommBit( group );

  return true;
}

void SharedMap_ResetCommHealth( CommDeviceGroup_t group )
{
  if( ( uint32_t )group >= ( uint32_t )COMM_DEVICE_GROUP_UNKNOWN )
  {
    return;
  }
  taskENTER_CRITICAL();
  ( void )memset( s_comm_seen[ group ], 0, sizeof( s_comm_seen[ group ] ) );
  taskEXIT_CRITICAL();
}

bool SharedMap_HasRecentCommSuccess( CommDeviceGroup_t group, uint8_t slave_index,
                                     uint32_t now_ms, uint32_t timeout_ms )
{
  bool healthy;

  if( ( ( uint32_t )group >= ( uint32_t )COMM_DEVICE_GROUP_UNKNOWN )
      || ( slave_index >= SLAVE_STATUS_PER_GROUP ) )
  {
    return false;
  }
  if( group == COMM_DEVICE_GROUP_GST )
  {
    slave_index = 0U;
  }
  taskENTER_CRITICAL();
  /* A response task may have run after the caller sampled now_ms. */
  if( ( int32_t )( s_comm_last_success_ms[ group ][ slave_index ] - now_ms ) > 0 )
  {
    now_ms = s_comm_last_success_ms[ group ][ slave_index ];
  }
  healthy = s_comm_seen[ group ][ slave_index ]
            && ( ( uint32_t )( now_ms - s_comm_last_success_ms[ group ][ slave_index ] ) < timeout_ms );
  /* Once stale, only a new valid response can restore health, even across tick wrap. */
  if( ( uint32_t )( now_ms - s_comm_last_success_ms[ group ][ slave_index ] ) >= timeout_ms )
  {
    s_comm_seen[ group ][ slave_index ] = false;
  }
  taskEXIT_CRITICAL();
  return healthy;
}

bool SharedMap_IncrementPlcHeartbeat( void )
{
  SharedMapEntry_t *entry = SharedMap_FindGlobal( SHARED_MAP_TABLE_HOLDING_REGISTER, GST_HEARTBEAT_ADDR );

  if( entry == NULL )
  {
    return false;
  }

  taskENTER_CRITICAL();
  entry->value = ( uint16_t )( entry->value + 1U );
  entry->last_update_ms = s_current_time_ms;
  taskEXIT_CRITICAL();

  return true;
}
