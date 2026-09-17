#include "boot_flash.h"
#include "bootloader_shared.h"
#include "main.h"

/* 바이트 주소를 "이 칩(STM32L452, 단일뱅크 512KB, 2KB 페이지)" 기준
 * 페이지 번호로 변환한다. HAL_FLASHEx_Erase()는 절대 주소가 아니라
 * "몇 번째 페이지인지"를 요구하기 때문에 필요한 변환이다. */
static uint32_t PageNumberFromAddress( uint32_t address )
{
  return ( address - BL_FLASH_BASE ) / FLASH_PAGE_SIZE;
}

bool BootFlash_EraseRange( uint32_t addr, uint32_t size )
{
  FLASH_EraseInitTypeDef erase = { 0 };
  uint32_t page_error = 0U;
  uint32_t page_count;
  bool ok;

  /* size바이트를 지우려면 몇 개의 2KB 페이지가 필요한지 올림 계산.
   * 예: size=1바이트여도 페이지 1개는 통째로 지워야 하므로 +FLASH_PAGE_SIZE-1
   * 을 더한 뒤 나누는 "올림 나눗셈" 공식을 사용한다. */
  page_count = ( size + FLASH_PAGE_SIZE - 1U ) / FLASH_PAGE_SIZE;

  ( void )HAL_FLASH_Unlock();

  erase.TypeErase = FLASH_TYPEERASE_PAGES;
  erase.Banks     = FLASH_BANK_1;
  erase.Page      = PageNumberFromAddress( addr );
  erase.NbPages   = page_count;

  /* HAL_FLASHEx_Erase는 내부적으로 페이지 하나하나를 순서대로 지우면서
   * 완료될 때까지 기다리는(blocking) 함수다. 이 시간 동안 CPU는 flash에서
   * 명령을 못 읽어오므로(단일뱅크 read-while-write 제약), 사실상 인터럽트도
   * 응답이 늦어질 수 있다 - 부트로더는 실시간 처리를 하지 않으므로 문제 없다. */
  ok = ( HAL_FLASHEx_Erase( &erase, &page_error ) == HAL_OK );

  ( void )HAL_FLASH_Lock();
  BootFlash_FlushCaches();
  return ok;
}

bool BootFlash_Write( uint32_t addr, const uint8_t *data, uint32_t len )
{
  /* 마지막 double-word(8바이트)를 채우기 위한 임시 패딩 버퍼.
   * len이 8의 배수가 아닐 때만 사용된다(그 외에는 data를 바로 읽는다). */
  uint8_t padded[ 8 ];
  uint32_t full_doublewords = len / 8U;
  uint32_t remainder = len % 8U;
  uint32_t i;
  bool ok = true;

  /* addr이 8의 배수가 아니면 애초에 double-word 프로그래밍이 불가능하다.
   * 상위 프로토콜(boot_protocol.c)에서 offset을 항상 8의 배수로만 보내도록
   * 강제하고 있으므로 정상 동작에서는 여기 걸리지 않는다 - 방어적 체크. */
  if( ( addr % 8U ) != 0U )
  {
    return false;
  }

  ( void )HAL_FLASH_Unlock();

  /* 1) 8바이트 단위로 딱 떨어지는 부분을 먼저 그대로 쓴다. */
  for( i = 0U; ( i < full_doublewords ) && ok; i++ )
  {
    uint64_t value;

    /* data는 1바이트 정렬이라 uint64_t*로 그냥 캐스팅해서 읽으면 얼라인먼트
     * 오류가 날 수 있으므로, memcpy 대신 바이트 단위로 안전하게 조립한다. */
    ( void )__builtin_memcpy( &value, &data[ i * 8U ], 8U );

    if( HAL_FLASH_Program( FLASH_TYPEPROGRAM_DOUBLEWORD, addr + ( i * 8U ), value ) != HAL_OK )
    {
      ok = false;
    }
  }

  /* 2) 8의 배수로 안 떨어지고 남은 자투리(remainder)가 있으면,
   *    남는 자리를 0xFF(소거 상태와 동일한 값)로 채워서 마지막 double-word
   *    하나를 추가로 써준다. 이 경우는 보통 "이미지의 맨 마지막 청크"에서만
   *    발생한다(그 뒤에 이어붙일 데이터가 없다는 게 보장된 상황). */
  if( ok && ( remainder != 0U ) )
  {
    uint64_t value;

    for( i = 0U; i < 8U; i++ )
    {
      padded[ i ] = ( i < remainder ) ? data[ full_doublewords * 8U + i ] : 0xFFU;
    }
    ( void )__builtin_memcpy( &value, padded, 8U );

    if( HAL_FLASH_Program( FLASH_TYPEPROGRAM_DOUBLEWORD, addr + ( full_doublewords * 8U ), value ) != HAL_OK )
    {
      ok = false;
    }
  }

  ( void )HAL_FLASH_Lock();
  BootFlash_FlushCaches();
  return ok;
}

void BootFlash_FlushCaches( void )
{
  /* 리셋하기 전에 반드시 캐시를 비활성화해야 한다("This function must be
   * used only when the Instruction/Data Cache is disabled" - HAL 헤더 주석). */
  __HAL_FLASH_INSTRUCTION_CACHE_DISABLE();
  __HAL_FLASH_DATA_CACHE_DISABLE();

  __HAL_FLASH_INSTRUCTION_CACHE_RESET();
  __HAL_FLASH_DATA_CACHE_RESET();

  __HAL_FLASH_INSTRUCTION_CACHE_ENABLE();
  __HAL_FLASH_DATA_CACHE_ENABLE();
}
