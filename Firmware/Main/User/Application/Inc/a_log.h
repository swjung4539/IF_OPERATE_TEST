#ifndef A_LOG_H
#define A_LOG_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>
#include <stdio.h>

extern uint8_t g_log_uart4;
extern uint8_t g_log_slave;
extern uint8_t g_log_master;
extern uint8_t g_log_gst;
extern uint8_t g_log_dio;
extern uint8_t g_log_heating;
extern uint8_t g_log_gas;

#define LOG(flag, fmt, ...)          do { if (flag) printf(fmt, ##__VA_ARGS__); } while(0)
#define LOGHEX(flag, tag, data, len) do { if (flag) PrintHex(tag, data, len); } while(0)

void PrintHex(const char *tag, const uint8_t *data, uint16_t len);

#ifdef __cplusplus
}
#endif

#endif /* A_LOG_H */
