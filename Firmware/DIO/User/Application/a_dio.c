//*****************************************************************************/
//* @file : a_dio_modbus.c*/
//* @author : gjkwon1 (gjkwon1@gst-in.com)*/
//* @brief : Digital I/O to Modbus RTU Mapping Module*/
//* @version : 0.1*/
//* @date : 2025. 12. 29.
//*****************************************************************************/
//* @copyright Copyright (c) 2024 Global Standard Technology Co., Ltd.*/
//*****************************************************************************/

/*****************************************************************************/
/** INCLUDES *****************************************************************/
/*****************************************************************************/
#include "a_dio.h"
/*****************************************************************************/
/** DATATYPES ****************************************************************/
/*****************************************************************************/
typedef struct {
  GPIO_TypeDef *port;
  uint16_t pin;
} DIO_Pin_t;


#define DIO_WRITE_REG_ADDR   129U
#define DIO_READ_REG_ADDR    130U


/*****************************************************************************/
/** LOCAL VARIABLES **********************************************************/
/*****************************************************************************/
// Digital Output 핀 매핑 (D_OUTPUT1 ~ D_OUTPUT16)
static const DIO_Pin_t outputPins[DIO_OUTPUT_COUNT] = {
  {D_OUTPUT1_GPIO_Port, D_OUTPUT1_Pin},    // Coil 0
  {D_OUTPUT2_GPIO_Port, D_OUTPUT2_Pin},    // Coil 1
  {D_OUTPUT3_GPIO_Port, D_OUTPUT3_Pin},    // Coil 2
  {D_OUTPUT4_GPIO_Port, D_OUTPUT4_Pin},    // Coil 3
  {D_OUTPUT5_GPIO_Port, D_OUTPUT5_Pin},    // Coil 4
  {D_OUTPUT6_GPIO_Port, D_OUTPUT6_Pin},    // Coil 5
  {D_OUTPUT7_GPIO_Port, D_OUTPUT7_Pin},    // Coil 6
  {D_OUTPUT8_GPIO_Port, D_OUTPUT8_Pin},    // Coil 7
  {D_OUTPUT9_GPIO_Port, D_OUTPUT9_Pin},    // Coil 8
  {D_OUTPUT10_GPIO_Port, D_OUTPUT10_Pin},  // Coil 9
  {D_OUTPUT11_GPIO_Port, D_OUTPUT11_Pin},  // Coil 10
  {D_OUTPUT12_GPIO_Port, D_OUTPUT12_Pin},  // Coil 11
  {D_OUTPUT13_GPIO_Port, D_OUTPUT13_Pin},  // Coil 12
  {D_OUTPUT14_GPIO_Port, D_OUTPUT14_Pin},  // Coil 13
  {D_OUTPUT15_GPIO_Port, D_OUTPUT15_Pin},  // Coil 14
  {D_OUTPUT16_GPIO_Port, D_OUTPUT16_Pin}   // Coil 15
};

// Digital Input 핀 매핑 (D_INPUT1 ~ D_INPUT16)
static const DIO_Pin_t inputPins[DIO_INPUT_COUNT] = {
  {D_INPUT1_GPIO_Port, D_INPUT1_Pin},      // Discrete Input 0
  {D_INPUT2_GPIO_Port, D_INPUT2_Pin},      // Discrete Input 1
  {D_INPUT3_GPIO_Port, D_INPUT3_Pin},      // Discrete Input 2
  {D_INPUT4_GPIO_Port, D_INPUT4_Pin},      // Discrete Input 3
  {D_INPUT5_GPIO_Port, D_INPUT5_Pin},      // Discrete Input 4
  {D_INPUT6_GPIO_Port, D_INPUT6_Pin},      // Discrete Input 5
  {D_INPUT7_GPIO_Port, D_INPUT7_Pin},      // Discrete Input 6
  {D_INPUT8_GPIO_Port, D_INPUT8_Pin},      // Discrete Input 7
  {D_INPUT9_GPIO_Port, D_INPUT9_Pin},      // Discrete Input 8
  {D_INPUT10_GPIO_Port, D_INPUT10_Pin},    // Discrete Input 9
  {D_INPUT11_GPIO_Port, D_INPUT11_Pin},    // Discrete Input 10
  {D_INPUT12_GPIO_Port, D_INPUT12_Pin},    // Discrete Input 11
  {D_INPUT13_GPIO_Port, D_INPUT13_Pin},    // Discrete Input 12
  {D_INPUT14_GPIO_Port, D_INPUT14_Pin},    // Discrete Input 13
  {D_INPUT15_GPIO_Port, D_INPUT15_Pin},    // Discrete Input 14
  {D_INPUT16_GPIO_Port, D_INPUT16_Pin}     // Discrete Input 15
};

// Modbus 데이터 접근 (a_modbus_slave.c에서 정의됨)
extern uint8_t coils[];
extern uint8_t discreteInputs[];
extern uint16_t holdingRegisters[];
extern uint16_t inputRegisters[];
/*****************************************************************************/
/** FUNCTION DEFINITIONS *****************************************************/
/*****************************************************************************/

/******************************************************************************
 * @brief DIO Modbus 모듈 초기화
 *****************************************************************************/
void DIO_Modbus_Init(void)
{
  // 모든 출력을 LOW로 초기화
  for(uint8_t i = 0; i < DIO_OUTPUT_COUNT; i++)
  {
    HAL_GPIO_WritePin(outputPins[i].port, outputPins[i].pin, GPIO_PIN_RESET);
  }
}

/******************************************************************************
 * @brief Modbus Coils 데이터를 실제 Digital Output에 반영
 * @note holdingRegisters[1]의 각 비트가 D_OUTPUT1~16을 제어
 *****************************************************************************/
void DIO_Modbus_UpdateOutputs(uint16_t starting_address)
{
    uint16_t outputValue = 0;

    if (starting_address == DIO_WRITE_REG_ADDR)
    {
        outputValue = holdingRegisters[0];   // index 0 = address 129 (HoldingsStart=129)
    }
  
  for(uint8_t i = 0; i < DIO_OUTPUT_COUNT; i++)
  {
    // holdingRegisters[1]의 비트를 coils 배열에도 반영
    uint8_t bitValue = (outputValue >> i) & 0x01;
    coils[i] = bitValue;
    
    // GPIO 출력 (0 = LOW, 1 = HIGH)
    GPIO_PinState state = bitValue ? GPIO_PIN_SET : GPIO_PIN_RESET;
    HAL_GPIO_WritePin(outputPins[i].port, outputPins[i].pin, state);
  }
}

/******************************************************************************
 * @brief 실제 Digital Input을 읽어서 Modbus Discrete Inputs에 반영
 * @note GPIO 입력 상태를 discreteInputs[]와 holdingRegisters[0]에 저장
 *****************************************************************************/
void DIO_Modbus_ReadInputs(void)
{
  uint16_t inputValue = 0;  // 16비트 입력 값 초기화
  
  for(uint8_t i = 0; i < DIO_INPUT_COUNT; i++)
  {
    // GPIO 입력 읽기
    GPIO_PinState pinState = HAL_GPIO_ReadPin(inputPins[i].port, inputPins[i].pin);
    
    // Discrete Input에 저장 (0 or 1)
    discreteInputs[i] = (pinState == GPIO_PIN_SET) ? 1 : 0;
    
    if(discreteInputs[i])
    {
      inputValue |= (1 << i);  // 해당 비트를 1로 설정
    }
  }
  
  // 16비트 입력 상태를 holdingRegisters[0]에 저장
//   if (slave_address == 1) {
//   holdingRegisters[18] = inputValue;

// }
//   else if (slave_address == 2) {
// 	  holdingRegisters[19] = inputValue;
// }
  holdingRegisters[1] = inputValue;   // index 1 = address 130 (HoldingsStart=129)
  inputRegisters[0] = inputValue;     // index 0 = address 130 (InputsStart=130)
}
