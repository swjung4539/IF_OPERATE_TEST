#ifndef A_MODBUS_SLAVE_H
#define A_MODBUS_SLAVE_H

#ifdef __cplusplus
extern "C" {
#endif

#include "a_shared_map.h"
#include "m_modbus.h"

#include <stdbool.h>
#include <stdint.h>

#define MODBUS_SLAVE_MAX_WRITE_VALUES 124U
#define MODBUS_SLAVE_MAX_WRITE_REGISTERS 123U
#define MODBUS_SLAVE_MAX_ACK_LENGTH   MODBUS_RTU_WRITE_ACK_LENGTH

typedef enum {
    MODBUS_SLAVE_RESULT_NO_RESPONSE = 0,
    MODBUS_SLAVE_RESULT_RESPONSE_READY,
    MODBUS_SLAVE_RESULT_WRITE_PENDING
} ModbusSlaveResult_t;

typedef struct {
    uint8_t slave_id;
    uint8_t function;
    SharedMapTable_t table;
    uint16_t address;
    uint16_t quantity;
    uint16_t values[MODBUS_SLAVE_MAX_WRITE_VALUES];
    uint8_t is_global;
    uint8_t valid;
    uint8_t ack_frame[MODBUS_SLAVE_MAX_ACK_LENGTH];
    uint16_t ack_length;
} ModbusSlaveWriteRequest_t;

ModbusSlaveResult_t ModbusSlave_HandleRequest(const uint8_t *request,
                                              uint16_t request_len,
                                              uint8_t *response,
                                              uint16_t response_capacity,
                                              uint16_t *response_len,
                                              ModbusSlaveWriteRequest_t *pending);

bool ModbusSlave_ProcessWriteRequest(ModbusSlaveWriteRequest_t *pending,
                                      bool success,
                                      uint8_t *response,
                                      uint16_t response_capacity,
                                      uint16_t *response_len);

#ifdef __cplusplus
}
#endif

#endif /* A_MODBUS_SLAVE_H */
