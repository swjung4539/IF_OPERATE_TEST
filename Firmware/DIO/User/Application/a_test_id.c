#include "a_test_id.h"
#include "main.h"

uint8_t TestId_ReadRaw(void)
{
  uint8_t raw = 0U;

  if( HAL_GPIO_ReadPin( ID_SET1_GPIO_Port, ID_SET1_Pin ) == GPIO_PIN_SET )
  {
    raw |= 0x02U;
  }
  if( HAL_GPIO_ReadPin( ID_SET2_GPIO_Port, ID_SET2_Pin ) == GPIO_PIN_SET )
  {
    raw |= 0x01U;
  }
  return raw;
}

uint8_t TestId_ReadValue(void)
{
  /* 회로도 기준: LOW/LOW=ID1, LOW/HIGH=ID2, HIGH/LOW=ID3, HIGH/HIGH=ID4. */
  return (uint8_t)( TestId_ReadRaw() + 1U );
}
