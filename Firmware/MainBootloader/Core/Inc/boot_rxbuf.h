#ifndef BOOT_RXBUF_H
#define BOOT_RXBUF_H

/*
 * boot_rxbuf : USB CDC 수신 바이트를 담아두는 "단일 생산자/단일 소비자(SPSC)" 링버퍼.
 *
 * 왜 필요한가?
 *  - USB CDC의 CDC_Receive_FS() 콜백은 USB 인터럽트(OTG_FS IRQ) 컨텍스트에서 호출된다.
 *    이 콜백 안에서 프레임 파싱이나 Flash 쓰기 같은 무거운 작업을 하면 인터럽트가
 *    오래 붙잡혀 다른 USB 이벤트 처리가 늦어지거나, Flash 소거/프로그래밍 중
 *    또 다른 인터럽트가 겹치는 위험한 상황이 생길 수 있다.
 *  - 그래서 콜백에서는 "받은 바이트를 이 링버퍼에 복사만 하고 즉시 리턴"하고,
 *    실제 프레임 해석/커맨드 처리는 main()의 while(1) 슈퍼루프(일반 컨텍스트)에서
 *    한가할 때 꺼내서 처리한다.
 *
 * 왜 락(lock)이 필요 없는가?
 *  - "생산자"(ISR에서 쓰는 head)와 "소비자"(메인루프에서 읽는 tail)가 각각
 *    자기 쪽 인덱스만 갱신하고 상대방 인덱스는 읽기만 하기 때문에,
 *    두 인덱스를 volatile로 선언해두면 별도의 뮤텍스/critical section 없이도
 *    안전하게 동작한다(전형적인 SPSC 링버퍼 패턴).
 */

#include <stdint.h>
#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

/* 프레임 하나(SOF 2 + CMD 1 + LEN 2 + PAYLOAD 최대 256 + CRC16 2 = 263바이트)가
 * 넉넉히 들어가고도 여러 프레임이 밀려도 견디도록 여유있게 잡은 크기.
 * 2의 거듭제곱으로 두면 나머지 연산(%) 대신 비트마스크로 인덱스를 감쌀 수 있어
 * 살짝 더 빠르지만, 여기서는 가독성을 위해 그냥 나머지 연산을 사용한다. */
#define BOOT_RXBUF_SIZE   (1024U)

/* 버퍼 초기화(head=tail=0). main() 시작 시 한 번만 호출하면 된다. */
void BootRxBuf_Init( void );

/* [ISR 컨텍스트에서 호출] data[0..len-1]을 링버퍼에 밀어 넣는다.
 * 버퍼가 가득 차 더 못 넣는 바이트는 그냥 버린다(오버런) - 프레임 CRC16 검증에서
 * 어차피 걸러지므로 상위 프로토콜 레벨에서는 안전하다. */
void BootRxBuf_Push( const uint8_t *data, uint32_t len );

/* [메인루프 컨텍스트에서 호출] 바이트 하나를 꺼낸다.
 * 꺼낼 데이터가 있으면 true를 반환하고 *out에 값을 채운다.
 * 버퍼가 비어있으면 false를 반환한다(폴링 방식). */
bool BootRxBuf_PopByte( uint8_t *out );

#ifdef __cplusplus
}
#endif

#endif /* BOOT_RXBUF_H */
