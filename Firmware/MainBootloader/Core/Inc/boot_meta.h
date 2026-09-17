#ifndef BOOT_META_H
#define BOOT_META_H

#include "bootloader_shared.h"
#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ping-pong 2페이지 중 유효(magic+crc32 일치)하고 sequence가 더 큰 것을 반환.
 * 둘 다 무효면(최초 부팅/공장 초기 상태) Slot A / CONFIRMED 기본값을 반환한다. */
BlBootMetadata_t BootMeta_Load( void );

/* meta의 sequence를 1 증가시키고 crc32를 재계산한 뒤, 현재 사용 중이 아닌 페이지에 소거+기록한다.
 * 반환값 false면 flash 오류(HAL_FLASH_Program/Erase 실패). */
bool BootMeta_Save( BlBootMetadata_t *meta );

/* 편의 함수: 대상 슬롯으로 즉시 CONFIRMED 상태를 만들어 저장 (롤백/최초 확정용) */
bool BootMeta_SetConfirmed( BlSlotId_t slot );

#ifdef __cplusplus
}
#endif

#endif /* BOOT_META_H */
