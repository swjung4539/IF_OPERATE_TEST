#include "boot_meta.h"
#include "boot_flash.h"
#include "main.h"
#include <string.h>
#include <stddef.h>

#define META_PAGE_COUNT   (2U)

static const uint32_t s_page_base[ META_PAGE_COUNT ] = { BL_META_PAGE0_BASE, BL_META_PAGE1_BASE };

static uint32_t PageNumberFromAddress( uint32_t address )
{
  return ( address - BL_FLASH_BASE ) / FLASH_PAGE_SIZE;
}

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

BlBootMetadata_t BootMeta_Load( void )
{
  BlBootMetadata_t candidates[ META_PAGE_COUNT ];
  bool valid[ META_PAGE_COUNT ];
  int32_t best = -1;
  uint32_t i;
  BlBootMetadata_t def;

  for( i = 0U; i < META_PAGE_COUNT; i++ )
  {
    valid[ i ] = ReadPage( s_page_base[ i ], &candidates[ i ] );
    if( valid[ i ] && ( ( best < 0 ) || ( candidates[ i ].sequence > candidates[ ( uint32_t )best ].sequence ) ) )
    {
      best = ( int32_t )i;
    }
  }

  if( best >= 0 )
  {
    return candidates[ ( uint32_t )best ];
  }

  /* 유효 레코드 없음: 최초 부팅(공장 초기 상태) 기본값 - Slot A, CONFIRMED */
  ( void )memset( &def, 0, sizeof( def ) );
  def.magic = BL_META_MAGIC;
  def.sequence = 0U;
  def.active_slot = BL_SLOT_A;
  def.state = BL_STATE_CONFIRMED;
  def.retry_count = 0U;
  def.crc32 = BootShared_Crc32( ( const uint8_t * )&def, ( uint32_t )offsetof( BlBootMetadata_t, crc32 ) );
  return def;
}

bool BootMeta_Save( BlBootMetadata_t *meta )
{
  BlBootMetadata_t candidates[ META_PAGE_COUNT ];
  bool valid[ META_PAGE_COUNT ];
  int32_t current_page = -1;
  uint32_t i;
  uint32_t target_page;
  uint32_t target_base;
  FLASH_EraseInitTypeDef erase = { 0 };
  uint32_t page_error = 0U;
  bool ok;

  for( i = 0U; i < META_PAGE_COUNT; i++ )
  {
    valid[ i ] = ReadPage( s_page_base[ i ], &candidates[ i ] );
    if( valid[ i ] && ( ( current_page < 0 ) || ( candidates[ i ].sequence > candidates[ ( uint32_t )current_page ].sequence ) ) )
    {
      current_page = ( int32_t )i;
    }
  }

  target_page = ( current_page == 0 ) ? 1U : 0U;
  target_base = s_page_base[ target_page ];

  meta->magic = BL_META_MAGIC;
  meta->sequence = ( current_page >= 0 ) ? ( candidates[ ( uint32_t )current_page ].sequence + 1U ) : 1U;
  meta->crc32 = BootShared_Crc32( ( const uint8_t * )meta, ( uint32_t )offsetof( BlBootMetadata_t, crc32 ) );

  ( void )HAL_FLASH_Unlock();

  erase.TypeErase = FLASH_TYPEERASE_PAGES;
  erase.Banks = FLASH_BANK_1;
  erase.Page = PageNumberFromAddress( target_base );
  erase.NbPages = 1U;

  ok = ( HAL_FLASHEx_Erase( &erase, &page_error ) == HAL_OK );

  if( ok )
  {
    /* 주의: ARM GCC(arm-none-eabi)는 임베디드 빌드에서 기본적으로
     * enum을 표현 가능한 최소 크기(예: 1바이트)로 압축한다(-fshort-enums,
     * ARM EABI 표준 동작). 그래서 이 구조체의 실제 sizeof() 값은
     * 8의 배수로 딱 떨어지지 않을 수 있다(예: 56바이트가 아니라 52바이트).
     * 예전에는 sizeof(meta)/8을 정수 나눗셈으로 그냥 잘라 썼는데, 그러면
     * 나머지 바이트(하필 crc32 필드 자신)가 Flash에 기록되지 않아
     * "저장은 성공했다고 나오는데 다음에 읽으면 항상 무효 판정"되는
     * 버그가 있었다. boot_flash.c의 BootFlash_Write()처럼 나머지를
     * 0xFF로 패딩해서 마저 쓰는 방식으로 고쳤다. */
    const uint8_t *src_bytes = ( const uint8_t * )( const void * )meta;
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
  BootFlash_FlushCaches();
  return ok;
}

bool BootMeta_SetConfirmed( BlSlotId_t slot )
{
  BlBootMetadata_t meta = BootMeta_Load();

  meta.active_slot = slot;
  meta.state = BL_STATE_CONFIRMED;
  meta.retry_count = 0U;
  return BootMeta_Save( &meta );
}
