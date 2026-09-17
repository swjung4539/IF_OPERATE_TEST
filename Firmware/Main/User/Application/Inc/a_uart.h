#ifndef A_UART_H
#define A_UART_H

#ifdef __cplusplus
extern "C" {
#endif

#include "stm32l4xx_hal.h"

#include <stdbool.h>
#include <stdint.h>

#define UART_FRAME_MAX_LENGTH 256U

void Uart_Init(void);
void Uart_RegisterDirControl(UART_HandleTypeDef *huart, void (*set_tx_mode)(void), void (*set_rx_mode)(void));
bool Uart_TransmitBlocking(UART_HandleTypeDef *huart, const uint8_t *data, uint16_t length, uint32_t timeout_ms);
bool Uart_StartReceive(UART_HandleTypeDef *huart);
void Uart_StopReceive(UART_HandleTypeDef *huart);
void Uart_ClearData(UART_HandleTypeDef *huart);
void Uart_ServiceRecovery(void);
bool Uart_ServiceRecoveryFor(UART_HandleTypeDef *huart);
bool Uart_GetData(UART_HandleTypeDef *huart, uint8_t *data, uint16_t capacity, uint16_t *length);
bool Uart_WaitData(UART_HandleTypeDef *huart,
                   uint8_t *data,
                   uint16_t capacity,
                   uint16_t *length,
                   uint32_t timeout_ms);

#ifdef __cplusplus
}
#endif

#endif /* A_UART_H */
