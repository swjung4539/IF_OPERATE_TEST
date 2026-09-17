#include "a_bootloader_link.h"
#include "bootloader_shared.h"
#include "main.h"
#include <string.h>
#include <stddef.h>

/* 부트 메타데이터는 ping-pong 방식으로 2개의 2KB 페이지에 번갈아 저장된다
 * (Bootloader 프로젝트의 boot_meta.c와 완전히 동일한 레이아웃/알고리즘 -
 *  둘 중 하나가 손상되거나 쓰다가 정전이 나도, 반대쪽 페이지의 이전 값이
 *  그대로 남아있으므로 항상 유효한 메타데이터 하나는 확보된다). */
#define META_PAGE_COUNT   (2U)

static const uint32_t s_page_base[ META_PAGE_COUNT ] = { BL_META_PAGE0_BASE, BL_META_PAGE1_BASE };

static uint32_t PageNumberFromAddress( uint32_t address )
{
  return ( address - BL_FLASH_BASE ) / FLASH_PAGE_SIZE;
}

/* 페이지 하나를 읽어서 magic과 자체 CRC32가 모두 맞으면 유효한 것으로 본다.
 * (부트로더 쪽 boot_meta.c의 ReadPage()와 완전히 동일한 검증 규칙 - 두
 *  프로젝트가 서로 다른 규칙을 쓰면 서로 쓴 걸 서로 못 알아보게 된다.) */
static bool ReadPage( uint32_t page_base, BlBootMetadata_t *out )
{
  uint32_t computed;

  ( void )memcpy( out, ( const void * )page_base, sizeof( BlBootMetadata_t ) );

  if( out->magic != BL_META_MAGIC )
  {
    return false;
  }

  computed = BootShared_Crc32( ( const uint8_t * )out, ( uint32_t )offsetof( BlBootMetadata_t, crc32 ) );
  return out->crc32 == computed;
}

bool Bootloader_ConfirmBootOk( void )
{
  BlBootMetadata_t candidates[ META_PAGE_COUNT ];
  bool valid[ META_PAGE_COUNT ];
  int32_t current_page = -1;
  uint32_t i;
  BlBootMetadata_t meta;
  uint32_t target_page;
  uint32_t target_base;
  FLASH_EraseInitTypeDef erase = { 0 };
  uint32_t page_error = 0U;
  bool ok;

  /* 두 페이지 중 sequence(증가 카운터)가 더 큰, 즉 "가장 최근에 쓰인" 유효한
   * 쪽을 현재 메타데이터로 취급한다. */
  for( i = 0U; i < META_PAGE_COUNT; i++ )
  {
    valid[ i ] = ReadPage( s_page_base[ i ], &candidates[ i ] );
    if( valid[ i ] && ( ( current_page < 0 ) || ( candidates[ i ].sequence > candidates[ ( uint32_t )current_page ].sequence ) ) )
    {
      current_page = ( int32_t )i;
    }
  }

  if( current_page < 0 )
  {
    /* 유효한 메타데이터가 아예 없다 - 부트로더를 아직 한 번도 거치지 않은
     * 공장 초기 상태(예: 개발 중 이 이미지를 ST-Link로 직접 플래싱한 경우)일
     * 수 있다. 확정할 대상 자체가 없으므로 그냥 성공으로 간주하고 넘어간다. */
    return true;
  }

  meta = candidates[ ( uint32_t )current_page ];

  if( meta.state != BL_STATE_PENDING_TEST )
  {
    /* 이미 CONFIRMED 상태다(예: 정상적으로 오래 동작해온 펌웨어의 일반적인
     * 재부팅) - 다시 쓸 필요가 없다. 불필요한 Flash 쓰기를 피해서 마모를
     * 줄인다. */
    return true;
  }

  /* PENDING_TEST -> CONFIRMED로 전환하고 재시도 카운터를 리셋한다.
   * 이 시점 이후로는 이 슬롯이 "검증된 정상 펌웨어"로 취급되어, 다음에
   * 전원이 나갔다 들어와도 부트로더가 망설임 없이 바로 이 슬롯으로 점프한다. */
  meta.state = BL_STATE_CONFIRMED;
  meta.retry_count = 0U;

  target_page = ( current_page == 0 ) ? 1U : 0U;
  target_base = s_page_base[ target_page ];

  meta.magic = BL_META_MAGIC;
  meta.sequence = candidates[ ( uint32_t )current_page ].sequence + 1U;
  meta.crc32 = BootShared_Crc32( ( const uint8_t * )&meta, ( uint32_t )offsetof( BlBootMetadata_t, crc32 ) );

  ( void )HAL_FLASH_Unlock();

  erase.TypeErase = FLASH_TYPEERASE_PAGES;
  erase.Banks = FLASH_BANK_1;
  erase.Page = PageNumberFromAddress( target_base );
  erase.NbPages = 1U;

  ok = ( HAL_FLASHEx_Erase( &erase, &page_error ) == HAL_OK );

  if( ok )
  {
    /* 주의: ARM GCC는 임베디드 빌드에서 기본적으로 enum을 표현 가능한 최소
     * 크기(예: 1바이트)로 압축한다(-fshort-enums, ARM EABI 표준 동작).
     * 그래서 이 구조체의 sizeof() 값이 8의 배수로 딱 떨어지지 않을 수 있다
     * (예: 56바이트가 아니라 52바이트). 예전에는 sizeof(meta)/8을 정수
     * 나눗셈으로 그냥 잘라 써서, 나머지 바이트(하필 crc32 필드 자신)가
     * Flash에 기록되지 않아 "저장은 성공했다는데 다음에 읽으면 항상 무효
     * 판정"되는 버그가 있었다(Bootloader 프로젝트의 boot_meta.c와 동일한
     * 버그). 나머지를 0xFF로 패딩해서 마저 쓰는 방식으로 고쳤다. */
    const uint8_t *src_bytes = ( const uint8_t * )( const void * )&meta;
    uint32_t total_bytes = ( uint32_t )sizeof( BlBootMetadata_t );
    uint32_t full_doublewords = total_bytes / 8U;
    uint32_t remainder = total_bytes % 8U;
    uint32_t w;

    for( w = 0U; ( w < full_doublewords ) && ok; w++ )
    {
      uint64_t value;

      ( void )memcpy( &value, &src_bytes[ w * 8U ], 8U );
      if( HAL_FLASH_Program( FLASH_TYPEPROGRAM_DOUBLEWORD, target_base + ( w * 8U ), value ) != HAL_OK )
      {
        ok = false;
      }
    }

    if( ok && ( remainder != 0U ) )
    {
      uint8_t padded[ 8 ];
      uint64_t value;
      uint32_t i;

      for( i = 0U; i < 8U; i++ )
      {
        padded[ i ] = ( i < remainder ) ? src_bytes[ ( full_doublewords * 8U ) + i ] : 0xFFU;
      }
      ( void )memcpy( &value, padded, 8U );

      if( HAL_FLASH_Program( FLASH_TYPEPROGRAM_DOUBLEWORD, target_base + ( full_doublewords * 8U ), value ) != HAL_OK )
      {
        ok = false;
      }
    }
  }

  ( void )HAL_FLASH_Lock();
  return ok;
}
