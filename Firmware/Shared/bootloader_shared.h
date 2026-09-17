#ifndef BOOTLOADER_SHARED_H
#define BOOTLOADER_SHARED_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* ===================== Flash 레이아웃 =====================
 * Bootloader 48KB / Slot A 224KB / Slot B 224KB / Metadata 4KB / 예비 12KB = 512KB
 * (최초 32KB로 시작했으나 USB Device 스택만으로 32KB를 초과해 48KB로 확장,
 *  Slot A/B 크기는 유지하고 시작 주소만 뒤로 밀림. 2026-07-06 변경.) */
#define BL_FLASH_BASE            (0x08000000UL)
#define BL_BOOTLOADER_BASE       (0x08000000UL)   /* 48KB */
#define BL_BOOTLOADER_SIZE       (48U  * 1024U)

#define BL_SLOT_A_BASE           (0x0800C000UL)   /* 224KB */
#define BL_SLOT_B_BASE           (0x08044000UL)   /* 224KB */
#define BL_SLOT_SIZE             (224U * 1024U)

#define BL_META_BASE             (0x0807C000UL)   /* 4KB, 2 page x 2KB ping-pong */
#define BL_META_PAGE_SIZE        (2U   * 1024U)
#define BL_META_PAGE0_BASE       (BL_META_BASE)
#define BL_META_PAGE1_BASE       (BL_META_BASE + BL_META_PAGE_SIZE)

/* ===================== 부트 메타데이터 ===================== */
#define BL_META_MAGIC            (0x424C4D44UL)  /* "BLMD" */

typedef enum
{
  BL_SLOT_A = 0,
  BL_SLOT_B = 1
} BlSlotId_t;

typedef enum
{
  BL_STATE_CONFIRMED   = 0,
  BL_STATE_PENDING_TEST = 1
} BlBootState_t;

typedef struct
{
  uint8_t  major;
  uint8_t  minor;
  uint8_t  patch;
  uint8_t  reserved;
} BlFwVersion_t;

typedef struct
{
  uint32_t      size;        /* 실제 이미지 크기(bytes) */
  uint32_t      crc32;       /* 이미지 전체 CRC32 */
  BlFwVersion_t version;
  uint8_t       valid;       /* 이 슬롯에 유효한 이미지가 있는지 */
  uint8_t       reserved[3];
} BlSlotInfo_t;

typedef struct
{
  uint32_t      magic;         /* BL_META_MAGIC, 유효성 검증용 */
  uint32_t      sequence;      /* ping-pong 중 최신본 판별용 증가 카운터 */
  BlSlotId_t    active_slot;   /* 현재(또는 다음) 부팅 대상 슬롯 */
  BlBootState_t state;         /* CONFIRMED / PENDING_TEST */
  uint32_t      retry_count;   /* PENDING_TEST 상태로 재부팅된 횟수 */
  BlSlotInfo_t  slot_info[2];  /* index = BlSlotId_t */
  uint32_t      crc32;         /* 이 구조체 자신의 CRC32 (magic~slot_info까지) */
} BlBootMetadata_t;

/* ===================== 앱->부트로더 재부팅 신호 (.noinit RAM) ===================== */
#define BL_NOINIT_MAGIC_ENTER_UPDATE  (0x5A5AC33CUL)

typedef struct
{
  uint32_t magic;   /* BL_NOINIT_MAGIC_ENTER_UPDATE 이면 업데이트 모드로 진입 요청 */
} BlNoinitFlag_t;

/* 앱/부트로더 링커스크립트에 아래와 동일한 이름의 .noinit 섹션을 정의하고
 * 이 구조체 변수를 그 섹션에 배치해야 한다(예: __attribute__((section(".noinit_boot_flag")))). */

/* ===================== USB CDC 업데이트 프레임 프로토콜 ===================== */
#define BL_FRAME_SOF0             (0xAAU)
#define BL_FRAME_SOF1             (0x55U)
#define BL_FRAME_CHUNK_SIZE       (256U)
/* WRITE_CHUNK payload = offset(4) + data(BL_FRAME_CHUNK_SIZE) 이므로 청크
 * 크기보다 최소 4바이트 더 커야 한다. 예전에 256으로 잘못 잡아서 WRITE_CHUNK
 * 프레임(260바이트)이 "있을 수 없는 길이"로 파서에서 거부당하는 버그가 있었다 -
 * 여유를 두고 8바이트 더 크게 잡는다. */
#define BL_FRAME_MAX_PAYLOAD      (BL_FRAME_CHUNK_SIZE + 8U)

typedef enum
{
  BL_CMD_ENTER_UPDATE = 0x00,  /* 앱 상주 중 인식하는 유일한 명령 */
  BL_CMD_PING         = 0x01,  /* 부트로더 버전/슬롯 정보 조회 */
  BL_CMD_ERASE_SLOT   = 0x02,
  BL_CMD_WRITE_CHUNK  = 0x03,
  BL_CMD_FINALIZE     = 0x04,
  BL_CMD_REBOOT       = 0x05,
  /* 진단용: [addr:4][len:2](len<=48)을 보내면 그 위치의 Flash 원본 바이트를
   * 그대로 돌려준다. WRITE_CHUNK로 실제로 어떤 내용이 기록됐는지 PC에서
   * 파일과 바이트 단위로 직접 대조하기 위한 명령. */
  BL_CMD_READ_FLASH   = 0x06
} BlCmd_t;

/* CRC32 헬퍼 (부트로더/앱 양쪽에서 동일 다항식 사용) */
uint32_t BootShared_Crc32( const uint8_t *data, uint32_t length );

/* CRC16 헬퍼 (프레임 검증용, Modbus CRC16과 동일 다항식 재사용 가능) */
uint16_t BootShared_Crc16( const uint8_t *data, uint32_t length );

#ifdef __cplusplus
}
#endif

#endif /* BOOTLOADER_SHARED_H */
