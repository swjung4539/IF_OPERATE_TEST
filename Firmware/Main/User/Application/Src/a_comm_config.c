#include "a_comm_config.h"

#include <string.h>

#if defined(__has_include)
#if __has_include("FreeRTOS.h") && __has_include("task.h")
#include "FreeRTOS.h"
#include "task.h"
#define COMM_CONFIG_HAS_FREERTOS 1
#endif
#endif

#ifndef COMM_CONFIG_HAS_FREERTOS
#define taskENTER_CRITICAL() do { } while (0)
#define taskEXIT_CRITICAL()  do { } while (0)
#endif

static const CommConfig_t kCommConfigDefaults = {
    .lpuart1_poll_period_ms = DEFAULT_LPUART1_POLL_PERIOD_MS,
    .usart2_poll_period_ms = DEFAULT_USART2_POLL_PERIOD_MS,
    .usart3_poll_period_ms = DEFAULT_USART3_POLL_PERIOD_MS,
    .usart1_period_ms = DEFAULT_USART1_PERIOD_MS,
    .tx_timeout_ms = DEFAULT_TX_TIMEOUT_MS,
    .lpuart1_response_timeout_ms = DEFAULT_LPUART1_RESPONSE_TIMEOUT_MS,
    .usart2_response_timeout_ms = DEFAULT_USART2_RESPONSE_TIMEOUT_MS,
    .usart3_response_timeout_ms = DEFAULT_USART3_RESPONSE_TIMEOUT_MS,
    .retry_count = DEFAULT_RETRY_COUNT,
    .dio_ids = {
        .ids = {1U, 2U},
        .count = 2U
    },
    .heating_ids = {
        .ids = {20U, 21U, 22U},
        .count = 3U
    },
    .gas_ids = {
        .ids = {30U, 40U, 50U},
        .count = 3U
    },
    .gas_types = {
        .types = {COMM_GAS_TYPE_H2, COMM_GAS_TYPE_H2, COMM_GAS_TYPE_H2_TFT}
    },
    .revision = 0U
};

static CommConfig_t s_comm_config;

void CommConfig_InitDefaults( void )
{
  ( void )memcpy( &s_comm_config, &kCommConfigDefaults, sizeof( s_comm_config ) );
}

const CommConfig_t* CommConfig_Get( void )
{
  return &s_comm_config;
}

bool CommConfig_GetActiveIds( CommDeviceGroup_t group, ActiveIdList_t *out )
{
  const ActiveIdList_t *active_ids = NULL;

  if( out == NULL )
  {
    return false;
  }

  switch( group )
  {
    case COMM_DEVICE_GROUP_DIO:
      active_ids = &s_comm_config.dio_ids;
      break;
    case COMM_DEVICE_GROUP_HEATING:
      active_ids = &s_comm_config.heating_ids;
      break;
    case COMM_DEVICE_GROUP_GAS:
      active_ids = &s_comm_config.gas_ids;
      break;
    case COMM_DEVICE_GROUP_GST:
    case COMM_DEVICE_GROUP_UNKNOWN:
    default:
      return false;
  }

  /* CommConfig_ApplyDeviceSettings()가 다른 태스크에서 재구성 중일 수 있으므로,
   * 포인터가 아니라 critical section 안에서 스냅샷을 통째로 복사해 반환한다. */
  taskENTER_CRITICAL();
  *out = *active_ids;
  taskEXIT_CRITICAL();

  return true;
}

bool CommConfig_IsActiveId( CommDeviceGroup_t group, uint8_t id )
{
  ActiveIdList_t active_ids;
  uint8_t index;

  if( !CommConfig_GetActiveIds( group, &active_ids ) )
  {
    return false;
  }

  for( index = 0U; index < active_ids.count; index++ )
  {
    if( active_ids.ids[ index ] == id )
    {
      return true;
    }
  }

  return false;
}

static bool IsValidId( uint16_t id )
{
  return ( id == 0U ) || ( ( id >= 1U ) && ( id <= 247U ) );
}

static bool HasDuplicateNonZeroId( const uint16_t *ids, uint8_t max_count )
{
  uint8_t i;
  uint8_t j;

  for( i = 0U; i < max_count; i++ )
  {
    if( ids[ i ] == 0U )
    {
      continue;
    }

    for( j = ( uint8_t )( i + 1U ); j < max_count; j++ )
    {
      if( ids[ i ] == ids[ j ] )
      {
        return true;
      }
    }
  }

  return false;
}

static void BuildActiveList( ActiveIdList_t *list, const uint16_t *ids, uint8_t max_count )
{
  uint8_t i;

  ( void )memset( list, 0, sizeof( *list ) );

  for( i = 0U; i < max_count; i++ )
  {
    if( ids[ i ] != 0U )
    {
      list->ids[ list->count ] = ( uint8_t )ids[ i ];
      list->count++;
    }
  }
}

uint16_t CommConfig_ApplyDeviceSettings( const uint16_t *dio_ids,
                                         const uint16_t *heating_ids,
                                         const uint16_t *gas_ids,
                                         const uint16_t *gas_types )
{
  uint8_t i;

  if( ( dio_ids == NULL ) || ( heating_ids == NULL ) || ( gas_ids == NULL ) || ( gas_types == NULL ) )
  {
    return COMM_CONFIG_RESULT_INTERNAL_ERROR;
  }

  for( i = 0U; i < MAX_ACTIVE_DIO_ID_COUNT; i++ )
  {
    if( !IsValidId( dio_ids[ i ] ) )
    {
      return COMM_CONFIG_RESULT_INVALID_ID;
    }
  }

  for( i = 0U; i < MAX_ACTIVE_HEATING_ID_COUNT; i++ )
  {
    if( !IsValidId( heating_ids[ i ] ) )
    {
      return COMM_CONFIG_RESULT_INVALID_ID;
    }
  }

  for( i = 0U; i < MAX_ACTIVE_GAS_ID_COUNT; i++ )
  {
    if( !IsValidId( gas_ids[ i ] ) )
    {
      return COMM_CONFIG_RESULT_INVALID_ID;
    }

    /* 0=동작 안 함, 1=STD, 2=TFT */
    if( gas_types[ i ] > ( uint16_t )COMM_GAS_TYPE_H2_TFT )
    {
      return COMM_CONFIG_RESULT_INVALID_GAS_TYPE;
    }
  }

  if( HasDuplicateNonZeroId( dio_ids, MAX_ACTIVE_DIO_ID_COUNT )
      || HasDuplicateNonZeroId( heating_ids, MAX_ACTIVE_HEATING_ID_COUNT )
      || HasDuplicateNonZeroId( gas_ids, MAX_ACTIVE_GAS_ID_COUNT ) )
  {
    return COMM_CONFIG_RESULT_DUPLICATE_ID;
  }

  /* 다른 태스크(폴링/PLC write)가 CommConfig_GetActiveIds()로 읽는 중일 수 있으므로,
   * 재구성 전체를 critical section으로 묶어 torn read를 방지한다. */
  taskENTER_CRITICAL();

  BuildActiveList( &s_comm_config.dio_ids, dio_ids, MAX_ACTIVE_DIO_ID_COUNT );
  BuildActiveList( &s_comm_config.heating_ids, heating_ids, MAX_ACTIVE_HEATING_ID_COUNT );

  /* Gas는 ID가 0이거나 Type이 0(동작 안 함)인 슬롯을 제외하고,
   * active 목록과 type 목록을 같은 순서로 함께 압축해 index 정합성을 유지한다 */
  ( void )memset( &s_comm_config.gas_ids, 0, sizeof( s_comm_config.gas_ids ) );
  ( void )memset( &s_comm_config.gas_types, 0, sizeof( s_comm_config.gas_types ) );
  for( i = 0U; i < MAX_ACTIVE_GAS_ID_COUNT; i++ )
  {
    if( ( gas_ids[ i ] != 0U ) && ( gas_types[ i ] != ( uint16_t )COMM_GAS_TYPE_NONE ) )
    {
      s_comm_config.gas_types.types[ s_comm_config.gas_ids.count ] = ( uint8_t )gas_types[ i ];
      s_comm_config.gas_ids.ids[ s_comm_config.gas_ids.count ] = ( uint8_t )gas_ids[ i ];
      s_comm_config.gas_ids.count++;
    }
  }

  s_comm_config.revision++;

  taskEXIT_CRITICAL();

  return COMM_CONFIG_RESULT_OK;
}

uint32_t CommConfig_GetRevision( void )
{
  return s_comm_config.revision;
}

CommGasType_t CommConfig_GetGasTypeByIndex( uint8_t index )
{
  if( index >= MAX_ACTIVE_GAS_ID_COUNT )
  {
    return COMM_GAS_TYPE_NONE;
  }

  return ( CommGasType_t )s_comm_config.gas_types.types[ index ];
}

