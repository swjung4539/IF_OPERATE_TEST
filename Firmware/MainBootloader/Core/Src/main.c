/* USER CODE BEGIN Header */
/**
  ******************************************************************************
  * @file           : main.c
  * @brief          : Main program body
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
/* Includes ------------------------------------------------------------------*/
#include "main.h"
#include "usb_device.h"

/* Private includes ----------------------------------------------------------*/
/* USER CODE BEGIN Includes */
#include "bootloader_shared.h"
#include "boot_flag.h"
#include "boot_meta.h"
#include "boot_jump.h"
#include "boot_protocol.h"
/* USER CODE END Includes */

/* Private typedef -----------------------------------------------------------*/
/* USER CODE BEGIN PTD */

/* USER CODE END PTD */

/* Private define ------------------------------------------------------------*/
/* USER CODE BEGIN PD */

/* USER CODE END PD */

/* Private macro -------------------------------------------------------------*/
/* USER CODE BEGIN PM */

/* USER CODE END PM */

/* Private variables ---------------------------------------------------------*/

/* USER CODE BEGIN PV */

/* USER CODE END PV */

/* Private function prototypes -----------------------------------------------*/
void SystemClock_Config(void);
static void MX_GPIO_Init(void);
/* USER CODE BEGIN PFP */

/* USER CODE END PFP */

/* Private user code ---------------------------------------------------------*/
/* USER CODE BEGIN 0 */

/* USER CODE END 0 */

/**
  * @brief  The application entry point.
  * @retval int
  */
int main(void)
{

  /* USER CODE BEGIN 1 */

  /* USER CODE END 1 */

  /* MCU Configuration--------------------------------------------------------*/

  /* Reset of all peripherals, Initializes the Flash interface and the Systick. */
  HAL_Init();

  /* USER CODE BEGIN Init */

  /* USER CODE END Init */

  /* Configure the system clock */
  SystemClock_Config();

  /* USER CODE BEGIN SysInit */

  /* USER CODE END SysInit */

  /* Initialize all configured peripherals */
  MX_GPIO_Init();
  /* NOTE: MX_USB_DEVICE_Init()는 여기서 무조건 호출하지 않는다 - 아래 USER CODE 2에서
   * 업데이트 모드로 상주할 때만 조건부로 호출한다(평상시 부팅 지연 최소화).
   * CubeMX에서 코드를 재생성하면 이 줄이 자동으로 복원될 수 있으니 다시 지워야 한다. */
  /* USER CODE BEGIN 2 */
  {
    BlBootMetadata_t meta = BootMeta_Load();
    BlSlotId_t boot_slot = meta.active_slot;
    bool stay_resident = false;

    if( meta.state == BL_STATE_PENDING_TEST )
    {
      if( meta.retry_count == 0U )
      {
        /* PENDING_TEST 슬롯을 부팅하는 첫 시도다 - 아직 이 슬롯이 실패했다고
         * 볼 근거가 없으므로 롤백하지 않고 그대로 한 번 실행해본다.
         * retry_count만 1로 올려서 "이미 한 번 시도했다"는 것을 기록해두면,
         * 만약 이번 시도가 Bootloader_ConfirmBootOk()를 호출하지 못하고
         * 다시 리셋되어 여기로 돌아왔을 때(크래시/IWDG) 그때는 진짜로
         * 실패한 것으로 보고 롤백할 수 있다. */
        meta.retry_count = 1U;
        ( void )BootMeta_Save( &meta );
      }
      else
      {
        /* 이미 한 번 시도했는데도 여전히 CONFIRMED가 안 된 채 재부팅됐다
         * -> 새 펌웨어가 멈추거나 크래시한 것으로 판단하고 이전 슬롯으로 롤백 */
        boot_slot = ( meta.active_slot == BL_SLOT_A ) ? BL_SLOT_B : BL_SLOT_A;
        ( void )BootMeta_SetConfirmed( boot_slot );
      }
    }

    if( BootFlag_IsEnterUpdateRequested() )
    {
      BootFlag_Clear();
      stay_resident = true;
    }

    /* 대상 슬롯에 유효한 이미지가 없으면(예: 부트로더만 최초로 플래싱되고
     * 앱은 아직 한 번도 올라간 적 없는 공장 출하 상태) 점프를 시도하지 않고
     * 업데이트 대기 모드로 남는다. Bootloader_JumpToSlot()은 유효하지 않은
     * 슬롯으로 호출되면 안전을 위해 Error_Handler()에서 멈춰버리므로(USB도
     * 켜지지 않음), 반드시 이 사전 확인을 거쳐야 한다. */
    if( !stay_resident && !Bootloader_SlotHasValidImage( boot_slot ) )
    {
      stay_resident = true;
    }

    if( !stay_resident )
    {
      /* 점프하는 케이스는 켜자마자 곧바로 앱이 자기 GPIO를 다시 초기화하며
       * 스스로 켤 것이므로 여기서는 잠깐 켜주기만 한다(하드폴트 없이 정상
       * 판단을 마쳤다는 짧은 신호). */
      HAL_GPIO_WritePin( MODULE_RUN_GPIO_Port, MODULE_RUN_Pin, GPIO_PIN_SET );
      Bootloader_JumpToSlot( boot_slot );
      /* 정상 이미지이므로 위에서 리턴하지 않는다. */
    }
    /* USB 업데이트 대기 모드로 남는 경우는 while(1) 루프에서 초록 LED를
     * 점멸시켜, "앱이 정상 실행 중"(항상 켜짐)과 "부트로더가 업데이트를
     * 기다리는 중"(점멸)을 눈으로 구분할 수 있게 한다. */

    MX_USB_DEVICE_Init();

    /* 프레임 파서/쓰기 상태를 초기화한다. 이 지점부터는 while(1) 루프에서
     * BootProtocol_Process()가 반복 호출되며 PC GUI의 명령을 처리한다. */
    BootProtocol_Init();
  }
  /* USER CODE END 2 */

  /* Infinite loop */
  /* USER CODE BEGIN WHILE */
  uint32_t last_blink_ms = HAL_GetTick();

  while (1)
  {
    uint32_t now_ms = HAL_GetTick();

    /* stay_resident == true인 경우에만 여기 도달한다(그렇지 않으면 위에서
     * Bootloader_JumpToSlot()이 이미 다른 슬롯의 코드로 실행 흐름을 넘겨서
     * 이 while문 자체를 다시 돌아오지 않는다).
     * 500ms마다 MODULE_RUN(초록) LED를 토글해서 "부트로더가 정상적으로
     * 살아서 업데이트를 기다리는 중"임을 점멸로 나타낸다. */
    if( ( uint32_t )( now_ms - last_blink_ms ) >= 500U )
    {
      HAL_GPIO_TogglePin( MODULE_RUN_GPIO_Port, MODULE_RUN_Pin );
      last_blink_ms = now_ms;
    }

    /* 매 iteration마다 USB로 들어온 바이트를 확인해서, 완전한 프레임이
     * 도착했으면 즉시 처리한다(ERASE_SLOT/WRITE_CHUNK/FINALIZE/REBOOT 등). */
    BootProtocol_Process();
    /* USER CODE END WHILE */

    /* USER CODE BEGIN 3 */
  }
  /* USER CODE END 3 */
}

/**
  * @brief System Clock Configuration
  * @retval None
  */
void SystemClock_Config(void)
{
  RCC_OscInitTypeDef RCC_OscInitStruct = {0};
  RCC_ClkInitTypeDef RCC_ClkInitStruct = {0};

  /** Configure the main internal regulator output voltage
  */
  if (HAL_PWREx_ControlVoltageScaling(PWR_REGULATOR_VOLTAGE_SCALE1) != HAL_OK)
  {
    Error_Handler();
  }

  /** Initializes the RCC Oscillators according to the specified parameters
  * in the RCC_OscInitTypeDef structure.
  */
  RCC_OscInitStruct.OscillatorType = RCC_OSCILLATORTYPE_HSI48|RCC_OSCILLATORTYPE_MSI;
  RCC_OscInitStruct.HSI48State = RCC_HSI48_ON;
  RCC_OscInitStruct.MSIState = RCC_MSI_ON;
  RCC_OscInitStruct.MSICalibrationValue = 0;
  RCC_OscInitStruct.MSIClockRange = RCC_MSIRANGE_6;
  RCC_OscInitStruct.PLL.PLLState = RCC_PLL_ON;
  RCC_OscInitStruct.PLL.PLLSource = RCC_PLLSOURCE_MSI;
  RCC_OscInitStruct.PLL.PLLM = 1;
  RCC_OscInitStruct.PLL.PLLN = 40;
  RCC_OscInitStruct.PLL.PLLP = RCC_PLLP_DIV7;
  RCC_OscInitStruct.PLL.PLLQ = RCC_PLLQ_DIV2;
  RCC_OscInitStruct.PLL.PLLR = RCC_PLLR_DIV2;
  if (HAL_RCC_OscConfig(&RCC_OscInitStruct) != HAL_OK)
  {
    Error_Handler();
  }

  /** Initializes the CPU, AHB and APB buses clocks
  */
  RCC_ClkInitStruct.ClockType = RCC_CLOCKTYPE_HCLK|RCC_CLOCKTYPE_SYSCLK
                              |RCC_CLOCKTYPE_PCLK1|RCC_CLOCKTYPE_PCLK2;
  RCC_ClkInitStruct.SYSCLKSource = RCC_SYSCLKSOURCE_PLLCLK;
  RCC_ClkInitStruct.AHBCLKDivider = RCC_SYSCLK_DIV1;
  RCC_ClkInitStruct.APB1CLKDivider = RCC_HCLK_DIV1;
  RCC_ClkInitStruct.APB2CLKDivider = RCC_HCLK_DIV1;

  if (HAL_RCC_ClockConfig(&RCC_ClkInitStruct, FLASH_LATENCY_4) != HAL_OK)
  {
    Error_Handler();
  }
}

/**
  * @brief GPIO Initialization Function
  * @param None
  * @retval None
  */
static void MX_GPIO_Init(void)
{
  /* USER CODE BEGIN MX_GPIO_Init_1 */

  /* USER CODE END MX_GPIO_Init_1 */

  /* GPIO Ports Clock Enable */
  __HAL_RCC_GPIOH_CLK_ENABLE();
  __HAL_RCC_GPIOA_CLK_ENABLE();
  __HAL_RCC_GPIOB_CLK_ENABLE();

  /* USER CODE BEGIN MX_GPIO_Init_2 */
  {
    GPIO_InitTypeDef led_init = { 0 };

    /* 두 핀 다 처음엔 꺼진 상태(LOW)로 시작 - 정상 부팅 판단이 끝나면
     * MODULE_RUN(초록)을 켠다. MODULE_ERROR(빨강)는 HardFault_Handler에서만 켠다. */
    HAL_GPIO_WritePin( GPIOB, MODULE_ERROR_Pin | MODULE_RUN_Pin, GPIO_PIN_RESET );

    led_init.Pin = MODULE_ERROR_Pin | MODULE_RUN_Pin;
    led_init.Mode = GPIO_MODE_OUTPUT_PP;
    led_init.Pull = GPIO_NOPULL;
    led_init.Speed = GPIO_SPEED_FREQ_LOW;
    HAL_GPIO_Init( GPIOB, &led_init );
  }
  /* USER CODE END MX_GPIO_Init_2 */
}

/* USER CODE BEGIN 4 */

/* USER CODE END 4 */

/**
  * @brief  Period elapsed callback in non blocking mode
  * @note   This function is called  when TIM6 interrupt took place, inside
  * HAL_TIM_IRQHandler(). It makes a direct call to HAL_IncTick() to increment
  * a global variable "uwTick" used as application time base.
  * @param  htim : TIM handle
  * @retval None
  */
void HAL_TIM_PeriodElapsedCallback(TIM_HandleTypeDef *htim)
{
  /* USER CODE BEGIN Callback 0 */

  /* USER CODE END Callback 0 */
  if (htim->Instance == TIM6)
  {
    HAL_IncTick();
  }
  /* USER CODE BEGIN Callback 1 */

  /* USER CODE END Callback 1 */
}

/**
  * @brief  This function is executed in case of error occurrence.
  * @retval None
  */
void Error_Handler(void)
{
  /* USER CODE BEGIN Error_Handler_Debug */
  /* User can add his own implementation to report the HAL error return state */
  __disable_irq();
  while (1)
  {
  }
  /* USER CODE END Error_Handler_Debug */
}
#ifdef USE_FULL_ASSERT
/**
  * @brief  Reports the name of the source file and the source line number
  *         where the assert_param error has occurred.
  * @param  file: pointer to the source file name
  * @param  line: assert_param error line source number
  * @retval None
  */
void assert_failed(uint8_t *file, uint32_t line)
{
  /* USER CODE BEGIN 6 */
  /* User can add his own implementation to report the file name and line number,
     ex: printf("Wrong parameters value: file %s on line %d\r\n", file, line) */
  /* USER CODE END 6 */
}
#endif /* USE_FULL_ASSERT */
