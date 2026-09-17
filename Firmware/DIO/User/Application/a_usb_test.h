#ifndef A_USB_TEST_H
#define A_USB_TEST_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

void DioUsbTest_Init(void);
void DioUsbTest_OnReceive(const uint8_t *data, uint32_t length);
void DioUsbTest_Process(void);

#ifdef __cplusplus
}
#endif

#endif /* A_USB_TEST_H */
