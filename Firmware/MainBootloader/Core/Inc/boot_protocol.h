#ifndef BOOT_PROTOCOL_H
#define BOOT_PROTOCOL_H

/*
 * boot_protocol : PC의 펌웨어 업데이트 GUI와 USB CDC(가상 COM포트)로 주고받는
 * "펌웨어 업데이트 전용 프레임 프로토콜"의 파서 + 커맨드 처리기.
 *
 * 프레임 형식 (모든 다바이트 필드는 리틀엔디안):
 *   [0xAA][0x55][CMD:1][LEN:2][PAYLOAD:LEN바이트][CRC16:2]
 *   - 0xAA,0x55       : SOF(Start Of Frame) 매직 바이트 2개. 이 두 바이트가
 *                        연속으로 오기 전까지는 그냥 잡음(noise)으로 보고 버린다.
 *   - CMD             : BlCmd_t 값 (bootloader_shared.h에 정의).
 *   - LEN             : PAYLOAD의 길이(바이트 수).
 *   - PAYLOAD         : 커맨드별로 의미가 다른 실제 데이터.
 *   - CRC16           : SOF 2바이트는 빼고 CMD+LEN+PAYLOAD 전체에 대한 CRC16
 *                        (bootloader_shared.h의 BootShared_Crc16, Modbus와 동일 다항식).
 *
 * 이 모듈은 "메인 슈퍼루프에서 반복 호출되는" 폴링(polling) 스타일로 동작한다.
 * (RTOS도 없고 인터럽트 기반 이벤트 큐도 안 쓰는 가장 단순한 방식 - 부트로더는
 *  실시간성이 필요 없고 코드 크기/복잡도를 최소화하는 게 더 중요하기 때문.)
 */

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* 상태 초기화. main()에서 USB를 켜고 업데이트 모드로 들어가기로 결정한 직후
 * 딱 한 번 호출한다. */
void BootProtocol_Init( void );

/* main()의 while(1) 루프에서 매 iteration마다 호출한다.
 * 내부에서 BootRxBuf_PopByte()로 수신 바이트를 하나씩 꺼내 프레임을 조립하고,
 * 완전한 프레임이 완성되면(CRC16까지 확인) 즉시 해당 커맨드를 처리한 뒤
 * 응답 프레임을 CDC_Transmit_FS()로 돌려보낸다.
 * REBOOT 커맨드를 받으면 이 함수 내부에서 응답을 보낸 뒤 재부팅하므로
 * 정상적으로는 이 함수가 무한히 반복 호출된다(리턴은 하지만 재부팅 전까지 계속 불림). */
void BootProtocol_Process( void );

#ifdef __cplusplus
}
#endif

#endif /* BOOT_PROTOCOL_H */
