#include "a_log.h"

uint8_t g_log_uart4   = 1U;
uint8_t g_log_slave   = 0U;
uint8_t g_log_master  = 0U;
uint8_t g_log_gst     = 0U;
uint8_t g_log_dio     = 0U;
uint8_t g_log_heating = 0U;
uint8_t g_log_gas     = 0U;

void PrintHex(const char *tag, const uint8_t *data, uint16_t len)
{
    uint16_t i;
    printf("%s", tag);
    for (i = 0U; i < len; i++) {
        printf("%02X ", data[i]);
    }
    printf("\r\n");
}
