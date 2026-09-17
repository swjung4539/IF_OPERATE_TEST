#ifndef BOOT_FLAG_H
#define BOOT_FLAG_H

#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

/* 앱이 "업데이트 모드로 진입해줘"라고 요청했는지 확인 (.noinit RAM 플래그 검사) */
bool BootFlag_IsEnterUpdateRequested( void );

/* 확인 후 플래그 클리어(재부팅 후 계속 부트로더에 머무르지 않도록) */
void BootFlag_Clear( void );

/* 앱 측에서 사용: 업데이트 진입을 요청하고 플래그를 세팅 */
void BootFlag_RequestEnterUpdate( void );

#ifdef __cplusplus
}
#endif

#endif /* BOOT_FLAG_H */
