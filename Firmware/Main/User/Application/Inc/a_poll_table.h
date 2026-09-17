#ifndef A_POLL_TABLE_H
#define A_POLL_TABLE_H

#ifdef __cplusplus
extern "C" {
#endif

#include "a_comm_config.h"
#include "a_shared_map.h"

#include <stddef.h>
#include <stdint.h>

typedef enum {
    POLL_TABLE_FUNCTION_NONE = 0,
    POLL_TABLE_FUNCTION_FC01 = 1,
    POLL_TABLE_FUNCTION_FC02 = 2,
    POLL_TABLE_FUNCTION_FC03 = 3,
    POLL_TABLE_FUNCTION_FC04 = 4,
    POLL_TABLE_FUNCTION_FC05 = 5,
    POLL_TABLE_FUNCTION_FC06 = 6,
    POLL_TABLE_FUNCTION_FC0F = 15,
    POLL_TABLE_FUNCTION_FC10 = 16
} PollTableFunction_t;

typedef enum {
    POLL_TABLE_PROFILE_DIO = 0,
    POLL_TABLE_PROFILE_HEATING,
    POLL_TABLE_PROFILE_H2,
    POLL_TABLE_PROFILE_H2_TFT,
    POLL_TABLE_PROFILE_UNKNOWN
} PollTableProfile_t;

typedef enum {
    POLL_TABLE_ITEM_FLAG_READ = 0x01U,
    POLL_TABLE_ITEM_FLAG_WRITE = 0x02U,
    POLL_TABLE_ITEM_FLAG_READBACK = 0x04U
} PollTableItemFlags_t;

typedef struct {
    PollTableFunction_t function;
    PollTableFunction_t alternate_function;
    SharedMapTable_t table;
    uint16_t address;
    int32_t wire_address_offset;
    uint16_t quantity;
    uint8_t flags;
} PollTableItem_t;

PollTableProfile_t PollTable_GetProfileForGroupId(CommDeviceGroup_t group, uint8_t id);
const PollTableItem_t *PollTable_GetPollItems(PollTableProfile_t profile, size_t *count);
const PollTableItem_t *PollTable_FindWriteItem(PollTableProfile_t profile,
                                               SharedMapTable_t table,
                                               uint16_t address);

#ifdef __cplusplus
}
#endif

#endif /* A_POLL_TABLE_H */
