//*****************************************************************************/
//* @file : a_modbus_slave.c
//* @author : gjkwon1 (gjkwon1@gst-in.com)*/
//* @brief : Modbus RTU Slave Protocol Task Implementation*/
//* @version : 0.1*/
//* @date : 2025. 12. 23.
//*****************************************************************************/
//* @copyright Copyright (c) 2024 Global Standard Technology Co., Ltd.*/
//*****************************************************************************/
//* All rights reserved.*/
//* This file is part of closed source software.*/
//* Unauthorized reproduction and redistribution prohibited.*/
//*****************************************************************************/

/*****************************************************************************/
/** INCLUDES *****************************************************************/
/*****************************************************************************/
#include "a_modbus_slave.h"
#include "a_dio.h"
#include "a_test_id.h"
#include "m_modbus.h"
#include "m_datablock.h"
#include <string.h>
/*****************************************************************************/
/** MACRO DEFINITIONS ********************************************************/
/*****************************************************************************/
#define UART_RX_BUFFER_SIZE 256
#define SLAVE_ID_STABLE_COUNT 50U
/*****************************************************************************/
/** DATATYPES ****************************************************************/
/*****************************************************************************/

/*****************************************************************************/
/** FUNCTION DECLARATIONS ****************************************************/
/*****************************************************************************/
// DIO functions declared in a_dio.h
/*****************************************************************************/
/** GLOBAL VARIABLES *********************************************************/
/*****************************************************************************/
extern UART_HandleTypeDef huart2;
/*****************************************************************************/
/** LOCAL VARIABLES **********************************************************/
/*****************************************************************************/
uint8_t uart_rx_buffer[ UART_RX_BUFFER_SIZE ];
uint8_t slave_address = 1;
static volatile uint8_t uart_rx_flag = 0;
static volatile uint16_t uart_rx_length = 0;

// 인터럽트에서 복사한 안전한 수신 버퍼
static uint8_t safe_rx_buffer[ UART_RX_BUFFER_SIZE ];
static volatile uint16_t safe_rx_length = 0;

// Modbus 데이터 영역
uint16_t coils[ 16 ] = { 0 };
uint16_t discreteInputs[ 16 ] = { 0 };
uint16_t holdingRegisters[ 2 ] = { 0 };   // [0]=addr 129(DO), [1]=addr 130(DI)
uint16_t inputRegisters[ 16 ] = { 0 };

// Modbus 송수신 버퍼
static uint8_t modbus_tx_buffer[ 256 ];
static Modbus_Rx_st modbus_rx_frame;

// Modbus Slave용 DataBlock
static DataBlock_st modbus_datablock = { .DInputs = discreteInputs,     .DInputsCnt = 16,     .DInputsStart = 0,
                                         .Coils = coils,                .CoilsCnt = 16,       .CoilsStart = 0,
                                         .Inputs = inputRegisters,      .InputsCnt = 1,     .InputsStart = 130,
                                         .Holdings = holdingRegisters,  .HoldingsCnt = 2,      .HoldingsStart = 129,   // Modbus address 129~130 → array index 0~1
    };
/*****************************************************************************/
/** FUNCTION DEFINITIONS *****************************************************/
/*****************************************************************************/

/******************************************************************************
 * @brief UART Idle Interrupt Callback
 * @param Size: DMA 시작 이후 전송된 총 바이트 수 (circular mode에서는 현재 위치)
 * @note 인터럽트 콘텍스트에서 즉시 데이터를 안전한 버퍼로 복사
 *****************************************************************************/
void HAL_UARTEx_RxEventCallback( UART_HandleTypeDef *huart, uint16_t Size )
{
  if( huart->Instance == huart2.Instance )
  {
    if( Size > 0 && uart_rx_flag == RESET )
    {
      // 이전 프레임이 아직 처리되지 않은 경우에만 복사 (덮어쓰기 방지)
      memcpy( uart_rx_buffer, safe_rx_buffer, Size );
      uart_rx_length = Size;
      uart_rx_flag = SET;
    }

    // 다음 수신을 위해 DMA 재시작
    HAL_UARTEx_ReceiveToIdle_DMA( &huart2, safe_rx_buffer, UART_RX_BUFFER_SIZE );
    __HAL_DMA_DISABLE_IT( huart2.hdmarx, DMA_IT_HT );
  }
}

void HAL_UART_ErrorCallback( UART_HandleTypeDef *huart )
{
  if( huart->Instance == huart2.Instance )
  {
    safe_rx_length = 0;
    uart_rx_length = 0;
    uart_rx_flag = RESET;

    HAL_UARTEx_ReceiveToIdle_DMA( &huart2, safe_rx_buffer, UART_RX_BUFFER_SIZE );
    __HAL_DMA_DISABLE_IT( huart2.hdmarx, DMA_IT_HT );
  }
}

/******************************************************************************
 * @brief UART 초기화
 *****************************************************************************/
void UartRxInit( void )
{
  // IDLE 인터럽트를 사용한 DMA 수신 시작
  //  HAL_UARTEx_ReceiveToIdle_DMA( &huart2, uart_rx_buffer, UART_RX_BUFFER_SIZE );
//  __HAL_UART_DISABLE(&huart2);
//  huart2.Instance->RTOR = 35;                     // 35 bit times = 3.5 char
//  SET_BIT(huart2.Instance->CR2, USART_CR2_RTOEN); // RTO enable
//  __HAL_UART_ENABLE(&huart2);
//__HAL_UART_ENABLE_IT(&huart2, UART_IT_RTO);      // RTO 인터럽트 enable

  HAL_UARTEx_ReceiveToIdle_DMA( &huart2, safe_rx_buffer, UART_RX_BUFFER_SIZE );
  __HAL_DMA_DISABLE_IT( huart2.hdmarx, DMA_IT_HT );
  __HAL_UART_ENABLE_IT( &huart2, UART_IT_IDLE );
}

uint8_t SlaveIDCheck( void )
{
  return TestId_ReadValue();
}

/******************************************************************************
 * @brief ModbusSlaveTask - Modbus RTU Slave 프로토콜 처리
 *****************************************************************************/

// ID 스위치 디바운싱 처리 //
static void SlaveIDUpdate( void )
{
  static uint8_t last_raw = 0U;
  static uint16_t stable_count = 0U;
  const uint8_t raw = SlaveIDCheck();

  if( raw != last_raw )
  {
    last_raw = raw;
    stable_count = 0U;
    return;
  }

  if( stable_count < SLAVE_ID_STABLE_COUNT )
  {
    stable_count++;
    if( stable_count >= SLAVE_ID_STABLE_COUNT )
    {
      slave_address = raw;
    }
  }
}

void ModbusSlaveTask( void *argument )
{
  UartRxInit();
  DIO_Modbus_Init();
  HAL_GPIO_WritePin( UART2_DE_GPIO_Port, UART2_DE_Pin, GPIO_PIN_SET );
  osDelay( 1 );
  HAL_GPIO_WritePin( UART2_DE_GPIO_Port, UART2_DE_Pin, GPIO_PIN_RESET );

  // 초기 Slave ID 적용 /
  osDelay( 20 );
  slave_address = SlaveIDCheck();

  while( 1 )
  {
    SlaveIDUpdate();
    // DIO 입력 상태 주기적으로 읽기
    DIO_Modbus_ReadInputs();

    // UART 수신 데이터 확인
    if( uart_rx_flag )
    {
      // length를 먼저 읽고 flag를 클리어: flag가 SET인 동안에는 ISR이 uart_rx_length를
      // 덮어쓰지 않으므로 안전하게 스냅샷 가능 (레이스 컨디션 방지)
      uint16_t data_length = uart_rx_length;
      uart_rx_flag = RESET;

      // 매 프레임마다 초기화: CRC 실패 시 Unit_ID가 이전 값으로 남아
      // 오동작하는 것을 방지
      memset( &modbus_rx_frame, 0, sizeof( modbus_rx_frame ) );

      // Modbus RTU 프레임 체크 (frame_format: 1=RTU, 2=ASCII, 3=TCP)
      Modbus_ExCode_et ex_code = MbSlaveCheckRequestFrame( 'R', // RTU mode
          uart_rx_buffer, data_length, &modbus_datablock, &modbus_rx_frame );

      // 슬레이브 주소가 일치하는 경우에만 응답
      // CRC 오류 시 Unit_ID = 0 (memset) → 주소 불일치 → 무응답 (스펙 준수)
      if( modbus_rx_frame.MBAP_Header.Unit_ID == slave_address )
      {
        uint16_t tx_length = 0;

        if( ex_code != Modbus_ExCode_Normal )
        {
          // CRC 이후 단계 오류 (주소 범위, 수량, 미지원 FC) → Exception 응답
          tx_length = modbus_exception_response( 'R', &modbus_rx_frame, modbus_tx_buffer, ex_code );
        }
        else
        {
          // Function code에 따른 응답 생성
          switch( modbus_rx_frame.function_code )
          {
            case Modbus_FunCode_Read_Holding_Reg:
              tx_length = modbus_slave_resp_read_holding_reg( 'R', &modbus_rx_frame, &modbus_datablock, modbus_tx_buffer );
              break;

            case Modbus_FunCode_Read_Input_Reg:
              tx_length = modbus_slave_resp_read_input_reg( 'R', &modbus_rx_frame, &modbus_datablock, modbus_tx_buffer );
              break;

            case Modbus_FunCode_Write_Single_Reg:
              tx_length = modbus_slave_resp_preset_holding_reg( 'R', &modbus_rx_frame, &modbus_datablock, modbus_tx_buffer );
              // Write 명령 후 즉시 GPIO 출력 업데이트
              DIO_Modbus_UpdateOutputs( modbus_rx_frame.starting_address_write );
              break;

            case Modbus_FunCode_Write_Multiple_Reg:
              tx_length = modbus_slave_resp_preset_multiple_reg( 'R', &modbus_rx_frame, &modbus_datablock, modbus_tx_buffer );
              // Write 명령 후 즉시 GPIO 출력 업데이트
              DIO_Modbus_UpdateOutputs( modbus_rx_frame.starting_address_write );
              break;

            default:
              tx_length = modbus_exception_response( 'R', &modbus_rx_frame, modbus_tx_buffer, Modbus_ExCode_Illegal_FunCode );
              break;
          }
        }

        // 응답 전송
        // vPortEnterCritical 제거: M0+에서는 __disable_irq()와 동일하여 SysTick을
        // 차단하므로 HAL_UART_Transmit의 타임아웃이 무한대가 됨.
        // ModbusSlaveTask 단독 접근이므로 critical section 불필요.
        if( tx_length > 0 )
        {
          HAL_GPIO_WritePin( UART2_DE_GPIO_Port, UART2_DE_Pin, GPIO_PIN_SET );
          HAL_UART_Transmit( &huart2, modbus_tx_buffer, tx_length, 50 );
          HAL_GPIO_WritePin( UART2_DE_GPIO_Port, UART2_DE_Pin, GPIO_PIN_RESET );
        }
      }
    }
    osDelay( 1 );
  }
}
