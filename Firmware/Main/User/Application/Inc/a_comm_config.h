#ifndef A_COMM_CONFIG_H
#define A_COMM_CONFIG_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdbool.h>
#include <stdint.h>

#define DEFAULT_LPUART1_POLL_PERIOD_MS          50U
#define DEFAULT_USART2_POLL_PERIOD_MS           50U
#define DEFAULT_USART3_POLL_PERIOD_MS           50U
#define DEFAULT_USART1_PERIOD_MS                50U
#define DEFAULT_TX_TIMEOUT_MS                   50U
#define DEFAULT_LPUART1_RESPONSE_TIMEOUT_MS     50U
#define DEFAULT_USART2_RESPONSE_TIMEOUT_MS     200U
#define DEFAULT_USART3_RESPONSE_TIMEOUT_MS     300U
#define DEFAULT_RETRY_COUNT                      3U

/* Common debounce for PLC, configured slaves and the GST backup link. */
#define COMM_FAULT_DEBOUNCE_MS                2000U

#define MAX_ACTIVE_ID_COUNT             10U
#define MAX_ACTIVE_DIO_ID_COUNT         2U
#define MAX_ACTIVE_HEATING_ID_COUNT     3U
#define MAX_ACTIVE_GAS_ID_COUNT         3U

#define COMM_CONFIG_RESULT_IDLE             0U
#define COMM_CONFIG_RESULT_OK               1U
#define COMM_CONFIG_RESULT_INVALID_ID       2U
#define COMM_CONFIG_RESULT_DUPLICATE_ID     3U
#define COMM_CONFIG_RESULT_INVALID_GAS_TYPE 4U
#define COMM_CONFIG_RESULT_INTERNAL_ERROR   5U

typedef enum {
    COMM_DEVICE_GROUP_DIO = 0,
    COMM_DEVICE_GROUP_HEATING,
    COMM_DEVICE_GROUP_GAS,
    COMM_DEVICE_GROUP_GST,
    COMM_DEVICE_GROUP_UNKNOWN
} CommDeviceGroup_t;

typedef enum {
    COMM_GAS_TYPE_NONE = 0,  /* 동작 안 함 - 해당 슬롯은 polling하지 않는다 */
    COMM_GAS_TYPE_H2 = 1,    /* Gas STD */
    COMM_GAS_TYPE_H2_TFT = 2 /* Gas TFT */
} CommGasType_t;

typedef struct {
    uint8_t ids[MAX_ACTIVE_ID_COUNT];
    uint8_t count;
} ActiveIdList_t;

typedef struct {
    uint8_t types[MAX_ACTIVE_GAS_ID_COUNT];
} GasTypeList_t;

typedef struct {
    uint16_t lpuart1_poll_period_ms;
    uint16_t usart2_poll_period_ms;
    uint16_t usart3_poll_period_ms;
    uint16_t usart1_period_ms;
    uint16_t tx_timeout_ms;
    uint16_t lpuart1_response_timeout_ms;
    uint16_t usart2_response_timeout_ms;
    uint16_t usart3_response_timeout_ms;
    uint8_t retry_count;

    ActiveIdList_t dio_ids;
    ActiveIdList_t heating_ids;
    ActiveIdList_t gas_ids;
    GasTypeList_t gas_types;
    uint32_t revision;
} CommConfig_t;

void CommConfig_InitDefaults(void);
const CommConfig_t *CommConfig_Get(void);
bool CommConfig_GetActiveIds(CommDeviceGroup_t group, ActiveIdList_t *out);
bool CommConfig_IsActiveId(CommDeviceGroup_t group, uint8_t id);
uint16_t CommConfig_ApplyDeviceSettings(const uint16_t *dio_ids,
                                        const uint16_t *heating_ids,
                                        const uint16_t *gas_ids,
                                        const uint16_t *gas_types);
uint32_t CommConfig_GetRevision(void);
CommGasType_t CommConfig_GetGasTypeByIndex(uint8_t index);

#ifdef __cplusplus
}
#endif

#endif /* A_COMM_CONFIG_H */
