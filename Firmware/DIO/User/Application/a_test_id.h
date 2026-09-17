#ifndef A_TEST_ID_H
#define A_TEST_ID_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

uint8_t TestId_ReadRaw(void);
uint8_t TestId_ReadValue(void);

#ifdef __cplusplus
}
#endif

#endif /* A_TEST_ID_H */
