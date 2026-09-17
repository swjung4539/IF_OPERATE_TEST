#ifndef A_MODBUS_MASTER_H
#define A_MODBUS_MASTER_H

#ifdef __cplusplus
extern "C" {
#endif

#include "a_comm_config.h"
#include "a_modbus_slave.h"
#include "a_poll_table.h"

#include "stm32l4xx_hal.h"

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

typedef enum {
    MODBUS_MASTER_PORT_LPUART1 = 0,
    MODBUS_MASTER_PORT_USART2,
    MODBUS_MASTER_PORT_USART3,
    MODBUS_MASTER_PORT_COUNT
} ModbusMasterPortId_t;

typedef struct {
    UART_HandleTypeDef *huart;
    ModbusMasterPortId_t port_id;
    CommDeviceGroup_t group;
    uint16_t poll_period_ms;
    void (*set_tx_mode)(void);
    void (*set_rx_mode)(void);

    uint8_t  active_id_index;
    uint16_t poll_item_index;
    uint32_t last_poll_ms;

    uint8_t         waiting_response;
    uint8_t         pending_is_write;
    uint32_t        tx_sent_ms;
    uint8_t         pending_slave_id;
    PollTableItem_t pending_item;
    uint16_t        pending_response_len;
    size_t          pending_poll_count;
    uint8_t         pending_active_count;
    uint8_t         pending_attempts;
    uint8_t         pending_request[MODBUS_RTU_READ_REQUEST_LENGTH];
    uint16_t        pending_request_len;

    /* config(ID/Type) 기반으로 등록된 슬롯 상태. Discovery(프로브) 없음 */
    uint32_t        enabled_mask;
    bool            slaves_registered;
    uint8_t         slot_profiles[MAX_ACTIVE_ID_COUNT];

    /* 직전에 SharedMap에 등록했던 실제 슬레이브 ID 목록. 국번(ID) 변경 시
     * 새 목록에 없는 이전 ID를 SharedMap에서 UnregisterSlave하기 위해 유지한다. */
    uint8_t         registered_ids[MAX_ACTIVE_ID_COUNT];
    uint8_t         registered_count;
} ModbusMasterPortContext_t;

void ModbusMaster_RegisterConfiguredSlaves(ModbusMasterPortContext_t *ctx);
void ModbusMaster_InitContext(ModbusMasterPortContext_t *ctx,
                              UART_HandleTypeDef *huart,
                              ModbusMasterPortId_t port_id,
                              CommDeviceGroup_t group,
                              uint16_t poll_period_ms,
                              void (*set_tx_mode)(void),
                              void (*set_rx_mode)(void));

bool ModbusMaster_InitQueues(void);
bool ModbusMaster_ForwardWrite(const ModbusSlaveWriteRequest_t *pending);
bool ModbusMaster_ForwardRecoveredWrite(const ModbusSlaveWriteRequest_t *pending);
void ModbusMaster_TxStep(ModbusMasterPortContext_t *ctx);
void ModbusMaster_RxStep(ModbusMasterPortContext_t *ctx);
void ModbusMaster_ProcessStep(ModbusMasterPortContext_t *ctx);

#ifdef __cplusplus
}
#endif

#endif /* A_MODBUS_MASTER_H */
