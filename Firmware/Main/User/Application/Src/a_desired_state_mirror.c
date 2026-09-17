/*
 * [누가] IF 모듈의 Desired-State Mirror가
 * [언제] PLC write가 실제 Slave write로 변환된 뒤와 watchdog 재부팅 뒤에
 * [어디서] AT25SF041B Flash와 기존 Modbus master queue 사이에서
 * [무엇을] 마지막 상태형 write를 저장하고 복구한다.
 * [왜] PLC ACK 이후 IF 모듈이 reset되어도 Slave 목표 상태가 유실되지 않게 하기 위해서다.
 * [어떻게] checkpoint + delta ring wear-leveling과 CRC/commit 검증을 사용한다.
 */
#include "a_desired_state_mirror.h"

#include "a_at25sf041b.h"
#include "a_modbus_master.h"
#include "bootloader_shared.h"

#include <stddef.h>
#include <string.h>

#define MIRROR_FLASH_SIZE_BYTES       ( 512U * 1024U )
#define MIRROR_SECTOR_SIZE_BYTES      4096U
#define MIRROR_SECTOR_COUNT           ( MIRROR_FLASH_SIZE_BYTES / MIRROR_SECTOR_SIZE_BYTES )
#define MIRROR_SLOT_COUNT             64U
#define MIRROR_MAX_DELTA_VALUES       4U
#define MIRROR_FLUSH_PERIOD_MS        100U

#define MIRROR_HEADER_MAGIC           0x4D495231UL
#define MIRROR_DELTA_MAGIC            0x4452U
#define MIRROR_FORMAT_VERSION         1U
#define MIRROR_COMMIT_VALUE           0x00U
#define MIRROR_ERASED_VALUE           0xFFU

#define MIRROR_DIO_HOLDING_ADDRESS    129U
#define MIRROR_HEATING_BASE_ADDRESS   2000U
#define MIRROR_HEATING_LAST_ADDRESS   2020U
#define MIRROR_HEATING_EEPROM_ADDRESS 2001U
#define MIRROR_HEATING_ALARM_ADDRESS  2002U

typedef struct __attribute__( ( packed ) )
{
  uint8_t port;
  uint8_t slave_id;
  uint8_t table;
  uint8_t valid;
  uint16_t address;
  uint16_t value;
} MirrorSlot_t;

typedef struct __attribute__( ( packed ) )
{
  uint32_t magic;
  uint16_t version;
  uint16_t header_size;
  uint32_t generation;
  MirrorSlot_t slots[ MIRROR_SLOT_COUNT ];
  uint32_t crc32;
  uint8_t commit;
} MirrorSectorHeader_t;

typedef struct __attribute__( ( packed ) )
{
  uint16_t magic;
  uint8_t value_count;
  uint8_t reserved;
  uint64_t changed_mask;
  uint16_t values[ MIRROR_MAX_DELTA_VALUES ];
  uint32_t crc32;
  uint8_t commit;
} MirrorDeltaRecord_t;

static MirrorSlot_t s_slots[ MIRROR_SLOT_COUNT ];
static uint64_t s_dirty_mask;
static uint64_t s_restore_mask;
static uint8_t s_port_ready_mask;
static uint32_t s_last_flush_ms;
static uint32_t s_generation;
static uint32_t s_sector_index;
static uint32_t s_write_offset;
static bool s_checkpoint_required;
static bool s_ready;

static uint32_t mirror_sector_address( uint32_t sector_index )
{
  return sector_index * MIRROR_SECTOR_SIZE_BYTES;
}

static uint32_t mirror_header_crc( const MirrorSectorHeader_t *header )
{
  return BootShared_Crc32( ( const uint8_t * )header, ( uint32_t )offsetof( MirrorSectorHeader_t, crc32 ) );
}

static uint32_t mirror_delta_crc( const MirrorDeltaRecord_t *record )
{
  return BootShared_Crc32( ( const uint8_t * )record, ( uint32_t )offsetof( MirrorDeltaRecord_t, crc32 ) );
}

static bool mirror_header_is_valid( const MirrorSectorHeader_t *header )
{
  return ( header != NULL ) && ( header->magic == MIRROR_HEADER_MAGIC )
      && ( header->version == MIRROR_FORMAT_VERSION ) && ( header->header_size == sizeof( MirrorSectorHeader_t ) )
      && ( header->commit == MIRROR_COMMIT_VALUE ) && ( header->crc32 == mirror_header_crc( header ) );
}

static bool mirror_delta_is_valid( const MirrorDeltaRecord_t *record )
{
  return ( record != NULL ) && ( record->magic == MIRROR_DELTA_MAGIC )
      && ( record->value_count > 0U ) && ( record->value_count <= MIRROR_MAX_DELTA_VALUES )
      && ( record->commit == MIRROR_COMMIT_VALUE ) && ( record->crc32 == mirror_delta_crc( record ) );
}

static bool mirror_address_is_recoverable( const ModbusSlaveWriteRequest_t *request, uint16_t address )
{
  if( ( request == NULL ) || ( request->table != SHARED_MAP_TABLE_HOLDING_REGISTER ) )
  {
    return false;
  }

  if( address == MIRROR_DIO_HOLDING_ADDRESS )
  {
    return true;
  }

  /* [누가] Mirror 추적기가 Heating write를 받을 때, [언제] 주소가 HR2000~2020인 경우,
   * [어디서] 실제 Slave write 변환 이후에, [무엇을] EEPROM Save/Alarm Reset을 제외한다.
   * [왜] 펄스 명령을 reset 뒤 재실행하면 의도하지 않은 동작이 발생하기 때문이다.
   * [어떻게] 두 pulse 주소(2001, 2002)를 명시적으로 false 처리한다. */
  return ( address >= MIRROR_HEATING_BASE_ADDRESS ) && ( address <= MIRROR_HEATING_LAST_ADDRESS )
      && ( address != MIRROR_HEATING_EEPROM_ADDRESS ) && ( address != MIRROR_HEATING_ALARM_ADDRESS );
}

static int32_t mirror_find_slot( uint8_t port, uint8_t slave_id, uint8_t table, uint16_t address )
{
  uint32_t index;

  for( index = 0U; index < MIRROR_SLOT_COUNT; index++ )
  {
    if( ( s_slots[ index ].valid != 0U ) && ( s_slots[ index ].port == port )
        && ( s_slots[ index ].slave_id == slave_id ) && ( s_slots[ index ].table == table )
        && ( s_slots[ index ].address == address ) )
    {
      return ( int32_t )index;
    }
  }
  return -1;
}

static int32_t mirror_find_empty_slot( void )
{
  uint32_t index;

  for( index = 0U; index < MIRROR_SLOT_COUNT; index++ )
  {
    if( s_slots[ index ].valid == 0U )
    {
      return ( int32_t )index;
    }
  }
  return -1;
}

static bool mirror_write_header( uint32_t sector_index, uint32_t generation )
{
  MirrorSectorHeader_t header;
  const uint32_t sector_address = mirror_sector_address( sector_index );

  ( void )memset( &header, MIRROR_ERASED_VALUE, sizeof( header ) );
  header.magic = MIRROR_HEADER_MAGIC;
  header.version = MIRROR_FORMAT_VERSION;
  header.header_size = sizeof( header );
  header.generation = generation;
  ( void )memcpy( header.slots, s_slots, sizeof( s_slots ) );
  header.crc32 = mirror_header_crc( &header );
  header.commit = MIRROR_ERASED_VALUE;

  if( !At25Sf041b_PageProgram( sector_address, ( const uint8_t * )&header, sizeof( header ) ) )
  {
    return false;
  }
  return At25Sf041b_PageProgram( sector_address + offsetof( MirrorSectorHeader_t, commit ),
                                 &( ( const uint8_t ) { MIRROR_COMMIT_VALUE } ), 1U );
}

static bool mirror_start_next_sector( void )
{
  const uint32_t next_sector = ( s_sector_index + 1U ) % MIRROR_SECTOR_COUNT;

  /* [누가] Mirror가 현재 sector를 모두 사용했거나 새 Slave slot을 처음 발견했을 때,
   * [언제] 다음 Flash 기록 전에, [어디서] ring의 다음 4 KiB sector에서,
   * [무엇을] erase 후 전체 checkpoint를 새 generation으로 기록한다.
   * [왜] 새 slot의 주소 정보도 reset 후 복원 가능해야 하며 erase 수명을 전체 chip에 분산해야 하기 때문이다.
   * [어떻게] sector index를 순환시키고 header를 commit byte 마지막 기록 방식으로 확정한다. */
  if( !At25Sf041b_Erase4K( mirror_sector_address( next_sector ) )
      || !mirror_write_header( next_sector, s_generation + 1U ) )
  {
    return false;
  }

  s_sector_index = next_sector;
  s_generation++;
  s_write_offset = sizeof( MirrorSectorHeader_t );
  return true;
}

static void mirror_apply_delta( const MirrorDeltaRecord_t *record )
{
  uint32_t slot_index;
  uint8_t value_index = 0U;

  for( slot_index = 0U; slot_index < MIRROR_SLOT_COUNT; slot_index++ )
  {
    if( ( record->changed_mask & ( ( uint64_t )1U << slot_index ) ) != 0U )
    {
      if( ( value_index < record->value_count ) && ( s_slots[ slot_index ].valid != 0U ) )
      {
        s_slots[ slot_index ].value = record->values[ value_index ];
      }
      value_index++;
    }
  }
}

static void mirror_scan_deltas( void )
{
  MirrorDeltaRecord_t record;
  const uint32_t sector_end = mirror_sector_address( s_sector_index ) + MIRROR_SECTOR_SIZE_BYTES;

  s_write_offset = sizeof( MirrorSectorHeader_t );
  while( ( s_write_offset + sizeof( record ) ) <= MIRROR_SECTOR_SIZE_BYTES )
  {
    if( !At25Sf041b_Read( mirror_sector_address( s_sector_index ) + s_write_offset, ( uint8_t * )&record, sizeof( record ) ) )
    {
      return;
    }
    if( record.magic == 0xFFFFU )
    {
      return;
    }
    if( !mirror_delta_is_valid( &record ) )
    {
      return;
    }
    mirror_apply_delta( &record );
    s_write_offset += sizeof( record );
    if( ( mirror_sector_address( s_sector_index ) + s_write_offset ) >= sector_end )
    {
      return;
    }
  }
}

static bool mirror_append_delta( uint64_t dirty_mask )
{
  MirrorDeltaRecord_t record;
  uint32_t slot_index;
  uint8_t value_count = 0U;
  uint64_t written_mask = 0U;
  const uint32_t record_address = mirror_sector_address( s_sector_index ) + s_write_offset;

  if( ( s_write_offset + sizeof( record ) ) > MIRROR_SECTOR_SIZE_BYTES )
  {
    return false;
  }

  ( void )memset( &record, MIRROR_ERASED_VALUE, sizeof( record ) );
  record.magic = MIRROR_DELTA_MAGIC;
  record.value_count = 0U;
  record.reserved = 0U;

  for( slot_index = 0U; ( slot_index < MIRROR_SLOT_COUNT ) && ( value_count < MIRROR_MAX_DELTA_VALUES ); slot_index++ )
  {
    if( ( dirty_mask & ( ( uint64_t )1U << slot_index ) ) != 0U )
    {
      record.values[ value_count ] = s_slots[ slot_index ].value;
      written_mask |= ( uint64_t )1U << slot_index;
      value_count++;
    }
  }
  if( value_count == 0U )
  {
    return true;
  }

  record.changed_mask = written_mask;
  record.value_count = value_count;
  record.crc32 = mirror_delta_crc( &record );
  record.commit = MIRROR_ERASED_VALUE;
  if( !At25Sf041b_PageProgram( record_address, ( const uint8_t * )&record, sizeof( record ) )
      || !At25Sf041b_PageProgram( record_address + offsetof( MirrorDeltaRecord_t, commit ),
                                  &( ( const uint8_t ) { MIRROR_COMMIT_VALUE } ), 1U ) )
  {
    return false;
  }

  s_dirty_mask &= ~written_mask;
  s_write_offset += sizeof( record );
  return true;
}

static bool mirror_has_delta_room( void )
{
  return ( s_write_offset + sizeof( MirrorDeltaRecord_t ) ) <= MIRROR_SECTOR_SIZE_BYTES;
}

bool DesiredStateMirror_Init( void )
{
  MirrorSectorHeader_t header;
  uint32_t sector_index;
  bool header_found = false;

  ( void )memset( s_slots, 0, sizeof( s_slots ) );
  s_dirty_mask = 0U;
  s_restore_mask = 0U;
  s_port_ready_mask = 0U;
  s_last_flush_ms = HAL_GetTick();
  s_checkpoint_required = false;
  s_ready = false;

  /* [누가] IF 모듈이 watchdog reset 뒤 재부팅할 때, [언제] 하위 Slave task 시작 전,
   * [어디서] AT25SF041B 전체 sector ring에서, [무엇을] 가장 큰 generation의 checkpoint를 찾는다.
   * [왜] 마지막 유효 상태만 복구하고 중간 reset으로 깨진 record를 버리기 위해서다.
   * [어떻게] header magic/version/CRC/commit을 모두 확인한 sector만 후보로 사용한다. */
  for( sector_index = 0U; sector_index < MIRROR_SECTOR_COUNT; sector_index++ )
  {
    if( !At25Sf041b_Read( mirror_sector_address( sector_index ), ( uint8_t * )&header, sizeof( header ) ) )
    {
      return false;
    }
    if( mirror_header_is_valid( &header ) && ( !header_found || ( header.generation > s_generation ) ) )
    {
      header_found = true;
      s_sector_index = sector_index;
      s_generation = header.generation;
      ( void )memcpy( s_slots, header.slots, sizeof( s_slots ) );
    }
  }

  if( !header_found )
  {
    s_sector_index = 0U;
    s_generation = 0U;
    if( !At25Sf041b_Erase4K( mirror_sector_address( s_sector_index ) ) || !mirror_write_header( s_sector_index, s_generation ) )
    {
      return false;
    }
    s_write_offset = sizeof( MirrorSectorHeader_t );
  }
  else
  {
    mirror_scan_deltas();
    for( sector_index = 0U; sector_index < MIRROR_SLOT_COUNT; sector_index++ )
    {
      if( s_slots[ sector_index ].valid != 0U )
      {
        s_restore_mask |= ( uint64_t )1U << sector_index;
      }
    }
  }

  s_ready = true;
  return true;
}

void DesiredStateMirror_TrackWrite( uint8_t port, const ModbusSlaveWriteRequest_t *request )
{
  uint16_t offset;

  if( !s_ready || ( request == NULL ) || ( request->valid == 0U ) || ( request->is_global != 0U ) )
  {
    return;
  }

  for( offset = 0U; offset < request->quantity; offset++ )
  {
    const uint16_t address = ( uint16_t )( request->address + offset );
    int32_t slot_index;

    if( !mirror_address_is_recoverable( request, address ) )
    {
      continue;
    }
    slot_index = mirror_find_slot( port, request->slave_id, ( uint8_t )request->table, address );
    if( slot_index < 0 )
    {
      slot_index = mirror_find_empty_slot();
      if( slot_index < 0 )
      {
        continue;
      }
      s_slots[ slot_index ].port = port;
      s_slots[ slot_index ].slave_id = request->slave_id;
      s_slots[ slot_index ].table = ( uint8_t )request->table;
      s_slots[ slot_index ].address = address;
      s_slots[ slot_index ].valid = 1U;

      /* 새 slot의 식별자는 delta에 없으므로, 다음 Service에서 checkpoint sector를 새로 만든다. */
      s_dirty_mask |= ( uint64_t )1U << slot_index;
      s_slots[ slot_index ].value = request->values[ offset ];
      s_checkpoint_required = true;
      continue;
    }
    if( s_slots[ slot_index ].value != request->values[ offset ] )
    {
      s_slots[ slot_index ].value = request->values[ offset ];
      s_dirty_mask |= ( uint64_t )1U << slot_index;
    }
  }
}

void DesiredStateMirror_Service( uint32_t now_ms )
{
  if( !s_ready || ( s_dirty_mask == 0U ) || ( now_ms - s_last_flush_ms ) < MIRROR_FLUSH_PERIOD_MS )
  {
    return;
  }
  s_last_flush_ms = now_ms;
  if( s_checkpoint_required || !mirror_has_delta_room() )
  {
    if( mirror_start_next_sector() )
    {
      /* checkpoint에는 현재 모든 slot이 들어가므로 성공한 뒤에만 dirty를 해제한다. */
      s_dirty_mask = 0U;
      s_checkpoint_required = false;
    }
    return;
  }
  /*
   * [누가] Mirror Flash 저장부가
   * [언제] delta body 또는 commit 기록이 실패했을 때
   * [어디서] 현재 sector의 다음 record 위치에서
   * [무엇을] 손상 가능성이 있는 record 위치를 더 이상 재사용하지 않는다.
   * [왜] NOR Flash는 0에서 1로 되돌릴 수 없어 같은 위치 재시도가 CRC 오류를 고착시킬 수 있기 때문이다.
   * [어떻게] 다음 ring sector에 현재 RAM 상태 전체를 checkpoint로 기록한 뒤 dirty를 해제한다.
   */
  if( !mirror_append_delta( s_dirty_mask ) && mirror_start_next_sector() )
  {
    s_dirty_mask = 0U;
  }
}

void DesiredStateMirror_SetPortReady( uint8_t port )
{
  if( port < 8U )
  {
    s_port_ready_mask |= ( uint8_t )( 1U << port );
  }
}

void DesiredStateMirror_RestoreService( void )
{
  uint32_t slot_index;

  if( !s_ready || ( s_restore_mask == 0U ) )
  {
    return;
  }

  for( slot_index = 0U; slot_index < MIRROR_SLOT_COUNT; slot_index++ )
  {
    ModbusSlaveWriteRequest_t request;
    MirrorSlot_t *slot = &s_slots[ slot_index ];

    if( ( s_restore_mask & ( ( uint64_t )1U << slot_index ) ) == 0U )
    {
      continue;
    }
    if( ( slot->port >= 8U ) || ( s_port_ready_mask & ( uint8_t )( 1U << slot->port ) ) == 0U )
    {
      continue;
    }

    ( void )memset( &request, 0, sizeof( request ) );
    request.slave_id = slot->slave_id;
    request.function = MODBUS_FC_WRITE_MULTIPLE_REGS;
    request.table = ( SharedMapTable_t )slot->table;
    request.address = slot->address;
    request.quantity = 1U;
    request.values[ 0 ] = slot->value;
    request.valid = 1U;

    /* [누가] Mirror 복구 service가, [언제] 해당 Modbus port가 ready가 된 뒤,
     * [어디서] 기존 master write queue에, [무엇을] 마지막 상태형 값을 재투입한다.
     * [왜] PLC가 reset 후 명령을 재전송하지 않아도 Slave 상태를 복원하기 위해서다.
     * [어떻게] recovered API를 사용해 Flash에 다시 기록하지 않고 성공한 slot만 clear한다. */
    if( ModbusMaster_ForwardRecoveredWrite( &request ) )
    {
      s_restore_mask &= ~( ( uint64_t )1U << slot_index );
    }
    return;
  }
}
