#include "a_poll_table.h"

#define ARRAY_COUNT(array) (sizeof(array) / sizeof((array)[0]))

static const PollTableItem_t kDioPollItems[] = {
    {
        .function = POLL_TABLE_FUNCTION_FC03,
        .alternate_function = POLL_TABLE_FUNCTION_NONE,
        .table = SHARED_MAP_TABLE_HOLDING_REGISTER,
        .address = 130U,
        .quantity = 1U,
        .flags = POLL_TABLE_ITEM_FLAG_READ
    }
};

static const PollTableItem_t kDioWriteItems[] = {
    {
        .function = POLL_TABLE_FUNCTION_FC10,
        .alternate_function = POLL_TABLE_FUNCTION_NONE,
        .table = SHARED_MAP_TABLE_HOLDING_REGISTER,
        .address = 129U,
        .quantity = 1U,
        .flags = POLL_TABLE_ITEM_FLAG_WRITE
    }
};

static const PollTableItem_t kHeatingPollItems[] = {
    {
        .function = POLL_TABLE_FUNCTION_FC04,
        .alternate_function = POLL_TABLE_FUNCTION_NONE,
        .table = SHARED_MAP_TABLE_INPUT_REGISTER,
        .address = 1000U,
        .quantity = 18U,
        .flags = POLL_TABLE_ITEM_FLAG_READ
    },
    {
        .function = POLL_TABLE_FUNCTION_FC04,
        .alternate_function = POLL_TABLE_FUNCTION_NONE,
        .table = SHARED_MAP_TABLE_INPUT_REGISTER,
        .address = 1018U,
        .quantity = 18U,
        .flags = POLL_TABLE_ITEM_FLAG_READ
    },
    {
        .function = POLL_TABLE_FUNCTION_FC03,
        .alternate_function = POLL_TABLE_FUNCTION_NONE,
        .table = SHARED_MAP_TABLE_HOLDING_REGISTER,
        .address = 2000U,
        .quantity = 21U,
        .flags = POLL_TABLE_ITEM_FLAG_READ | POLL_TABLE_ITEM_FLAG_READBACK
    }
};

static const PollTableItem_t kHeatingWriteItems[] = {
    {
        .function = POLL_TABLE_FUNCTION_FC10,
        .alternate_function = POLL_TABLE_FUNCTION_FC06,
        .table = SHARED_MAP_TABLE_HOLDING_REGISTER,
        .address = 2000U,
        .quantity = 1U,
        .flags = POLL_TABLE_ITEM_FLAG_WRITE | POLL_TABLE_ITEM_FLAG_READBACK
    },
    {
        .function = POLL_TABLE_FUNCTION_FC10,
        .alternate_function = POLL_TABLE_FUNCTION_FC06,
        .table = SHARED_MAP_TABLE_HOLDING_REGISTER,
        .address = 2001U,
        .quantity = 1U,
        .flags = POLL_TABLE_ITEM_FLAG_WRITE | POLL_TABLE_ITEM_FLAG_READBACK
    },
    {
        .function = POLL_TABLE_FUNCTION_FC10,
        .alternate_function = POLL_TABLE_FUNCTION_FC06,
        .table = SHARED_MAP_TABLE_HOLDING_REGISTER,
        .address = 2002U,
        .quantity = 1U,
        .flags = POLL_TABLE_ITEM_FLAG_WRITE | POLL_TABLE_ITEM_FLAG_READBACK
    },
    {
        .function = POLL_TABLE_FUNCTION_FC10,
        .alternate_function = POLL_TABLE_FUNCTION_NONE,
        .table = SHARED_MAP_TABLE_HOLDING_REGISTER,
        .address = 2003U,
        .quantity = 18U,
        .flags = POLL_TABLE_ITEM_FLAG_WRITE | POLL_TABLE_ITEM_FLAG_READBACK
    }
};

static const PollTableItem_t kH2PollItems[] = {
    {
        .function = POLL_TABLE_FUNCTION_FC04,
        .alternate_function = POLL_TABLE_FUNCTION_NONE,
        .table = SHARED_MAP_TABLE_INPUT_REGISTER,
        .address = 30000U,
        .wire_address_offset = -30000,
        .quantity = 4U,
        .flags = POLL_TABLE_ITEM_FLAG_READ
    },
    {
        .function = POLL_TABLE_FUNCTION_FC02,
        .alternate_function = POLL_TABLE_FUNCTION_NONE,
        .table = SHARED_MAP_TABLE_DISCRETE_INPUT,
        .address = 10001U,
        .wire_address_offset = -10001,
        .quantity = 8U,
        .flags = POLL_TABLE_ITEM_FLAG_READ
    },
    {
        .function = POLL_TABLE_FUNCTION_FC01,
        .alternate_function = POLL_TABLE_FUNCTION_NONE,
        .table = SHARED_MAP_TABLE_COIL,
        .address = 2U,
        .quantity = 2U,
        .flags = POLL_TABLE_ITEM_FLAG_READ | POLL_TABLE_ITEM_FLAG_READBACK
    }
};

static const PollTableItem_t kH2WriteItems[] = {
    {
        .function = POLL_TABLE_FUNCTION_FC05,
        .alternate_function = POLL_TABLE_FUNCTION_FC0F,
        .table = SHARED_MAP_TABLE_COIL,
        .address = 3U,
        .quantity = 1U,
        .flags = POLL_TABLE_ITEM_FLAG_WRITE | POLL_TABLE_ITEM_FLAG_READBACK
    },
    {
        .function = POLL_TABLE_FUNCTION_FC05,
        .alternate_function = POLL_TABLE_FUNCTION_FC0F,
        .table = SHARED_MAP_TABLE_COIL,
        .address = 2U,
        .quantity = 1U,
        .flags = POLL_TABLE_ITEM_FLAG_WRITE | POLL_TABLE_ITEM_FLAG_READBACK
    }
};

static const PollTableItem_t kH2TftPollItems[] = {
    {
        .function = POLL_TABLE_FUNCTION_FC01,
        .alternate_function = POLL_TABLE_FUNCTION_NONE,
        .table = SHARED_MAP_TABLE_COIL,
        .address = 1U,
        .quantity = 4U,
        .flags = POLL_TABLE_ITEM_FLAG_READ | POLL_TABLE_ITEM_FLAG_READBACK
    },
    {
        .function = POLL_TABLE_FUNCTION_FC02,
        .alternate_function = POLL_TABLE_FUNCTION_NONE,
        .table = SHARED_MAP_TABLE_DISCRETE_INPUT,
        .address = 0U,
        .quantity = 16U,
        .flags = POLL_TABLE_ITEM_FLAG_READ
    },
    {
        .function = POLL_TABLE_FUNCTION_FC04,
        .alternate_function = POLL_TABLE_FUNCTION_NONE,
        .table = SHARED_MAP_TABLE_INPUT_REGISTER,
        .address = 1U,
        .quantity = 15U,
        .flags = POLL_TABLE_ITEM_FLAG_READ | POLL_TABLE_ITEM_FLAG_READBACK
    },
    {
        .function = POLL_TABLE_FUNCTION_FC04,
        .alternate_function = POLL_TABLE_FUNCTION_NONE,
        .table = SHARED_MAP_TABLE_INPUT_REGISTER,
        .address = 21U,
        .quantity = 15U,
        .flags = POLL_TABLE_ITEM_FLAG_READ | POLL_TABLE_ITEM_FLAG_READBACK
    },
    {
        .function = POLL_TABLE_FUNCTION_FC04,
        .alternate_function = POLL_TABLE_FUNCTION_NONE,
        .table = SHARED_MAP_TABLE_INPUT_REGISTER,
        .address = 85U,
        .quantity = 2U,
        .flags = POLL_TABLE_ITEM_FLAG_READ | POLL_TABLE_ITEM_FLAG_READBACK
    },
    {
        .function = POLL_TABLE_FUNCTION_FC04,
        .alternate_function = POLL_TABLE_FUNCTION_NONE,
        .table = SHARED_MAP_TABLE_INPUT_REGISTER,
        .address = 89U,
        .quantity = 1U,
        .flags = POLL_TABLE_ITEM_FLAG_READ | POLL_TABLE_ITEM_FLAG_READBACK
    },
    {
        .function = POLL_TABLE_FUNCTION_FC04,
        .alternate_function = POLL_TABLE_FUNCTION_NONE,
        .table = SHARED_MAP_TABLE_INPUT_REGISTER,
        .address = 91U,
        .quantity = 6U,
        .flags = POLL_TABLE_ITEM_FLAG_READ | POLL_TABLE_ITEM_FLAG_READBACK
    },
    {
        .function = POLL_TABLE_FUNCTION_FC03,
        .alternate_function = POLL_TABLE_FUNCTION_NONE,
        .table = SHARED_MAP_TABLE_HOLDING_REGISTER,
        .address = 1U,
        .quantity = 15U,
        .flags = POLL_TABLE_ITEM_FLAG_READ | POLL_TABLE_ITEM_FLAG_READBACK
    },
    {
        .function = POLL_TABLE_FUNCTION_FC03,
        .alternate_function = POLL_TABLE_FUNCTION_NONE,
        .table = SHARED_MAP_TABLE_HOLDING_REGISTER,
        .address = 21U,
        .quantity = 15U,
        .flags = POLL_TABLE_ITEM_FLAG_READ | POLL_TABLE_ITEM_FLAG_READBACK
    },
    {
        .function = POLL_TABLE_FUNCTION_FC03,
        .alternate_function = POLL_TABLE_FUNCTION_NONE,
        .table = SHARED_MAP_TABLE_HOLDING_REGISTER,
        .address = 85U,
        .quantity = 2U,
        .flags = POLL_TABLE_ITEM_FLAG_READ | POLL_TABLE_ITEM_FLAG_READBACK
    },
    {
        .function = POLL_TABLE_FUNCTION_FC03,
        .alternate_function = POLL_TABLE_FUNCTION_NONE,
        .table = SHARED_MAP_TABLE_HOLDING_REGISTER,
        .address = 89U,
        .quantity = 1U,
        .flags = POLL_TABLE_ITEM_FLAG_READ | POLL_TABLE_ITEM_FLAG_READBACK
    },
    {
        .function = POLL_TABLE_FUNCTION_FC03,
        .alternate_function = POLL_TABLE_FUNCTION_NONE,
        .table = SHARED_MAP_TABLE_HOLDING_REGISTER,
        .address = 91U,
        .quantity = 6U,
        .flags = POLL_TABLE_ITEM_FLAG_READ | POLL_TABLE_ITEM_FLAG_READBACK
    }
};

static const PollTableItem_t kH2TftWriteItems[] = {
    {
        .function = POLL_TABLE_FUNCTION_FC0F,
        .alternate_function = POLL_TABLE_FUNCTION_FC05,
        .table = SHARED_MAP_TABLE_COIL,
        .address = 1U,
        .quantity = 4U,
        .flags = POLL_TABLE_ITEM_FLAG_WRITE | POLL_TABLE_ITEM_FLAG_READBACK
    },
    {
        .function = POLL_TABLE_FUNCTION_FC10,
        .alternate_function = POLL_TABLE_FUNCTION_FC06,
        .table = SHARED_MAP_TABLE_HOLDING_REGISTER,
        .address = 1U,
        .quantity = 15U,
        .flags = POLL_TABLE_ITEM_FLAG_WRITE | POLL_TABLE_ITEM_FLAG_READBACK
    },
    {
        .function = POLL_TABLE_FUNCTION_FC10,
        .alternate_function = POLL_TABLE_FUNCTION_FC06,
        .table = SHARED_MAP_TABLE_HOLDING_REGISTER,
        .address = 21U,
        .quantity = 15U,
        .flags = POLL_TABLE_ITEM_FLAG_WRITE | POLL_TABLE_ITEM_FLAG_READBACK
    },
    {
        .function = POLL_TABLE_FUNCTION_FC10,
        .alternate_function = POLL_TABLE_FUNCTION_FC06,
        .table = SHARED_MAP_TABLE_HOLDING_REGISTER,
        .address = 85U,
        .quantity = 2U,
        .flags = POLL_TABLE_ITEM_FLAG_WRITE | POLL_TABLE_ITEM_FLAG_READBACK
    },
    {
        .function = POLL_TABLE_FUNCTION_FC10,
        .alternate_function = POLL_TABLE_FUNCTION_FC06,
        .table = SHARED_MAP_TABLE_HOLDING_REGISTER,
        .address = 89U,
        .quantity = 1U,
        .flags = POLL_TABLE_ITEM_FLAG_WRITE | POLL_TABLE_ITEM_FLAG_READBACK
    },
    {
        .function = POLL_TABLE_FUNCTION_FC10,
        .alternate_function = POLL_TABLE_FUNCTION_FC06,
        .table = SHARED_MAP_TABLE_HOLDING_REGISTER,
        .address = 91U,
        .quantity = 6U,
        .flags = POLL_TABLE_ITEM_FLAG_WRITE | POLL_TABLE_ITEM_FLAG_READBACK
    }
};

PollTableProfile_t PollTable_GetProfileForGroupId( CommDeviceGroup_t group, uint8_t id )
{
  if( !CommConfig_IsActiveId( group, id ) )
  {
    return POLL_TABLE_PROFILE_UNKNOWN;
  }

  switch( group )
  {
    case COMM_DEVICE_GROUP_DIO:
      return POLL_TABLE_PROFILE_DIO;

    case COMM_DEVICE_GROUP_HEATING:
      return POLL_TABLE_PROFILE_HEATING;

    case COMM_DEVICE_GROUP_GAS:
    case COMM_DEVICE_GROUP_GST:
    case COMM_DEVICE_GROUP_UNKNOWN:
    default:
      return POLL_TABLE_PROFILE_UNKNOWN;
  }
}

const PollTableItem_t* PollTable_GetPollItems( PollTableProfile_t profile, size_t *count )
{
  const PollTableItem_t *items = NULL;
  size_t item_count = 0U;

  switch( profile )
  {
    case POLL_TABLE_PROFILE_DIO:
      items = kDioPollItems;
      item_count = ARRAY_COUNT( kDioPollItems );
      break;

    case POLL_TABLE_PROFILE_HEATING:
      items = kHeatingPollItems;
      item_count = ARRAY_COUNT( kHeatingPollItems );
      break;

    case POLL_TABLE_PROFILE_H2:
      items = kH2PollItems;
      item_count = ARRAY_COUNT( kH2PollItems );
      break;

    case POLL_TABLE_PROFILE_H2_TFT:
      items = kH2TftPollItems;
      item_count = ARRAY_COUNT( kH2TftPollItems );
      break;

    case POLL_TABLE_PROFILE_UNKNOWN:
    default:
      items = NULL;
      item_count = 0U;
      break;
  }

  if( count != NULL )
  {
    *count = item_count;
  }

  return items;
}

static const PollTableItem_t* PollTable_GetWriteItems( PollTableProfile_t profile, size_t *count )
{
  const PollTableItem_t *items = NULL;
  size_t item_count = 0U;

  switch( profile )
  {
    case POLL_TABLE_PROFILE_DIO:
      items = kDioWriteItems;
      item_count = ARRAY_COUNT( kDioWriteItems );
      break;

    case POLL_TABLE_PROFILE_HEATING:
      items = kHeatingWriteItems;
      item_count = ARRAY_COUNT( kHeatingWriteItems );
      break;

    case POLL_TABLE_PROFILE_H2:
      items = kH2WriteItems;
      item_count = ARRAY_COUNT( kH2WriteItems );
      break;

    case POLL_TABLE_PROFILE_H2_TFT:
      items = kH2TftWriteItems;
      item_count = ARRAY_COUNT( kH2TftWriteItems );
      break;

    case POLL_TABLE_PROFILE_UNKNOWN:
    default:
      items = NULL;
      item_count = 0U;
      break;
  }

  if( count != NULL )
  {
    *count = item_count;
  }

  return items;
}

const PollTableItem_t* PollTable_FindWriteItem( PollTableProfile_t profile, SharedMapTable_t table, uint16_t address )
{
  size_t count;
  size_t index;
  const PollTableItem_t *items = PollTable_GetWriteItems( profile, &count );

  if( items == NULL )
  {
    return NULL;
  }

  for( index = 0U; index < count; index++ )
  {
    const PollTableItem_t *item = &items[ index ];
    const uint32_t start_address = item->address;
    const uint32_t end_address = start_address + item->quantity;

    if( ( item->table == table ) && ( ( uint32_t )address >= start_address ) && ( ( uint32_t )address < end_address ) )
    {
      return item;
    }
  }

  return NULL;
}
