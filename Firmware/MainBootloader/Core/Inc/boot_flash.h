#ifndef BOOT_FLASH_H
#define BOOT_FLASH_H

/*
 * boot_flash : 슬롯(A/B) 영역에 대한 "소거"와 "쓰기"를 담당하는 저수준 헬퍼.
 *
 * STM32L4 Flash의 중요한 제약 두 가지를 이 모듈 안에서만 다루고,
 * 상위 코드(boot_protocol.c)는 이런 하드웨어 디테일을 몰라도 되게 감싸준다.
 *
 *  1) 소거 단위는 "페이지"(2KB) - 페이지 전체를 한 번에 0xFF로 지운다.
 *     일부만 지우는 것은 불가능하다.
 *  2) 프로그래밍 단위는 "double-word"(8바이트) - 반드시 8바이트씩,
 *     그것도 8의 배수 주소에만 쓸 수 있다. 1바이트나 4바이트 단위로는 못 쓴다.
 *     그리고 "이미 값이 들어있는 곳(0xFF가 아닌 곳)"에 다시 쓰면 안 된다
 *     (그 전에 반드시 페이지 소거를 먼저 해야 한다).
 */

#include <stdint.h>
#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

/* [addr, addr+size) 범위를 포함하는 모든 2KB 페이지를 소거한다.
 * addr은 반드시 슬롯 시작 주소(페이지 정렬된 주소)여야 한다.
 * 반환값 false면 HAL_FLASHEx_Erase 실패(하드웨어 오류 등). */
bool BootFlash_EraseRange( uint32_t addr, uint32_t size );

/* addr(반드시 8의 배수)부터 len바이트를 프로그래밍한다.
 * len이 8의 배수가 아니면, 마지막 double-word의 남는 자리는 0xFF(소거된 값)로
 * 채워서 쓴다 - 어차피 소거 상태와 같은 값이라 실제 플래시 내용에는 영향이 없다.
 * 주의: addr~addr+len 범위는 이 함수를 호출하기 전에 반드시 소거되어 있어야 한다. */
bool BootFlash_Write( uint32_t addr, const uint8_t *data, uint32_t len );

/* STM32L4의 Flash 컨트롤러에는 별도의 명령어(I) 캐시와 데이터(D) 캐시가 있다.
 * 이 캐시들은 소프트웨어 리셋(NVIC_SystemReset)에도 내용이 살아남을 수 있어서,
 * 부트로더가 어떤 슬롯의 Flash를 소거/프로그래밍한 뒤, 그 슬롯으로 실제
 * 점프하기 직전에 이 함수로 캐시를 리셋해주지 않으면, 방금 새로 써넣은
 * 내용 대신 이전(소거 전 또는 다른 시점)의 캐시된 값을 읽어버릴 수 있다 -
 * 실제로 이 문제 때문에 USB로 올린 새 펌웨어가 CRC32 검증은 통과하고도
 * 부팅 시 초기화 코드(.data 복사)에서 HardFault가 나는 버그가 있었다.
 * ERASE_SLOT/WRITE_CHUNK로 Flash를 건드린 뒤와, Bootloader_JumpToSlot() 직전에
 * 반드시 호출해야 한다. */
void BootFlash_FlushCaches( void );

#ifdef __cplusplus
}
#endif

#endif /* BOOT_FLASH_H */
