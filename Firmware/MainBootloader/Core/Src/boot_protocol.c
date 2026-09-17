#include "boot_protocol.h"
#include "boot_rxbuf.h"
#include "boot_flash.h"
#include "boot_meta.h"
#include "boot_jump.h"
#include "bootloader_shared.h"
#include "usbd_cdc_if.h"   /* CDC_Transmit_FS(), USBD_BUSY */
#include "main.h"          /* HAL_Delay, HAL_NVIC_SystemReset */
#include <string.h>

/* 이 부트로더 자체의 버전(펌웨어 이미지 버전과는 별개). PING 응답에 실어 보낸다.
 * 부트로더 코드를 고칠 때마다 필요하면 올려주면 된다. */
#define BOOTLOADER_FW_VERSION_MAJOR  (0U)
#define BOOTLOADER_FW_VERSION_MINOR  (1U)
#define BOOTLOADER_FW_VERSION_PATCH  (0U)

/* ============================================================
 * 프레임 수신 상태머신
 * ------------------------------------------------------------
 * BootProtocol_Process()가 링버퍼에서 바이트를 하나씩 꺼낼 때마다
 * 아래 상태를 한 칸씩 전진시킨다. 마치 "자판기가 동전을 하나씩 받으면서
 * 다음에 뭘 기다려야 하는지 상태를 바꾸는 것"과 비슷한 구조다.
 *
 *   SOF0 --0xAA--> SOF1 --0x55--> CMD --1바이트--> LEN0,LEN1(2바이트)
 *        --> PAYLOAD(LEN바이트) --> CRC0,CRC1(2바이트) --> [프레임 완성, 처리]
 *
 * 중간에 예상과 다른 바이트가 오면(SOF1 자리에 0x55가 아닌 값이 온다든지)
 * 그 즉시 SOF0 대기 상태로 되돌아가서 "다시 처음부터 프레임을 찾는다"
 * (동기화가 깨졌을 때 스스로 복구하는 resync 로직). ============================================================ */
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
static uint8_t   s_payload[ BL_FRAME_MAX_PAYLOAD ];
static uint16_t  s_crc_received;

/* ============================================================
 * ERASE_SLOT ~ FINALIZE 사이에 유지되는 "쓰기 진행 상태".
 * GUI는 반드시 ERASE_SLOT -> WRITE_CHUNK(여러 번) -> FINALIZE 순서로
 * 명령을 보내야 하고, 이 상태는 그 시퀀스 동안의 문맥을 기억하는 역할이다.
 * ============================================================ */
static bool       s_erase_done       = false;  /* ERASE_SLOT이 성공적으로 끝났는지 */
static BlSlotId_t s_target_slot;               /* 지금 쓰고 있는 슬롯 (A 또는 B) */
static uint32_t   s_target_base;               /* 그 슬롯의 Flash 시작 주소 */
static uint32_t   s_target_total_size;         /* GUI가 ERASE_SLOT에서 알려준 전체 이미지 크기 */

/* ============================================================
 * 응답 프레임 전송 헬퍼
 * ------------------------------------------------------------
 * 요청 프레임과 똑같은 형식 [SOF0][SOF1][CMD][LEN][PAYLOAD][CRC16]으로
 * 응답을 만들어 USB CDC로 돌려보낸다. CMD는 처리한 요청의 커맨드를 그대로
 * 에코해서, GUI가 "이 응답이 어떤 요청에 대한 것인지" 쉽게 매칭할 수 있게 한다.
 * ============================================================ */
static void SendResponse( BlCmd_t cmd, const uint8_t *payload, uint16_t len )
{
  /* SOF(2) + CMD(1) + LEN(2) + PAYLOAD(len) + CRC16(2) */
  uint8_t  frame[ 5U + BL_FRAME_MAX_PAYLOAD + 2U ];
  uint16_t idx = 0U;
  uint16_t crc;
  uint32_t attempts;

  frame[ idx++ ] = BL_FRAME_SOF0;
  frame[ idx++ ] = BL_FRAME_SOF1;
  frame[ idx++ ] = ( uint8_t )cmd;
  frame[ idx++ ] = ( uint8_t )( len & 0xFFU );
  frame[ idx++ ] = ( uint8_t )( ( len >> 8 ) & 0xFFU );

  if( len > 0U )
  {
    ( void )memcpy( &frame[ idx ], payload, len );
    idx += len;
  }

  /* CRC16은 SOF 2바이트는 빼고, CMD+LEN+PAYLOAD 구간(=frame[2] 부터 idx-2 바이트)에
   * 대해서만 계산한다 - 요청 프레임을 검증할 때와 동일한 규칙이어야 GUI 쪽
   * 파서와 호환된다. */
  crc = BootShared_Crc16( &frame[ 2 ], ( uint32_t )( idx - 2U ) );
  frame[ idx++ ] = ( uint8_t )( crc & 0xFFU );
  frame[ idx++ ] = ( uint8_t )( ( crc >> 8 ) & 0xFFU );

  /* CDC_Transmit_FS는 이전 전송이 아직 진행 중이면 USBD_BUSY를 반환한다.
   * 부트로더는 RTOS가 없는 단순 슈퍼루프이므로, 그냥 잠깐 재시도하며
   * 기다리는(busy-wait) 방식으로 처리한다. attempts에 상한을 둬서
   * USB가 아예 끊긴 극단적인 상황에서도 무한루프에 빠지지 않게 한다. */
  attempts = 0U;
  while( ( CDC_Transmit_FS( frame, idx ) == USBD_BUSY ) && ( attempts < 100000U ) )
  {
    attempts++;
  }
}

/* ============================================================
 * PING(0x01) 처리
 * ------------------------------------------------------------
 * GUI가 "지금 이 장치가 부트로더 모드로 잘 들어와 있는지, 어느 슬롯이
 * 지금 실행 중(active)인지, 각 슬롯에 어떤 버전이 들어있는지"를 확인하는
 * 용도. 응답 payload 구성(총 13바이트):
 *   [0] status(항상 0=OK)
 *   [1..3] 부트로더 자체 버전 major/minor/patch
 *   [4] 현재 active_slot (0=A, 1=B)
 *   [5] SlotA valid(0/1)  [6..8] SlotA 버전 major/minor/patch
 *   [9] SlotB valid(0/1)  [10..12] SlotB 버전 major/minor/patch
 * ============================================================ */
static void HandlePing( void )
{
  BlBootMetadata_t meta = BootMeta_Load();
  uint8_t resp[ 17 ];
  uint8_t idx = 0U;

  resp[ idx++ ] = 0U; /* status = OK */
  resp[ idx++ ] = ( uint8_t )BOOTLOADER_FW_VERSION_MAJOR;
  resp[ idx++ ] = ( uint8_t )BOOTLOADER_FW_VERSION_MINOR;
  resp[ idx++ ] = ( uint8_t )BOOTLOADER_FW_VERSION_PATCH;
  resp[ idx++ ] = ( uint8_t )meta.active_slot;

  resp[ idx++ ] = meta.slot_info[ BL_SLOT_A ].valid;
  resp[ idx++ ] = meta.slot_info[ BL_SLOT_A ].version.major;
  resp[ idx++ ] = meta.slot_info[ BL_SLOT_A ].version.minor;
  resp[ idx++ ] = meta.slot_info[ BL_SLOT_A ].version.patch;

  resp[ idx++ ] = meta.slot_info[ BL_SLOT_B ].valid;
  resp[ idx++ ] = meta.slot_info[ BL_SLOT_B ].version.major;
  resp[ idx++ ] = meta.slot_info[ BL_SLOT_B ].version.minor;
  resp[ idx++ ] = meta.slot_info[ BL_SLOT_B ].version.patch;

  /* 진단용 추가 필드: 왜 특정 슬롯으로 점프하지 않고 대기 모드에 머무는지를
   * PC에서 원격으로 들여다볼 수 있게 한다.
   *   [13] state (0=CONFIRMED, 1=PENDING_TEST)
   *   [14] retry_count
   *   [15] SlotA에 실제로 점프 가능한 이미지가 있는지(0/1) - 메타데이터가
   *        아니라 Flash의 실제 벡터테이블을 직접 검사한 결과
   *   [16] SlotB에 실제로 점프 가능한 이미지가 있는지(0/1) */
  resp[ idx++ ] = ( uint8_t )meta.state;
  resp[ idx++ ] = ( uint8_t )meta.retry_count;
  resp[ idx++ ] = Bootloader_SlotHasValidImage( BL_SLOT_A ) ? 1U : 0U;
  resp[ idx++ ] = Bootloader_SlotHasValidImage( BL_SLOT_B ) ? 1U : 0U;

  SendResponse( BL_CMD_PING, resp, idx );
}

/* ============================================================
 * ERASE_SLOT(0x02) 처리
 * ------------------------------------------------------------
 * 요청 payload(5바이트): [target_slot:1][total_size:4]
 * target_slot에 해당하는 영역을 total_size만큼 커버하는 페이지 수만큼
 * 통째로 소거(0xFF로)한다. 성공하면 이후 WRITE_CHUNK를 받을 준비 상태로
 * 전환한다(s_erase_done=true 및 관련 상태 저장).
 * ============================================================ */
static void HandleEraseSlot( const uint8_t *payload, uint16_t len )
{
  uint8_t status = 1U; /* 기본값 FAIL, 성공 조건을 모두 통과해야 0(OK)로 바뀐다 */
  uint8_t resp[ 1 ];

  if( len == 5U )
  {
    BlSlotId_t target = ( BlSlotId_t )payload[ 0 ];
    uint32_t   total_size;

    ( void )memcpy( &total_size, &payload[ 1 ], 4U );

    /* target이 A/B 둘 중 하나인지, 크기가 0보다 크고 슬롯 용량(224KB)을
     * 넘지 않는지 확인 - 여기서 걸러야 잘못된 주소에 소거/쓰기가 일어나는
     * 사고를 막을 수 있다. */
    if( ( ( target == BL_SLOT_A ) || ( target == BL_SLOT_B ) )
        && ( total_size > 0U ) && ( total_size <= BL_SLOT_SIZE ) )
    {
      uint32_t base = ( target == BL_SLOT_A ) ? BL_SLOT_A_BASE : BL_SLOT_B_BASE;

      if( BootFlash_EraseRange( base, total_size ) )
      {
        s_target_slot      = target;
        s_target_base      = base;
        s_target_total_size = total_size;
        s_erase_done       = true;
        status = 0U;
      }
    }
  }

  resp[ 0 ] = status;
  SendResponse( BL_CMD_ERASE_SLOT, resp, 1U );
}

/* ============================================================
 * WRITE_CHUNK(0x03) 처리
 * ------------------------------------------------------------
 * 요청 payload: [offset:4][data:N] (N은 보통 256바이트, 마지막 청크만 더 짧을 수 있음)
 * offset은 슬롯 시작 주소로부터의 상대 오프셋이며, 반드시 8의 배수여야 한다
 * (Flash double-word 프로그래밍 제약, boot_flash.c 주석 참고).
 * 반드시 ERASE_SLOT 이후에만 호출 가능하다(그렇지 않으면 실패 응답).
 * ============================================================ */
static void HandleWriteChunk( const uint8_t *payload, uint16_t len )
{
  uint8_t status = 1U;
  uint8_t resp[ 1 ];

  if( ( len >= 4U ) && s_erase_done )
  {
    uint32_t      offset;
    uint32_t      data_len = ( uint32_t )( len - 4U );
    const uint8_t *data = &payload[ 4 ];

    ( void )memcpy( &offset, &payload[ 0 ], 4U );

    /* offset이 8의 배수인지, 그리고 이 청크를 다 쓰고 났을 때
     * ERASE_SLOT에서 약속한 total_size를 넘어서지 않는지 확인한다. */
    if( ( ( offset % 8U ) == 0U ) && ( ( offset + data_len ) <= s_target_total_size ) )
    {
      if( BootFlash_Write( s_target_base + offset, data, data_len ) )
      {
        status = 0U;
      }
    }
  }

  resp[ 0 ] = status;
  SendResponse( BL_CMD_WRITE_CHUNK, resp, 1U );
}

/* ============================================================
 * READ_FLASH(0x06) 처리 - 진단용
 * ------------------------------------------------------------
 * 요청 payload(6바이트): [addr:4][len:2] (len<=48)
 * addr에 있는 Flash 내용을 그대로 읽어 응답에 실어 보낸다. WRITE_CHUNK로
 * 실제로 기록된 바이트가 원본 파일과 정말 같은지, 디버거를 거치지 않고
 * USB로 직접 검증하기 위한 명령이다. 아무 Flash 주소나 읽을 수 있으므로
 * (슬롯 범위 체크 없음) 부트로더 자기 자신의 영역도 읽을 수 있다 - 진단
 * 목적으로만 쓰고 필요 없어지면 제거해도 된다.
 * ============================================================ */
#define READ_FLASH_MAX_LEN  (48U) /* WRITE_CHUNK와 동일하게 USB 패킷 1개(64바이트) 안에 응답이 들어가도록 제한 */

static void HandleReadFlash( const uint8_t *payload, uint16_t len )
{
  uint8_t resp[ 1U + READ_FLASH_MAX_LEN ];
  uint8_t status = 1U;
  uint16_t resp_len = 1U;

  if( len == 6U )
  {
    uint32_t addr;
    uint16_t read_len;

    ( void )memcpy( &addr, &payload[ 0 ], 4U );
    ( void )memcpy( &read_len, &payload[ 4 ], 2U );

    if( read_len <= READ_FLASH_MAX_LEN )
    {
      ( void )memcpy( &resp[ 1 ], ( const void * )( uintptr_t )addr, read_len );
      status = 0U;
      resp_len = ( uint16_t )( 1U + read_len );
    }
  }

  resp[ 0 ] = status;
  SendResponse( BL_CMD_READ_FLASH, resp, resp_len );
}

/* ============================================================
 * FINALIZE(0x04) 처리 - 업데이트의 핵심 "안전 장치"
 * ------------------------------------------------------------
 * 요청 payload(11바이트): [total_size:4][crc32:4][major:1][minor:1][patch:1]
 *
 * 여기서 하는 일이 이번 A/B 이중 슬롯 설계의 핵심이다:
 *  1) GUI가 USB로 보내면서 "이 이미지의 전체 CRC32는 이거야"라고 주장한 값과,
 *     실제로 Flash에 "물리적으로 기록된" 내용을 다시 읽어서(리드백) 계산한
 *     CRC32를 비교한다. USB 전송 중 오류나 Flash 프로그래밍 실패가 있었다면
 *     여기서 반드시 불일치가 나므로 잡아낼 수 있다.
 *  2) 일치하면 그제서야 메타데이터를 갱신한다:
 *       - 이 슬롯을 "유효(valid)"하다고 표시
 *       - active_slot을 이 슬롯으로 변경
 *       - state를 PENDING_TEST로 설정 (아직 "확정"은 아니고 "한번 시험 부팅
 *         해볼 대상"이라는 뜻 - 새 펌웨어가 정상적으로 초기화를 마치고
 *         스스로 Bootloader_ConfirmBootOk()를 호출해야 비로소 CONFIRMED가 된다.
 *         만약 새 펌웨어가 멈추거나 크래시하면, IWDG가 리셋시키고 다음 부팅 때
 *         부트로더가 "PENDING_TEST인데 확정이 안 됐네?"를 보고 자동으로
 *         이전 슬롯으로 롤백한다.)
 *  3) 이 모든 게 성공해야 GUI에게 OK를 돌려준다. 실패하면 기존에 실행 중이던
 *     슬롯은 전혀 건드리지 않았으므로 장치는 여전히 정상 동작한다.
 * ============================================================ */
static void HandleFinalize( const uint8_t *payload, uint16_t len )
{
  uint8_t status = 1U;
  uint8_t resp[ 1 ];

  if( ( len == 11U ) && s_erase_done )
  {
    uint32_t total_size;
    uint32_t expected_crc;
    uint8_t  major = payload[ 8 ];
    uint8_t  minor = payload[ 9 ];
    uint8_t  patch = payload[ 10 ];

    ( void )memcpy( &total_size, &payload[ 0 ], 4U );
    ( void )memcpy( &expected_crc, &payload[ 4 ], 4U );

    /* ERASE_SLOT에서 약속했던 크기와 실제로 FINALIZE에서 말하는 크기가
     * 같아야 한다(중간에 GUI가 다른 이미지로 바꿔치기하는 등의 혼선 방지). */
    if( total_size == s_target_total_size )
    {
      /* Flash는 메모리맵되어 있어 그냥 포인터로 읽으면 된다 - 이 시점에
       * WRITE_CHUNK로 실제 기록된 물리적 내용을 그대로 읽어오는 것이므로,
       * "GUI가 보냈다고 주장하는 내용"이 아니라 "실제로 저장된 내용"을
       * 검증하는 셈이다. */
      uint32_t actual_crc = BootShared_Crc32( ( const uint8_t * )( uintptr_t )s_target_base, total_size );

      if( actual_crc == expected_crc )
      {
        BlBootMetadata_t meta = BootMeta_Load();

        meta.slot_info[ s_target_slot ].size          = total_size;
        meta.slot_info[ s_target_slot ].crc32         = expected_crc;
        meta.slot_info[ s_target_slot ].version.major = major;
        meta.slot_info[ s_target_slot ].version.minor = minor;
        meta.slot_info[ s_target_slot ].version.patch = patch;
        meta.slot_info[ s_target_slot ].valid         = 1U;

        meta.active_slot = s_target_slot;
        meta.state       = BL_STATE_PENDING_TEST;
        meta.retry_count = 0U;

        if( BootMeta_Save( &meta ) )
        {
          status = 0U;
        }
      }
    }
  }

  /* 성공/실패 관계없이 이번 ERASE~FINALIZE 사이클은 끝났으므로 상태를 리셋해서
   * 다음 업데이트 시도(재시도 포함)에 대비한다. */
  s_erase_done = false;

  resp[ 0 ] = status;
  SendResponse( BL_CMD_FINALIZE, resp, 1U );
}

/* ============================================================
 * REBOOT(0x05) 처리
 * ------------------------------------------------------------
 * 응답을 먼저 보내고(그래야 GUI가 "명령을 받았다"는 걸 알 수 있다),
 * USB가 그 응답을 실제로 다 내보낼 시간을 잠깐 준 뒤 재부팅한다.
 * 이 함수는 리턴하지 않는다(재부팅되므로).
 * ============================================================ */
static void HandleReboot( void )
{
  uint8_t resp[ 1 ] = { 0U };

  SendResponse( BL_CMD_REBOOT, resp, 1U );
  HAL_Delay( 50U ); /* USB 응답이 실제로 호스트에 도달할 시간을 잠깐 벌어준다 */
  HAL_NVIC_SystemReset();
}

/* ============================================================
 * 완성된 프레임 하나를 CRC16 검증 후 커맨드별로 분기(dispatch)한다.
 * ============================================================ */
static void ProcessCompleteFrame( void )
{
  /* CRC16은 "CMD(1) + LEN(2) + PAYLOAD(len)"에 대해 계산된 값이어야 한다.
   * s_cmd/s_len/s_payload가 서로 다른 배열에 나뉘어 있으므로, 검증을 위해
   * 하나의 연속된 버퍼에 잠깐 다시 조립한다(전송 쪽 SendResponse와 대칭). */
  uint8_t  header_and_payload[ 3U + BL_FRAME_MAX_PAYLOAD ];
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
    /* CRC가 안 맞으면 USB 전송 중 잡음/충돌로 프레임이 깨졌다는 뜻이다.
     * 응답도 보내지 않고 그냥 조용히 버린다 - GUI 쪽에서 일정 시간 안에
     * 응답이 안 오면 알아서 재전송하도록 설계되어 있다(타임아웃+재시도는
     * PC GUI 쪽 책임). */
    return;
  }

  switch( ( BlCmd_t )s_cmd )
  {
    case BL_CMD_PING:
      HandlePing();
      break;

    case BL_CMD_ERASE_SLOT:
      HandleEraseSlot( s_payload, s_len );
      break;

    case BL_CMD_WRITE_CHUNK:
      HandleWriteChunk( s_payload, s_len );
      break;

    case BL_CMD_FINALIZE:
      HandleFinalize( s_payload, s_len );
      break;

    case BL_CMD_REBOOT:
      HandleReboot();
      break;

    case BL_CMD_READ_FLASH:
      HandleReadFlash( s_payload, s_len );
      break;

    case BL_CMD_ENTER_UPDATE:
    default:
      /* ENTER_UPDATE는 앱이 살아있을 때만 의미가 있는 커맨드다(재부팅 트리거).
       * 이미 부트로더에 상주한 상태에서 또 받으면 할 일이 없으므로 무시한다.
       * 정의되지 않은 커맨드 값도 그냥 무시한다(알 수 없는 명령 보호). */
      break;
  }
}

void BootProtocol_Init( void )
{
  BootRxBuf_Init();
  s_rx_state    = RX_WAIT_SOF0;
  s_erase_done  = false;
}

void BootProtocol_Process( void )
{
  uint8_t byte;

  /* 링버퍼에 쌓여있는 바이트를 있는 대로 모두 소비할 때까지 반복한다.
   * (한 번의 Process() 호출에서 여러 바이트, 심지어 여러 프레임을 한꺼번에
   *  처리할 수도 있다 - USB가 한 번에 여러 프레임 분량을 배달했을 경우.) */
  while( BootRxBuf_PopByte( &byte ) )
  {
    switch( s_rx_state )
    {
      case RX_WAIT_SOF0:
        if( byte == BL_FRAME_SOF0 )
        {
          s_rx_state = RX_WAIT_SOF1;
        }
        /* SOF0가 아니면 그냥 무시하고 계속 SOF0를 기다린다(잡음 무시). */
        break;

      case RX_WAIT_SOF1:
        if( byte == BL_FRAME_SOF1 )
        {
          s_rx_state = RX_WAIT_CMD;
        }
        else
        {
          /* SOF0 다음에 SOF1이 안 왔다 - 방금 그 SOF0는 진짜 프레임 시작이
           * 아니라 우연히 데이터 안에 낀 0xAA였을 수 있다. 처음부터 다시. */
          s_rx_state = RX_WAIT_SOF0;
        }
        break;

      case RX_WAIT_CMD:
        s_cmd = byte;
        s_rx_state = RX_WAIT_LEN0;
        break;

      case RX_WAIT_LEN0:
        s_len = ( uint16_t )byte; /* 리틀엔디안 하위 바이트 먼저 */
        s_rx_state = RX_WAIT_LEN1;
        break;

      case RX_WAIT_LEN1:
        s_len |= ( uint16_t )( ( uint16_t )byte << 8 );

        if( s_len > BL_FRAME_MAX_PAYLOAD )
        {
          /* 있을 수 없는 길이 값(버퍼보다 큰 payload) - 프레임이 아니라
           * 잡음으로 보고 즉시 재동기화한다. */
          s_rx_state = RX_WAIT_SOF0;
        }
        else
        {
          s_payload_index = 0U;
          /* payload 길이가 0인 커맨드(PING, REBOOT)는 바로 CRC 단계로 넘어간다. */
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

        /* 프레임 한 개가 완성됐다 - CRC 검증 후 커맨드 처리. */
        ProcessCompleteFrame();

        /* 다음 프레임을 기다리기 위해 상태를 리셋한다. */
        s_rx_state = RX_WAIT_SOF0;
        break;

      default:
        /* 도달할 수 없는 상태 - 방어적으로 리셋. */
        s_rx_state = RX_WAIT_SOF0;
        break;
    }
  }
}
