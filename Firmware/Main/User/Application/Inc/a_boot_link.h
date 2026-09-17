#ifndef A_BOOT_LINK_H
#define A_BOOT_LINK_H

/*
 * a_boot_link : 앱이 USB CDC로 "펌웨어 업데이트 모드로 들어가줘"라는 명령을
 * 받아서 부트로더로 확정적으로 넘어가는(재부팅하는) 역할을 담당하는 모듈.
 *
 * 앱은 평상시에도 USB CDC(가상 COM포트)가 항상 열려 있으므로, PC의 펌웨어
 * 업데이트 GUI가 이 포트에 연결해서 짧은 프레임 하나(ENTER_UPDATE)를 보내면
 * 된다. 부팅 시 짧은 대기창을 두고 눈치보는 방식이 아니라, 앱이 살아있는
 * 동안 언제든 확정적으로 이 명령을 받아 처리하는 구조라 타이밍 레이스가 없다.
 *
 * 프레임 형식은 부트로더 쪽(`Bootloader/Bootloader/Core/Src/boot_protocol.c`)과
 * 완전히 동일하다: [0xAA][0x55][CMD:1][LEN:2][PAYLOAD:LEN][CRC16:2].
 * 다만 앱은 이 프레임들 중 `BL_CMD_ENTER_UPDATE`(payload 없음) 하나만
 * 알아들으면 되므로, 부트로더처럼 여러 커맨드를 처리하는 무거운 파서는
 * 필요 없고 훨씬 단순한 버전으로 구현한다.
 */

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* 파서 상태 초기화. Application_Init() 등 앱 시작 시 한 번 호출한다. */
void AppBootLink_Init( void );

/* [USB 인터럽트 컨텍스트에서 호출] usbd_cdc_if.c의 CDC_Receive_FS()가
 * 받은 원시 바이트를 그대로 넘겨준다. 여기서는 내부 링버퍼에 복사만 하고
 * 즉시 리턴한다(무거운 처리는 AppBootLink_Process()에서 한다). */
void AppBootLink_OnUsbReceive( const uint8_t *data, uint32_t len );

/* [일반 task 컨텍스트에서 호출, 주기적으로 반복] 링버퍼에 쌓인 바이트로
 * 프레임을 조립하고, 유효한(CRC16 일치) ENTER_UPDATE 프레임을 받으면
 * 부트로더 재진입을 예약(.noinit RAM 매직 플래그 세팅)한 뒤
 * HAL_NVIC_SystemReset()으로 즉시 재부팅한다 - 이 경우 이 함수는 리턴하지 않는다.
 * 그 외에는(프레임 미완성, CRC 불일치, 다른 커맨드) 조용히 리턴한다. */
void AppBootLink_Process( void );

#ifdef __cplusplus
}
#endif

#endif /* A_BOOT_LINK_H */
