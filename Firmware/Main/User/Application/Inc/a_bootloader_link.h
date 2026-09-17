#ifndef A_BOOTLOADER_LINK_H
#define A_BOOTLOADER_LINK_H

/*
 * a_bootloader_link : 부트로더가 관리하는 "부트 메타데이터"(A/B 슬롯 중 어느
 * 쪽이 실행 중인지, CONFIRMED/PENDING_TEST 상태 등)를 앱이 확정(confirm)하는
 * 역할을 담당한다.
 *
 * 왜 필요한가?
 *  - 새 펌웨어가 업데이트로 올라오면 부트로더는 메타데이터를 PENDING_TEST
 *    상태로 표시하고 그 슬롯으로 부팅시킨다("한 번 시험 삼아 부팅해보는 것"
 *    이라는 뜻 - 아직 신뢰하지 않는다는 의미).
 *  - 새 펌웨어(=지금 이 코드)가 Application_Init()을 끝까지 문제없이 마쳤다면
 *    "이 펌웨어는 정상 동작한다"는 증거이므로, 여기서 Bootloader_ConfirmBootOk()
 *    를 호출해서 메타데이터를 CONFIRMED로 바꿔준다.
 *  - 만약 새 펌웨어가 초기화 도중 멈추거나 크래시해서 이 함수가 호출되지
 *    못하면, IWDG(하드웨어 워치독)가 결국 리셋시킨다. 리셋 후 부트로더가
 *    다시 부팅 판단을 할 때 "PENDING_TEST인데 여태 CONFIRMED가 안 됐네?"를
 *    보고 자동으로 반대 슬롯(이전에 잘 동작하던 펌웨어)으로 롤백한다.
 *    즉 이 함수를 "제때 호출하는 것" 자체가 A/B 안전장치의 핵심 축이다.
 */

#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

/* Application_Init()이 성공적으로 끝난 직후 딱 한 번 호출한다.
 * 현재 실행 중인 슬롯의 부트 메타데이터가 PENDING_TEST 상태이면 CONFIRMED로
 * 바꿔서 Flash에 저장하고, 이미 CONFIRMED 상태이거나 메타데이터가 아예 없는
 * (공장 초기 상태) 경우에는 아무것도 하지 않고 성공(true)을 반환한다.
 * 반환값 false는 Flash 쓰기 실패(하드웨어 오류)를 의미한다 - 이 경우에도
 * 앱 동작 자체는 계속 진행해도 무방하다(다음 정상 종료/재확정 기회에
 * 다시 시도될 수 있음). */
bool Bootloader_ConfirmBootOk( void );

#ifdef __cplusplus
}
#endif

#endif /* A_BOOTLOADER_LINK_H */
