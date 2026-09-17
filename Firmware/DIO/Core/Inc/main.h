/* USER CODE BEGIN Header */
/**
  ******************************************************************************
  * @file           : main.h
  * @brief          : Header for main.c file.
  *                   This file contains the common defines of the application.
  ******************************************************************************
  * @attention
  *
  * Copyright (c) 2025 STMicroelectronics.
  * All rights reserved.
  *
  * This software is licensed under terms that can be found in the LICENSE file
  * in the root directory of this software component.
  * If no LICENSE file comes with this software, it is provided AS-IS.
  *
  ******************************************************************************
  */
/* USER CODE END Header */

/* Define to prevent recursive inclusion -------------------------------------*/
#ifndef __MAIN_H
#define __MAIN_H

#ifdef __cplusplus
extern "C" {
#endif

/* Includes ------------------------------------------------------------------*/
#include "stm32g0xx_hal.h"

/* Private includes ----------------------------------------------------------*/
/* USER CODE BEGIN Includes */
#include "cmsis_os2.h"
/* USER CODE END Includes */

/* Exported types ------------------------------------------------------------*/
/* USER CODE BEGIN ET */

/* USER CODE END ET */

/* Exported constants --------------------------------------------------------*/
/* USER CODE BEGIN EC */

/* USER CODE END EC */

/* Exported macro ------------------------------------------------------------*/
/* USER CODE BEGIN EM */

/* USER CODE END EM */

/* Exported functions prototypes ---------------------------------------------*/
void Error_Handler(void);

/* USER CODE BEGIN EFP */

/* USER CODE END EFP */

/* Private defines -----------------------------------------------------------*/
#define D_INPUT2_Pin GPIO_PIN_13
#define D_INPUT2_GPIO_Port GPIOC
#define D_INPUT1_Pin GPIO_PIN_14
#define D_INPUT1_GPIO_Port GPIOC
#define ID_SET2_Pin GPIO_PIN_15
#define ID_SET2_GPIO_Port GPIOC
#define ID_SET1_Pin GPIO_PIN_0
#define ID_SET1_GPIO_Port GPIOA
#define UART2_DE_Pin GPIO_PIN_1
#define UART2_DE_GPIO_Port GPIOA
#define D_OUTPUT2_Pin GPIO_PIN_4
#define D_OUTPUT2_GPIO_Port GPIOA
#define D_OUTPUT1_Pin GPIO_PIN_5
#define D_OUTPUT1_GPIO_Port GPIOA
#define D_OUTPUT4_Pin GPIO_PIN_6
#define D_OUTPUT4_GPIO_Port GPIOA
#define D_OUTPUT3_Pin GPIO_PIN_7
#define D_OUTPUT3_GPIO_Port GPIOA
#define D_OUTPUT6_Pin GPIO_PIN_0
#define D_OUTPUT6_GPIO_Port GPIOB
#define D_OUTPUT5_Pin GPIO_PIN_1
#define D_OUTPUT5_GPIO_Port GPIOB
#define D_OUTPUT8_Pin GPIO_PIN_2
#define D_OUTPUT8_GPIO_Port GPIOB
#define D_OUTPUT7_Pin GPIO_PIN_10
#define D_OUTPUT7_GPIO_Port GPIOB
#define D_OUTPUT10_Pin GPIO_PIN_11
#define D_OUTPUT10_GPIO_Port GPIOB
#define D_OUTPUT9_Pin GPIO_PIN_12
#define D_OUTPUT9_GPIO_Port GPIOB
#define D_OUTPUT12_Pin GPIO_PIN_13
#define D_OUTPUT12_GPIO_Port GPIOB
#define D_OUTPUT11_Pin GPIO_PIN_14
#define D_OUTPUT11_GPIO_Port GPIOB
#define D_OUTPUT14_Pin GPIO_PIN_15
#define D_OUTPUT14_GPIO_Port GPIOB
#define D_OUTPUT13_Pin GPIO_PIN_8
#define D_OUTPUT13_GPIO_Port GPIOA
#define D_OUTPUT16_Pin GPIO_PIN_9
#define D_OUTPUT16_GPIO_Port GPIOA
#define D_OUTPUT15_Pin GPIO_PIN_6
#define D_OUTPUT15_GPIO_Port GPIOC
#define D_INPUT16_Pin GPIO_PIN_7
#define D_INPUT16_GPIO_Port GPIOC
#define D_INPUT15_Pin GPIO_PIN_10
#define D_INPUT15_GPIO_Port GPIOA
#define D_INPUT14_Pin GPIO_PIN_15
#define D_INPUT14_GPIO_Port GPIOA
#define D_INPUT13_Pin GPIO_PIN_0
#define D_INPUT13_GPIO_Port GPIOD
#define D_INPUT12_Pin GPIO_PIN_1
#define D_INPUT12_GPIO_Port GPIOD
#define D_INPUT11_Pin GPIO_PIN_2
#define D_INPUT11_GPIO_Port GPIOD
#define D_INPUT10_Pin GPIO_PIN_3
#define D_INPUT10_GPIO_Port GPIOD
#define D_INPUT9_Pin GPIO_PIN_3
#define D_INPUT9_GPIO_Port GPIOB
#define D_INPUT8_Pin GPIO_PIN_4
#define D_INPUT8_GPIO_Port GPIOB
#define D_INPUT7_Pin GPIO_PIN_5
#define D_INPUT7_GPIO_Port GPIOB
#define D_INPUT6_Pin GPIO_PIN_6
#define D_INPUT6_GPIO_Port GPIOB
#define D_INPUT5_Pin GPIO_PIN_7
#define D_INPUT5_GPIO_Port GPIOB
#define D_INPUT4_Pin GPIO_PIN_8
#define D_INPUT4_GPIO_Port GPIOB
#define D_INPUT3_Pin GPIO_PIN_9
#define D_INPUT3_GPIO_Port GPIOB

/* USER CODE BEGIN Private defines */

/* USER CODE END Private defines */

#ifdef __cplusplus
}
#endif

#endif /* __MAIN_H */
