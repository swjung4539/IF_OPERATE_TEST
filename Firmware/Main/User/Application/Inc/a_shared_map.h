#ifndef A_SHARED_MAP_H
#define A_SHARED_MAP_H

#ifdef __cplusplus
extern "C" {
#endif

#include "a_comm_config.h"

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

typedef enum {
    SHARED_MAP_TABLE_COIL = 0,
    SHARED_MAP_TABLE_DISCRETE_INPUT,
    SHARED_MAP_TABLE_INPUT_REGISTER,
    SHARED_MAP_TABLE_HOLDING_REGISTER
} SharedMapTable_t;

typedef struct {
    uint32_t last_update_ms;
    uint16_t address;
    uint16_t value;
    uint16_t pending_value;
    uint8_t id;
    uint8_t table;
    uint8_t valid : 1;
    uint8_t dirty : 1;
    uint8_t writable : 1;
    uint8_t readable : 1;
    uint8_t global : 1;
} SharedMapEntry_t;

void SharedMap_Init(void);
void SharedMap_SetCurrentTimeMs(uint32_t now_ms);

SharedMapEntry_t *SharedMap_Find(uint8_t id, SharedMapTable_t table, uint16_t address);
SharedMapEntry_t *SharedMap_FindGlobal(SharedMapTable_t table, uint16_t address);

bool SharedMap_Read(uint8_t id, SharedMapTable_t table, uint16_t address, uint16_t *value);
bool SharedMap_ReadGlobal(SharedMapTable_t table, uint16_t address, uint16_t *value);
bool SharedMap_WriteGlobal(SharedMapTable_t table, uint16_t address, uint16_t value);

bool SharedMap_SetPendingWrite(uint8_t id, SharedMapTable_t table, uint16_t address, uint16_t value);
bool SharedMap_SetPendingWriteGlobal(SharedMapTable_t table, uint16_t address, uint16_t value);
bool SharedMap_CommitWrite(uint8_t id, SharedMapTable_t table, uint16_t address);
bool SharedMap_CommitWriteGlobal(SharedMapTable_t table, uint16_t address);
bool SharedMap_DiscardPendingWrite(uint8_t id, SharedMapTable_t table, uint16_t address);
bool SharedMap_DiscardPendingWriteGlobal(SharedMapTable_t table, uint16_t address);

bool SharedMap_UpdateFromPoll(uint8_t id, SharedMapTable_t table, uint16_t address, uint16_t value);
void SharedMap_RegisterSlave(uint8_t id, CommDeviceGroup_t group);
void SharedMap_UnregisterSlave(uint8_t id, CommDeviceGroup_t group);
bool SharedMap_SetCommStatus(CommDeviceGroup_t group, bool ok);
bool SharedMap_SetSlaveStatus(CommDeviceGroup_t group, uint8_t slave_index, bool ok);
bool SharedMap_IncrementPlcHeartbeat(void);
void SharedMap_ResetCommHealth(CommDeviceGroup_t group);
bool SharedMap_HasRecentCommSuccess(CommDeviceGroup_t group, uint8_t slave_index,
                                    uint32_t now_ms, uint32_t timeout_ms);

#ifdef __cplusplus
}
#endif

#endif /* A_SHARED_MAP_H */
