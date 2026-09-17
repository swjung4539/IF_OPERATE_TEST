#ifndef A_GST_PROTOCOL_H
#define A_GST_PROTOCOL_H

#ifdef __cplusplus
extern "C" {
#endif

#include "a_modbus_slave.h"

#include "cmsis_os.h"
#include "stm32l4xx_hal.h"

#include <stdbool.h>
#include <stdint.h>

#define GST_PROTOCOL_MAX_FRAME_LENGTH 93U

typedef bool (*GstProtocolAckCallback_t)(const uint8_t *frame, uint16_t length);

typedef struct {
    UART_HandleTypeDef *huart;
    uint16_t period_ms;
    void (*set_tx_mode)(void);
    void (*set_rx_mode)(void);
    GstProtocolAckCallback_t ack_callback;
    uint32_t last_transfer_ms;
    uint32_t last_rx_ms;
    uint32_t pending_started_ms;
    ModbusSlaveWriteRequest_t pending_write;
    osMessageQueueId_t rx_queue;
    osMessageQueueId_t tx_queue;
    uint8_t pending_active;
    uint8_t pending_tx_queued;
    uint8_t pending_waiting_response;
    uint8_t pending_done;
    uint8_t pending_success;
    uint8_t pending_attempts;
    uint8_t reassembly_buf[GST_PROTOCOL_MAX_FRAME_LENGTH];
    uint16_t reassembly_len;
    uint32_t reassembly_start_ms;
} GstProtocolContext_t;

void GstProtocol_InitContext(GstProtocolContext_t *ctx,
                             UART_HandleTypeDef *huart,
                             uint16_t period_ms,
                             void (*set_tx_mode)(void),
                             void (*set_rx_mode)(void));
bool GstProtocol_IsReady(const GstProtocolContext_t *ctx);
void GstProtocol_SetAckCallback(GstProtocolContext_t *ctx, GstProtocolAckCallback_t callback);

bool GstProtocol_ForwardWrite(GstProtocolContext_t *ctx,
                                    ModbusSlaveWriteRequest_t *pending);

void GstProtocol_RxStep(GstProtocolContext_t *ctx);
void GstProtocol_ProtocolStep(GstProtocolContext_t *ctx);
void GstProtocol_TxStep(GstProtocolContext_t *ctx);

#ifdef __cplusplus
}
#endif

#endif /* A_GST_PROTOCOL_H */
