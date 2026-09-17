#ifndef BOOT_JUMP_H
#define BOOT_JUMP_H

#include "bootloader_shared.h"
#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

/* 해당 슬롯에 실행 가능한 이미지가 있는지(초기 스택포인터가 RAM 범위를
 * 가리키는지)만 확인한다. 점프는 하지 않는다 - 호출자가 이 결과를 보고
 * 점프할지, USB 업데이트 대기 모드로 남을지 미리 판단할 수 있게 한다. */
bool Bootloader_SlotHasValidImage( BlSlotId_t slot );

/* 지정한 슬롯으로 VTOR 재설정 + MSP 로드 + Reset_Handler 분기. 정상 이미지가 있으면 리턴하지 않는다.
 * 반드시 Bootloader_SlotHasValidImage()로 먼저 확인한 뒤에만 호출해야 한다 - 빈/손상된
 * 슬롯으로 호출하면 안전을 위해 Error_Handler()로 빠져 무한정지한다(USB도 켜지지 않음). */
void Bootloader_JumpToSlot( BlSlotId_t slot );

#ifdef __cplusplus
}
#endif

#endif /* BOOT_JUMP_H */
