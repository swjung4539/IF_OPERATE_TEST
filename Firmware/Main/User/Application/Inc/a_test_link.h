#ifndef A_TEST_LINK_H
#define A_TEST_LINK_H

#include "main.h"
#include <stdbool.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

void TestLink_Init(void);
void TestLink_OnUsbReceive(const uint8_t *data, uint32_t len);
void TestLink_Process(void);
bool TestLink_IsFaultOverrideActive(void);
GPIO_PinState TestLink_GetFaultState(void);

#ifdef __cplusplus
}
#endif

#endif /* A_TEST_LINK_H */
