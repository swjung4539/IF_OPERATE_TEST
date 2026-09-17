#include "boot_rxbuf.h"

/* 실제 저장 공간. static이라 이 파일 밖에서는 접근 못 하고,
 * 반드시 아래 함수들을 통해서만 접근하도록 강제한다. */
static uint8_t  s_buffer[ BOOT_RXBUF_SIZE ];

/* head : 다음에 "쓸" 위치(ISR이 갱신) / tail : 다음에 "읽을" 위치(메인루프가 갱신).
 * volatile인 이유: 컴파일러가 "어차피 이 함수 안에서는 안 바뀌는 값"이라고
 * 착각해서 레지스터에 캐싱해버리면, 인터럽트/메인루프 양쪽에서 서로의 최신 값을
 * 못 보고 낡은 값을 참조하는 버그가 생길 수 있다. volatile을 붙여서
 * "매번 메모리에서 실제로 다시 읽어라"라고 컴파일러에게 알려준다. */
static volatile uint32_t s_head = 0U;
static volatile uint32_t s_tail = 0U;

void BootRxBuf_Init( void )
{
  s_head = 0U;
  s_tail = 0U;
}

void BootRxBuf_Push( const uint8_t *data, uint32_t len )
{
  uint32_t i;

  for( i = 0U; i < len; i++ )
  {
    /* 다음에 head가 가리킬 위치가 현재 tail과 같아지면 버퍼가 꽉 찬 것이다.
     * (링버퍼에서 "가득 참"과 "완전히 빔"을 구분하려고 한 칸을 항상 비워두는
     *  전형적인 트릭 - head와 tail이 정확히 같으면 "비어있음", head+1이 tail과
     *  같으면 "가득참"으로 판단한다.) */
    uint32_t next_head = ( s_head + 1U ) % BOOT_RXBUF_SIZE;

    if( next_head == s_tail )
    {
      /* 버퍼 풀(overrun) - 더 이상 못 넣으므로 나머지 바이트는 조용히 버린다.
       * 프레임이 깨지면 CRC16이 안 맞아서 상위 계층(boot_protocol)이 알아서
       * 그 프레임을 무시하고 다음 SOF를 다시 찾으므로 시스템이 죽지는 않는다. */
      break;
    }

    s_buffer[ s_head ] = data[ i ];
    s_head = next_head;
  }
}

bool BootRxBuf_PopByte( uint8_t *out )
{
  if( s_head == s_tail )
  {
    /* head == tail이면 버퍼가 비어있다는 뜻 - 읽을 게 없다. */
    return false;
  }

  *out = s_buffer[ s_tail ];
  s_tail = ( s_tail + 1U ) % BOOT_RXBUF_SIZE;
  return true;
}
