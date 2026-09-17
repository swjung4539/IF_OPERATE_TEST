/* USER CODE BEGIN Header */
/**
  ******************************************************************************
  * @file           : main.h
  * @brief          : Header for main.c file.
  *                   This file contains the common defines of the application.
  ******************************************************************************
  * @attention
  *
  * Copyright (c) 2026 STMicroelectronics.
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
#include "stm32l4xx_hal.h"

/* Private includes ----------------------------------------------------------*/
/* USER CODE BEGIN Includes */

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
#define FAULT_Pin GPIO_PIN_2
#define FAULT_GPIO_Port GPIOE
#define CAN_ID3_Pin GPIO_PIN_3
#define CAN_ID3_GPIO_Port GPIOE
#define FORCE_ON_Pin GPIO_PIN_13
#define FORCE_ON_GPIO_Port GPIOC
#define FORCE_EN_Pin GPIO_PIN_14
#define FORCE_EN_GPIO_Port GPIOC
#define CAN_ID4_Pin GPIO_PIN_0
#define CAN_ID4_GPIO_Port GPIOC
#define CAN_ID5_Pin GPIO_PIN_1
#define CAN_ID5_GPIO_Port GPIOC
#define CAN_ID6_Pin GPIO_PIN_2
#define CAN_ID6_GPIO_Port GPIOC
#define CAN_ID7_Pin GPIO_PIN_3
#define CAN_ID7_GPIO_Port GPIOC
#define UART4_DE_Pin GPIO_PIN_2
#define UART4_DE_GPIO_Port GPIOA
#define UART4_RE_Pin GPIO_PIN_3
#define UART4_RE_GPIO_Port GPIOA
#define USART3_DE_Pin GPIO_PIN_1
#define USART3_DE_GPIO_Port GPIOB
#define LPUART1_DE_Pin GPIO_PIN_12
#define LPUART1_DE_GPIO_Port GPIOB
#define USART1_DE_Pin GPIO_PIN_8
#define USART1_DE_GPIO_Port GPIOA
#define MEM_CS3_Pin GPIO_PIN_15
#define MEM_CS3_GPIO_Port GPIOA
#define MEM_SCK3_Pin GPIO_PIN_10
#define MEM_SCK3_GPIO_Port GPIOC
#define MEM_MISO3_Pin GPIO_PIN_11
#define MEM_MISO3_GPIO_Port GPIOC
#define MEM_MOSI3_Pin GPIO_PIN_12
#define MEM_MOSI3_GPIO_Port GPIOC
#define USART2_DE_Pin GPIO_PIN_4
#define USART2_DE_GPIO_Port GPIOD
#define MODULE_ERROR_Pin GPIO_PIN_4
#define MODULE_ERROR_GPIO_Port GPIOB
#define MODULE_RUN_Pin GPIO_PIN_5
#define MODULE_RUN_GPIO_Port GPIOB
#define CAN_ERROR_Pin GPIO_PIN_6
#define CAN_ERROR_GPIO_Port GPIOB
#define CAN_RUN_Pin GPIO_PIN_7
#define CAN_RUN_GPIO_Port GPIOB
#define CAN_ID0_Pin GPIO_PIN_0
#define CAN_ID0_GPIO_Port GPIOE
#define CAN_ID1_Pin GPIO_PIN_1
#define CAN_ID1_GPIO_Port GPIOE

/* USER CODE BEGIN Private defines */

/* USER CODE END Private defines */

#ifdef __cplusplus
}
#endif

#endif /* __MAIN_H */
