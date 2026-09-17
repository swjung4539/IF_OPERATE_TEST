#include "boot_jump.h"
#include "boot_flash.h"
#include "main.h"

extern TIM_HandleTypeDef htim6;

typedef void ( *ResetHandler_t )( void );

static uint32_t SlotBaseAddress( BlSlotId_t slot )
{
  return ( slot == BL_SLOT_A ) ? BL_SLOT_A_BASE : BL_SLOT_B_BASE;
}

/* 슬롯이 비어있으면(공장 출하 상태, Flash가 0xFF로 소거된 상태) 첫 4바이트인
 * 초기 스택포인터 값이 0xFFFFFFFF로 읽히고, 이는 유효한 RAM 주소(0x20000000~
 * 0x20020000)가 아니다. 이 검사로 "점프해도 안전한 이미지인지"를 미리 판정한다. */
static bool IsValidImage( uint32_t app_base, uint32_t sp, uint32_t reset_handler_addr )
{
  return ( sp >= 0x20000000UL ) && ( sp <= 0x20020000UL ) && ( reset_handler_addr >= app_base );
}

bool Bootloader_SlotHasValidImage( BlSlotId_t slot )
{
  uint32_t app_base = SlotBaseAddress( slot );
  uint32_t sp = *( volatile uint32_t * )( app_base + 0U );
  uint32_t reset_handler_addr = *( volatile uint32_t * )( app_base + 4U );

  return IsValidImage( app_base, sp, reset_handler_addr );
}

void Bootloader_JumpToSlot( BlSlotId_t slot )
{
  uint32_t app_base = SlotBaseAddress( slot );
  uint32_t sp = *( volatile uint32_t * )( app_base + 0U );
  uint32_t reset_handler_addr = *( volatile uint32_t * )( app_base + 4U );
  ResetHandler_t app_reset_handler;

  /* 정상적으로는 호출자가 Bootloader_SlotHasValidImage()로 미리 확인하고 나서만
   * 이 함수를 부르므로 여기서 실패하는 일이 없어야 한다. 그래도 방어적으로
   * 한 번 더 확인해서, 혹시라도 잘못된 슬롯으로 호출되는 경우 무의미한 주소로
   * 점프해 완전히 알 수 없는 상태(진짜 브릭)에 빠지는 것만은 막는다. */
  if( !IsValidImage( app_base, sp, reset_handler_addr ) )
  {
    Error_Handler();
    return;
  }

  __disable_irq();

  /* 타임베이스(TIM6)와 USB 등 사용 중이던 페리페럴 정지 */
  ( void )HAL_TIM_Base_Stop_IT( &htim6 );
  HAL_NVIC_DisableIRQ( TIM6_DAC_IRQn );

  /* TIM6 하나만 끄는 걸로는 부족하다 - 부트로더가 USB 대기 모드로 상주하면서
   * 켜뒀을 수 있는 USB 등 다른 인터럽트가 NVIC에 여전히 활성화+대기(pending)
   * 상태로 남아있을 수 있다. 이 상태로 앱에 넘어가서 인터럽트가 다시
   * 켜지면(원래는 여기서 __enable_irq()를 불렀었다), 앱이 자기 페리페럴
   * 핸들을 초기화하기도 전에 그 인터럽트가 발생해 초기화 안 된(0으로 비워진)
   * 구조체를 건드리다가 HardFault가 나는 간헐적 버그가 있었다.
   * NVIC의 모든 인터럽트를 확실히 끄고 대기 플래그도 지워서 앱이 완전히
   * 깨끗한 상태에서 시작하게 한다. */
  {
    uint32_t i;
    for( i = 0U; i < ( sizeof( NVIC->ICER ) / sizeof( NVIC->ICER[ 0 ] ) ); i++ )
    {
      NVIC->ICER[ i ] = 0xFFFFFFFFUL;
      NVIC->ICPR[ i ] = 0xFFFFFFFFUL;
    }
  }

  /* 마지막 안전장치: 이 슬롯의 Flash를 쓴 쪽(WRITE_CHUNK/FINALIZE)에서
   * 이미 캐시를 비워줬어야 하지만, 점프 직전에 한 번 더 확실히 비워서
   * 앱이 자기 자신의 .data 초기화 등에서 오래된 캐시 값을 읽는 사고를
   * 원천 차단한다. */
  BootFlash_FlushCaches();

  HAL_RCC_DeInit();
  HAL_DeInit();

  SysTick->CTRL = 0U;
  SysTick->LOAD = 0U;
  SysTick->VAL  = 0U;

  SCB->VTOR = app_base;

  __set_MSP( sp );

  app_reset_handler = ( ResetHandler_t )reset_handler_addr;

  /* 실제 MCU 리셋(전원 인가/NRST) 직후에는 PRIMASK가 0(인터럽트 전역 활성)
   * 상태다. 앱의 초기화 코드(HAL_Init, HAL_Delay 등)는 타이머 인터럽트로
   * 틱이 올라가는 것에 의존하므로, 인터럽트가 전역적으로 꺼진 채로 넘기면
   * HAL_Delay 등에서 멈춰버릴 수 있다. 위에서 NVIC의 개별 인터럽트는 이미
   * 모두 끄고 대기 플래그도 지웠으므로(그래서 앱이 미처 초기화 안 된
   * 페리페럴 핸들로 인터럽트를 받는 사고는 안 난다), 여기서는 전역
   * 인터럽트 자체는 정상적으로 다시 켜서 리셋 직후와 동일한 조건을 만든다. */
  __enable_irq();
  app_reset_handler();

  /* 여기 도달하면 안 됨 */
  for( ;; )
  {
  }
}
