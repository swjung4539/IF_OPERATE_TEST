#include "a_boot_link.h"
#include "bootloader_shared.h"
#include "main.h"
#include <string.h>
#include <stdbool.h>

/* 앱은 ENTER_UPDATE(payload 없음) 프레임 하나만 처리하면 되므로, 혹시 모를
 * 다른 커맨드가 실수로 들어와도 안전하게 무시할 수 있게 payload 여유 공간만
 * 작게 잡는다(부트로더 쪽처럼 256바이트씩 큰 버퍼를 둘 필요가 없다). */
#define APP_BOOT_LINK_MAX_PAYLOAD   (16U)

/* ============================================================
 * USB 인터럽트(ISR) -> 일반 task 사이에서 바이트를 안전하게 넘기기 위한
 * 아주 작은 SPSC(단일 생산자/단일 소비자) 링버퍼.
 * 원리는 Bootloader 프로젝트의 boot_rxbuf.c와 완전히 동일하다:
 * head는 ISR만 갱신, tail은 task만 갱신하므로 별도 락이 필요 없다.
 * ENTER_UPDATE 프레임 자체가 7바이트(SOF2+CMD1+LEN2+CRC2, payload=0)로
 * 아주 작으므로 버퍼도 작게(64바이트) 잡아도 충분하다. ============================================================ */
#define APP_BOOT_RXBUF_SIZE   (64U)

static uint8_t           s_rxbuf[ APP_BOOT_RXBUF_SIZE ];
static volatile uint32_t s_rxbuf_head = 0U;
static volatile uint32_t s_rxbuf_tail = 0U;

static void RxBufPush( const uint8_t *data, uint32_t len )
{
  uint32_t i;

  for( i = 0U; i < len; i++ )
  {
    uint32_t next_head = ( s_rxbuf_head + 1U ) % APP_BOOT_RXBUF_SIZE;

    if( next_head == s_rxbuf_tail )
    {
      /* 버퍼가 꽉 찼다 - 남은 바이트는 버린다. 어차피 프레임이 깨지면
       * CRC16 검증에서 걸러지므로 상위 로직은 안전하다. */
      break;
    }

    s_rxbuf[ s_rxbuf_head ] = data[ i ];
    s_rxbuf_head = next_head;
  }
}

static bool RxBufPop( uint8_t *out )
{
  if( s_rxbuf_head == s_rxbuf_tail )
  {
    return false;
  }

  *out = s_rxbuf[ s_rxbuf_tail ];
  s_rxbuf_tail = ( s_rxbuf_tail + 1U ) % APP_BOOT_RXBUF_SIZE;
  return true;
}

/* ============================================================
 * 프레임 조립 상태머신. 부트로더의 boot_protocol.c와 동일한 구조
 * (SOF0 -> SOF1 -> CMD -> LEN(2) -> PAYLOAD(LEN) -> CRC16(2))를 따르되,
 * 앱은 커맨드를 여러 개 처리할 필요가 없어 훨씬 단순화되어 있다.
 * ============================================================ */
typedef enum
{
  RX_WAIT_SOF0 = 0,
  RX_WAIT_SOF1,
  RX_WAIT_CMD,
  RX_WAIT_LEN0,
  RX_WAIT_LEN1,
  RX_READ_PAYLOAD,
  RX_WAIT_CRC0,
  RX_WAIT_CRC1
} RxState_t;

static RxState_t s_rx_state = RX_WAIT_SOF0;
static uint8_t   s_cmd;
static uint16_t  s_len;
static uint16_t  s_payload_index;
static uint8_t   s_payload[ APP_BOOT_LINK_MAX_PAYLOAD ];
static uint16_t  s_crc_received;

/* 앱 -> 부트로더 재부팅 신호. 링커스크립트의 NOINIT 영역(.noinit_boot_flag)에
 * 배치되어 소프트 리셋(HAL_NVIC_SystemReset)에도 값이 살아남는다.
 * 부트로더 쪽 boot_flag.c와 정확히 같은 섹션 이름/구조체를 사용해야
 * 서로 같은 주소, 같은 의미로 값을 주고받을 수 있다. */
static volatile BlNoinitFlag_t s_noinit_flag __attribute__( ( section( ".noinit_boot_flag" ) ) );

static void RequestEnterBootloaderUpdate( void )
{
  s_noinit_flag.magic = BL_NOINIT_MAGIC_ENTER_UPDATE;
}

/* 완성된 프레임 하나를 CRC16 검증 후, ENTER_UPDATE인 경우에만 실제로 동작한다. */
static void ProcessCompleteFrame( void )
{
  uint8_t  header_and_payload[ 3U + APP_BOOT_LINK_MAX_PAYLOAD ];
  uint16_t computed_crc;

  header_and_payload[ 0 ] = s_cmd;
  header_and_payload[ 1 ] = ( uint8_t )( s_len & 0xFFU );
  header_and_payload[ 2 ] = ( uint8_t )( ( s_len >> 8 ) & 0xFFU );

  if( s_len > 0U )
  {
    ( void )memcpy( &header_and_payload[ 3 ], s_payload, s_len );
  }

  computed_crc = BootShared_Crc16( header_and_payload, ( uint32_t )( 3U + s_len ) );

  if( computed_crc != s_crc_received )
  {
    /* CRC 불일치 - 잡음/깨진 프레임으로 보고 조용히 버린다. */
    return;
  }

  if( ( BlCmd_t )s_cmd == BL_CMD_ENTER_UPDATE )
  {
    /* 정상적인 업데이트 진입 요청이 확인됐다. 플래그를 세팅하고 즉시
     * 소프트 리셋한다 - 이 함수는 리턴하지 않는다(재부팅되므로).
     * 리셋 후 부트로더가 이 플래그를 보고 USB CDC를 켠 채로 상주하며
     * PC GUI의 ERASE_SLOT/WRITE_CHUNK/FINALIZE/REBOOT 명령을 처리한다. */
    RequestEnterBootloaderUpdate();
    HAL_NVIC_SystemReset();
  }

  /* ENTER_UPDATE가 아닌 다른 커맨드는 앱 입장에서 의미가 없으므로 무시한다. */
}

void AppBootLink_Init( void )
{
  s_rxbuf_head = 0U;
  s_rxbuf_tail = 0U;
  s_rx_state   = RX_WAIT_SOF0;
}

void AppBootLink_OnUsbReceive( const uint8_t *data, uint32_t len )
{
  RxBufPush( data, len );
}

void AppBootLink_Process( void )
{
  uint8_t byte;

  while( RxBufPop( &byte ) )
  {
    switch( s_rx_state )
    {
      case RX_WAIT_SOF0:
        if( byte == BL_FRAME_SOF0 )
        {
          s_rx_state = RX_WAIT_SOF1;
        }
        break;

      case RX_WAIT_SOF1:
        s_rx_state = ( byte == BL_FRAME_SOF1 ) ? RX_WAIT_CMD : RX_WAIT_SOF0;
        break;

      case RX_WAIT_CMD:
        s_cmd = byte;
        s_rx_state = RX_WAIT_LEN0;
        break;

      case RX_WAIT_LEN0:
        s_len = ( uint16_t )byte;
        s_rx_state = RX_WAIT_LEN1;
        break;

      case RX_WAIT_LEN1:
        s_len |= ( uint16_t )( ( uint16_t )byte << 8 );

        if( s_len > APP_BOOT_LINK_MAX_PAYLOAD )
        {
          /* 앱이 처리할 일 없는(너무 큰 payload) 프레임 - 잡음으로 보고
           * 재동기화한다. 부트로더로 가야 할 ERASE_SLOT/WRITE_CHUNK 같은
           * 큰 프레임은 애초에 앱이 아니라 부트로더 상주 상태에서만
           * 오가는 것이므로 여기서는 볼 일이 없다. */
          s_rx_state = RX_WAIT_SOF0;
        }
        else
        {
          s_payload_index = 0U;
          s_rx_state = ( s_len == 0U ) ? RX_WAIT_CRC0 : RX_READ_PAYLOAD;
        }
        break;

      case RX_READ_PAYLOAD:
        s_payload[ s_payload_index ] = byte;
        s_payload_index++;
        if( s_payload_index >= s_len )
        {
          s_rx_state = RX_WAIT_CRC0;
        }
        break;

      case RX_WAIT_CRC0:
        s_crc_received = ( uint16_t )byte;
        s_rx_state = RX_WAIT_CRC1;
        break;

      case RX_WAIT_CRC1:
        s_crc_received |= ( uint16_t )( ( uint16_t )byte << 8 );
        ProcessCompleteFrame();
        s_rx_state = RX_WAIT_SOF0;
        break;

      default:
        s_rx_state = RX_WAIT_SOF0;
        break;
    }
  }
}
