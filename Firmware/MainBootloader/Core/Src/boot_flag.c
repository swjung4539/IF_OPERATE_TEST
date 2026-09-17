#include "boot_flag.h"
#include "bootloader_shared.h"

/* 링커스크립트의 NOINIT 영역(.noinit_boot_flag)에 배치, 소프트 리셋에도 값 유지 */
static volatile BlNoinitFlag_t s_noinit_flag __attribute__( ( section( ".noinit_boot_flag" ) ) );

bool BootFlag_IsEnterUpdateRequested( void )
{
  return s_noinit_flag.magic == BL_NOINIT_MAGIC_ENTER_UPDATE;
}

void BootFlag_Clear( void )
{
  s_noinit_flag.magic = 0U;
}

void BootFlag_RequestEnterUpdate( void )
{
  s_noinit_flag.magic = BL_NOINIT_MAGIC_ENTER_UPDATE;
}
