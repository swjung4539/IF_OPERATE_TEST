# Data Model

## 목적

주요 데이터 구조, 상태 값, 설정 값, 저장 형식, 메모리 배치를 ID 기반으로 누적 관리한다.

## 데이터 모델 목록

| ID | 분류 | 이름 | 상태 | 관련 요구사항 |
|---|---|---|---|---|
| DATA-001 | 구조체 / 객체 | UART channel runtime state | Confirmed | REQ-002 |
| DATA-002 | 테이블 / 맵 | 통합 메모리 맵 entry | Confirmed | REQ-007, REQ-010 |
| DATA-003 | 열거형 | Map table type | Confirmed | REQ-007 |
| DATA-004 | 테이블 / 맵 | GST Holding Register 영역 199~322 | Confirmed | REQ-011, REQ-012, REQ-013 |
| DATA-005 | 설정 값 | RAM 통신 설정값 | Confirmed | REQ-014 |
| DATA-006 | 상태 값 | 통신 상태와 실패 카운터 | Confirmed | REQ-012, REQ-015 |
| DATA-007 | 테이블 / 목록 | 포트별 poll list | Confirmed | REQ-005, REQ-014 |
| DATA-008 | 상태 값 | Pending write 상태 | Confirmed | REQ-009 |
| DATA-009 | 테이블 / 맵 | 외부통신프로토콜 사양서 static entry 범위 | Confirmed | REQ-010 |
| DATA-010 | 설정 값 | Active ID list | Confirmed | REQ-016 |
| DATA-011 | 테이블 / 목록 | 장치별 poll/write item | Confirmed | REQ-017 |
| DATA-012 | 테이블 / 맵 | 슬레이브별 통신 상태 레지스터 131~145 | Confirmed | REQ-015 |
| DATA-013 | 테이블 / 맵 | 런타임 Config Apply 레지스터 110~122 | Confirmed | REQ-020 |
| DATA-014 | 구조체 / 객체 | 부트 메타데이터(BlBootMetadata_t) | In Progress | REQ-021 |

## 구현 계획 참조

- 데이터 구조 구현 순서는 `07-implementation-plan.md`의 `IMP-002~IMP-004`를 따른다.
- `CommConfig_t`, `ActiveIdList_t`, `SharedMapEntry`, poll/write item table은 구현 전에 이 문서와 `07-implementation-plan.md`의 정의가 일치해야 한다.
- UART4 write 즉시 ACK와 하위 전달 best-effort 정책은 `IMP-003`, `IMP-006`, `DEC-017`에서 검증한다.

## 상세 데이터 모델

### DATA-001 - UART channel runtime state

- 분류: 구조체 / 객체
- 상태: Confirmed
- 목적: UART별 DMA/IDLE 수신 상태와 task 소비용 frame queue를 관리한다.
- 필드 / 값: DMA RX buffer, frame queue, frame length, queue head/tail/count.
- 타입: `uint8_t[]`, `uint16_t`, flag.
- 단위: byte, index.
- 기본값: 0 초기화.
- 저장 위치: UART 공통 수신 모듈의 static RAM.
- 관련 요구사항: REQ-002

### DATA-002 - 통합 메모리 맵 entry

- 분류: 테이블 / 맵
- 상태: Confirmed
- 목적: PLC와 하위 장치, GST 통신이 공유하는 값을 ID/영역/주소별로 저장한다.
- Key: `id + table_type + address`.
- 개념 구조:

```c
typedef struct {
    uint32_t last_update_ms;
    uint16_t address;
    uint16_t value;
    uint16_t pending_value;
    uint8_t id;
    uint8_t table;
    uint8_t valid : 1;
    uint8_t dirty : 1;
    uint8_t writable : 1;
    uint8_t readable : 1;
    uint8_t global : 1;
} SharedMapEntry;
```

- 필드 의미:
  - `id`: Modbus slave ID. `0~247` 허용. USART1/GST global entry는 ID를 사용하지 않는다.
  - `table`: Coil, Discrete Input, Input Register, Holding Register.
  - `address`: 외부통신프로토콜 사양서 주소 숫자.
  - `value`: 현재 확정값 또는 마지막 read 성공값.
  - `pending_value`: PLC write 요청 처리 중 커밋 전 임시값. 현재 UART4 write 경로는 즉시 커밋 후 PLC ACK를 전송한다.
  - `valid`: static entry 활성 여부.
  - `dirty`: 하위 장치로 전송해야 하는 write pending 여부.
  - `writable`, `readable`: PLC 접근 가능 방향.
  - `global`: USART1/GST처럼 Modbus slave ID와 무관하게 공유되는 entry 여부.
  - `last_update_ms`: 마지막 성공 갱신 시각.
- 기본값: 모든 value/pending/status는 0.
- 저장 위치: SharedMap entry pool과 관련 상태 변수는 RAM2 전용 section에 배치한다.
  - 대상: `s_entries`, `s_entry_count`, `s_initialized`, `s_current_time_ms`, `s_comm_failure_counts`
  - RAM2 주소 영역: `0x10000000`, 크기 `32 KB`
  - 현재 코드 기준 `SHARED_MAP_ENTRY_CAPACITY`는 `660` entries이며, `s_entries` 크기는 약 `0x2940` bytes다.
  - `SharedMap_Init()`에서 entry pool과 상태 값을 명시적으로 초기화하므로 RAM2 section은 `NOLOAD`로 배치할 수 있다.
- 생성 정책: 외부통신프로토콜 사양서에 있는 주소만 static entry로 미리 생성한다. 런타임 자동 생성은 하지 않는다.
- 미정의 key 처리: 응답 없음.
- 관련 요구사항: REQ-007, REQ-010

### DATA-003 - Map table type

- 분류: 열거형
- 상태: Confirmed
- 목적: 같은 주소 숫자라도 Modbus 영역별로 entry를 분리한다.
- 값:

```c
typedef enum {
    MAP_COIL,
    MAP_DISCRETE_INPUT,
    MAP_INPUT_REGISTER,
    MAP_HOLDING_REGISTER,
} MapTableType;
```

- 영역 기준:
  - Coil: `1`번부터 사용, bit 데이터.
  - Discrete Input: `1`번부터 사용, bit 데이터.
  - Input Register: `30000`번부터 사용, 16-bit 데이터.
  - Holding Register: `40000`번 영역 표기는 영역 설명용이다. 실제 address 값은 사양서 숫자를 그대로 사용한다.
- 관련 요구사항: REQ-007

### DATA-004 - GST Holding Register 영역 199~322

- 분류: 테이블 / 맵
- 상태: Confirmed
- 목적: USART1 GST 통신과 PLC-visible Holding Register 영역을 연결한다.
- 주소:
  - `199`: 통신 상태 heartbeat bit flag.
  - `200`: PLC-UART4 heartbeat counter.
  - `201~241`: GST write 영역, 41개 register.
  - `242~282`: GST read 영역, 41개 register.
  - `283~322`: 예약 영역, read/write 허용, 기본값 0.
- 동기화 주체:
  - `USART1_ProtocolTask`는 통합 map `201~241` 값을 읽어 GST peer로 송신할 frame 데이터를 만든다.
  - `USART1_ProtocolTask`는 GST peer에서 수신한 값을 통합 map `242~282`에 반영한다.
  - `UART4_Task`는 USART1 GST frame 송수신을 수행하지 않고, PLC가 접근하는 Modbus slave 인터페이스만 제공한다.
- `199` bit 정의:
  - bit 0: DIO 통신 정상 여부.
  - bit 1: Heating Jacket 통신 정상 여부.
  - bit 2: Gas Detector 통신 정상 여부.
  - bit 3: USART1 GST 통신 정상 여부.
  - `1`: 정상, `0`: 비정상.
- `200` counter 정의:
  - UART4 정상 통신마다 1 증가.
  - 범위는 `0~65535`.
  - `65535` 초과 시 `0`으로 rollover.
- 기본값: 모든 register 0.
- 관련 요구사항: REQ-011, REQ-012, REQ-013

### DATA-005 - RAM 통신 설정값

- 분류: 설정 값
- 상태: Confirmed
- 목적: 통신 주기, TX/response timeout, retry count, 실제 사용 ID 목록을 `UART4_Task`, `LPUART1_Task`, `USART2_Task`, `USART3_Task`, USART1 GST task가 실행 중 참조한다.
- 개념 구조:

```c
typedef struct {
    uint8_t ids[10];
    uint8_t count;
} ActiveIdList_t;

typedef struct {
    uint8_t types[3];   /* MAX_ACTIVE_GAS_ID_COUNT, 0=AUTO, 1=H2, 2=H2_TFT */
} GasTypeList_t;

typedef struct {
    uint16_t lpuart1_poll_period_ms;
    uint16_t usart2_poll_period_ms;
    uint16_t usart3_poll_period_ms;
    uint16_t usart1_period_ms;
    uint16_t tx_timeout_ms;
    uint16_t lpuart1_response_timeout_ms;
    uint16_t usart2_response_timeout_ms;
    uint16_t usart3_response_timeout_ms;
    uint8_t retry_count;

    ActiveIdList_t dio_ids;
    ActiveIdList_t heating_ids;
    ActiveIdList_t gas_ids;
    GasTypeList_t gas_types;
    uint32_t revision;
} CommConfig_t;
```

- `gas_types`: Gas active ID 슬롯별 구성 profile 힌트(`0=AUTO`, `1=H2`, `2=H2_TFT`). 기본값은 3슬롯 모두 `AUTO`이며, `AUTO`인 경우 실제 profile은 Discovery(`ProbeGasType`)가 판별한다.
- `revision`: `CommConfig_ApplyDeviceSettings()`가 active ID/gas type 변경을 성공적으로 반영할 때마다 1 증가한다. 하위 포트 task는 이 값의 변경을 감지해 `ReinitXxxContext()` + `ModbusMaster_DiscoverSlaves()`를 재실행한다(DATA-013 참고).

- compile-time 기본값:

```c
#define DEFAULT_LPUART1_POLL_PERIOD_MS  50
#define DEFAULT_USART2_POLL_PERIOD_MS   50
#define DEFAULT_USART3_POLL_PERIOD_MS   50
#define DEFAULT_USART1_PERIOD_MS        50
#define DEFAULT_TX_TIMEOUT_MS           50
#define DEFAULT_LPUART1_RESPONSE_TIMEOUT_MS 50
#define DEFAULT_USART2_RESPONSE_TIMEOUT_MS  200
#define DEFAULT_USART3_RESPONSE_TIMEOUT_MS  100
#define DEFAULT_RETRY_COUNT             3
#define MAX_ACTIVE_ID_COUNT             10
#define MAX_ACTIVE_DIO_ID_COUNT         2
#define MAX_ACTIVE_HEATING_ID_COUNT     3
#define MAX_ACTIVE_GAS_ID_COUNT         3
```

- 저장 위치: RAM.
- 초기화: 부팅 시 compile-time 기본값을 RAM config에 복사한다.
- active ID 정책: `LPUART1_Task`, `USART2_Task`, `USART3_Task`와 `UART4_Task`는 RAM config의 active ID list에 포함된 ID만 처리한다.
- 기본 active ID (compile-time 기본값, `kCommConfigDefaults`):
  - DIO: `{1, 2}`.
  - Heating Jacket: `{20, 21, 22}`.
  - Gas Detector: `{31, 40, 30}`.
  - Gas Detector profile 기본값은 3슬롯 모두 `AUTO`이며, 실제 profile은 Discovery 결과(`ProbeGasType`)로 H2 Detector 또는 H2 Detector(TFT)로 판별한다.
  - USART1/GST: ID 없음. GST 영역은 ID 없는 global map으로 처리한다.
- 런타임 변경: `110~120` 주소에 새 값을 쓴 뒤 `121`번지에 `1`을 write하면 반영을 시도한다(DATA-013 참고). `121`이 `0`이면 반영 트리거가 아니라 단순 holding register 값으로만 취급되어 아무 효과가 없다.
- 제약 사항: Flash(비휘발성) 저장은 제공하지 않는다. 재부팅 시 항상 compile-time 기본값에서 다시 시작한다. PLC register 기반 런타임 설정 변경 자체는 DATA-013의 Config Apply 메커니즘으로 제공한다.
- 관련 요구사항: REQ-014, REQ-016, REQ-020

### DATA-006 - 통신 상태와 실패 카운터

- 분류: 상태 값
- 상태: Confirmed
- 목적: `199` heartbeat bit flag를 갱신하기 위한 상태를 관리한다.
- 대상:
  - DIO: `199` bit 0.
  - Heating Jacket: `199` bit 1.
  - Gas Detector: `199` bit 2.
  - USART1 GST: `199` bit 3.
- DIO/Heating/Gas (다중 슬레이브 그룹): 그룹 비트는 DATA-012 슬레이브별 상태 레지스터(131~145)를 AND 조합해서 재계산한다.
  - Active ID로 구성(configured)된 슬레이브가 전부 온라인이어야 그룹 비트가 1이 된다.
  - 구성된 슬레이브 중 하나라도 오프라인이면(연속 3회 실패) 그룹 비트는 계속 0을 유지한다. 구성되지 않은 슬롯은 검사 대상에서 제외한다.
  - PLC 포워딩 write 성공/실패는 그룹 비트에 반영하지 않는다(폴링 결과만 반영, best-effort 1회성 write의 노이즈 방지).
  - 재계산 시점: 슬레이브 폴링 결과로 `SharedMap_SetSlaveStatus()`가 호출될 때마다 자동 재계산(`RecomputeGroupCommBit()`, `a_shared_map.c`).
- USART1 GST (단일 디바이스, 슬레이브 인덱스 개념 없음): 기존 방식 그대로 `SharedMap_SetCommStatus()`가 직접 관리.
  - 실패 기준: 연속 3회 timeout/fail이면 비정상.
  - 복구 기준: 1회 성공하면 정상.
- 기본값: 전원 인가 직후 0.
- 관련 요구사항: REQ-012, REQ-015

### DATA-007 - 포트별 poll list

- 분류: 테이블 / 목록
- 상태: Confirmed
- 목적: 각 통신 task가 순차 처리할 read/write 항목을 정의한다.
- 포트:
  - `LPUART1_Task`: DIO poll/write list.
  - `USART2_Task`: Heating Jacket poll/write list.
  - `USART3_Task`: Gas Detector poll/write list.
  - `USART1_ProtocolTask`: GST 송수신 항목.
- USART1 queue:
  - `USART1_RxTask`는 수신 frame을 GST 수신 queue에 넣는다.
  - `USART1_ProtocolTask`는 GST 수신 queue를 소비하고 GST 송신 queue를 생산한다.
  - `USART1_TxTask`는 GST 송신 queue를 소비한다.
- 처리 방식: polling period마다 전체 map을 모두 처리하지 않고, poll list의 다음 항목 또는 가능한 항목을 처리한다.
- ID 처리: active_id_list에 등록된 실제 사용 ID만 순차 처리한다.
- 순서: 장치 ID 순서와 각 ID 안의 poll item 순서는 외부통신프로토콜 사양서의 장치별 주소 표 순서를 따른다.
- 우선순위: pending write가 있으면 read poll item보다 먼저 처리한다.
- 전체 갱신 시간: poll item 수, baudrate, timeout, retry count에 따라 달라진다.
- register chunk 크기: `05-protocol.md`의 `PROTO-008` 및 코드의 `a_poll_table.c` 정적 poll/write item 수량을 기준으로 확정한다.
- 관련 요구사항: REQ-005, REQ-014

### DATA-008 - Pending write 상태

- 분류: 상태 값
- 상태: Confirmed
- 목적: PLC write 요청의 커밋 전 임시값과 하위 장치 비동기 전달 대상 값을 관리한다.
- 흐름:
  - PLC write 요청 수신.
  - map entry에 `pending_value` 저장.
  - `dirty` set.
  - `UART4_Task`가 즉시 `value = pending_value`, `dirty` clear 처리 후 PLC ACK 전송.
  - 대상 통신 task 또는 GST task가 하위 장치 전달을 best-effort로 수행.
  - 하위 전달 실패는 PLC ACK를 되돌리지 않는다.
- 실패 정책: desired/actual 값을 별도로 보존하지 않는다.
- 관련 요구사항: REQ-009

### DATA-009 - 외부통신프로토콜 사양서 static entry 범위

- 분류: 테이블 / 맵
- 상태: Confirmed
- 목적: 통합 map이 지원해야 하는 외부 사양서 주소 template 범위를 정의한다.
- 범위:

| 장치군 | 포트 | ID 범위 | Table type | 주소 |
|---|---|---:|---|---|
| DIO | LPUART1 | active ID `{1, 2}` | Holding Register | `129~130` |
| USART1/GST | USART1 | ID 없음 / global | Holding Register | `199~322` |
| Heating Jacket | USART2 | active ID `{20, 21, 22}` | Input Register | `1000~1035` |
| Heating Jacket | USART2 | active ID `{20, 21, 22}` | Holding Register | `2000~2020` |
| Gas Detector | USART3 | active ID `{31, 40, 30}` | Coil / Discrete / Register | H2 + H2_TFT 합집합 profile 기준 |

- 생성 정책: 위 주소 범위는 static address template으로 정의하고, 응답/polling은 active ID에 대해서만 허용한다.
- 기본값: 모든 entry의 초기값은 0.
- 접근 정책: 외부 사양서의 R/W 방향과 table type에 따라 read/write 가능 여부를 설정한다.
- 관련 요구사항: REQ-010

### DATA-010 - Active ID list

- 분류: 설정 값
- 상태: Confirmed
- 목적: 실제 사용 ID만 polling하고 PLC 요청에도 응답하도록 제한한다.
- 개념 구조:

```c
typedef struct {
    uint8_t ids[10];
    uint8_t count;
} ActiveIdList_t;
```

- 목록:
  - `dio_ids`: DIO active ID. 유효 범위 `0~247`, 기본값 `{1, 2}`.
  - `heating_ids`: Heating Jacket active ID. 유효 범위 `0~247`, 기본값 `{20, 21, 22}`.
  - `gas_ids`: Gas Detector active ID. 유효 범위 `0~247`, 기본값 `{31, 40, 30}`.
  - `gas_profile`: active ID별 profile은 Discovery 결과에 따라 H2 Detector 또는 H2 Detector(TFT)로 판별한다.
- 정책:
  - active_id_list에 없는 ID는 허용 범위 안이어도 polling하지 않는다.
  - active_id_list에 없는 ID의 PLC read/write 요청은 응답하지 않는다.
  - `248~255`는 active_id_list에 등록할 수 없다.
  - ID `0`은 일반 ID로 등록할 수 있다.
- 저장 위치: RAM config.
- 초기화: compile-time 기본 active list를 부팅 시 RAM config에 복사한다.
- 관련 요구사항: REQ-016

### DATA-011 - 장치별 poll/write item

- 분류: 테이블 / 목록
- 상태: Confirmed
- 목적: 외부통신프로토콜 사양서의 장치별 주소 표를 `LPUART1_Task`, `USART2_Task`, `USART3_Task`의 poll/write scheduler 입력으로 사용한다.
- 구성:
  - port: LPUART1, USART2, USART3.
  - device profile: DIO, Heating Jacket, H2 Detector, H2 Detector(TFT).
  - active ID list: 해당 profile을 적용할 ID 목록.
  - function code: FC01, FC02, FC03, FC04, FC05, FC06, FC0F, FC10.
  - table type: Coil, Discrete Input, Input Register, Holding Register.
  - address range: 사양서의 주소 또는 연속 block.
  - direction: Poll Read, Pending Write, Readback.
- 처리 정책:
  - pending write item은 poll read item보다 우선한다.
  - poll read item은 active ID별로 사양서의 장치별 표 순서를 따른다.
  - R/W item은 pending write 대상으로 사용하고, readback이 필요한 경우 poll read item에도 포함한다.
- 상세 표: `05-protocol.md`의 `PROTO-008`을 기준으로 한다.
- 관련 요구사항: REQ-017

### DATA-012 - 슬레이브별 통신 상태 레지스터 131~145

- 분류: 테이블 / 맵
- 상태: Confirmed
- 목적: 포트별 슬레이브 통신 상태를 PLC가 FC03으로 읽을 수 있도록 제공한다.
- 주소 배치:
  | 주소 | 내용 |
  |------|------|
  | 131~135 | DIO 슬레이브 인덱스 0~4 (LPUART1) |
  | 136~140 | HEATING 슬레이브 인덱스 0~4 (USART2) |
  | 141~145 | GAS 슬레이브 인덱스 0~4 (USART3) |
- 레지스터 값 형식:
  - 상위 바이트: 온라인 여부 (0x01 = 온라인, 0x00 = 오프라인)
  - 하위 바이트: 연속 실패 횟수 (0~255)
  - 연속 3회 실패 시 오프라인 전환 (`COMM_STATUS_FAILURE_THRESHOLD = 3`)
- 갱신 주체: `LPUART1_Task`, `USART2_Task`, `USART3_Task` 폴링 완료 시 자동 갱신
- 접근: 읽기 전용 (PLC FC03)
- 관련 요구사항: REQ-015

### DATA-013 - 런타임 Config Apply 레지스터 110~122

- 분류: 테이블 / 맵
- 상태: Confirmed
- 목적: PLC가 재컴파일/재부팅 없이 DIO/Heating/Gas active ID와 Gas profile 힌트를 런타임에 변경할 수 있도록 holding register 인터페이스를 제공한다.
- 주소 배치 (`a_application.c`의 `CONFIG_*_START_ADDR`/`CONFIG_APPLY_*_ADDR` 기준):
  | 주소 | 내용 |
  |------|------|
  | 110~111 | DIO active ID 슬롯 0~1 |
  | 112~114 | Heating active ID 슬롯 0~2 |
  | 115~117 | Gas active ID 슬롯 0~2 |
  | 118~120 | Gas profile 힌트 슬롯 0~2 (`0=AUTO`, `1=H2`, `2=H2_TFT`) |
  | 121 | apply trigger (읽기/쓰기). PLC Slave ID `8` Write group addr `317`과 동일 레지스터 |
  | 122 | apply 결과 코드 (읽기 전용 성격) |
- 동작 순서 (`ServiceConfigApply()`, `UART4_Task` 루프에서 매 tick 수행):
  1. `121`(trigger)을 읽는다. `1`이 아니면 아무 동작도 하지 않는다.
  2. `1`이면 `110~120`의 현재 값을 읽어 `CommConfig_ApplyDeviceSettings(dio_ids, heating_ids, gas_ids, gas_types)`를 호출한다.
  3. 검증(ID는 `0` 또는 `1~247`, 그룹 내 `0`이 아닌 ID 중복 불허, gas profile 값은 `0~2` 범위)을 통과하면 active ID list를 갱신하고 `CommConfig_t.revision`을 1 증가시킨다. 실패하면 기존 active list는 변경되지 않는다.
  4. 결과 코드를 `122`에 쓴다: `0=IDLE`, `1=OK`, `2=INVALID_ID`, `3=DUPLICATE_ID`, `4=INVALID_GAS_TYPE`, `5=INTERNAL_ERROR`.
  5. `121`을 `0`으로 clear한다.
- 후속 동작: `revision`이 바뀌면 해당 하위 포트 task가 `ReinitXxxContext()` + `ModbusMaster_DiscoverSlaves()`를 재실행해 새 active ID 기준으로 재-Discovery한다.
- 저장 위치: RAM(`s_comm_config`)뿐이며 Flash에는 저장하지 않는다. 전원 재인가 시 compile-time 기본값(DATA-005)으로 되돌아간다.
- 관련 요구사항: REQ-014, REQ-020

### DATA-014 - 부트 메타데이터(BlBootMetadata_t)

- 분류: 구조체 / 객체
- 상태: In Progress (코드 작성/개별 빌드 확인 완료, 실 보드 검증 전)
- 목적: 부트로더가 A/B 두 슬롯 중 어느 쪽을 실행할지, 각 슬롯의 유효성/버전, 방금 올라온 새 펌웨어가 "시험 부팅(PENDING_TEST)" 중인지 "확정(CONFIRMED)"됐는지를 Flash에 영속적으로 기록한다.
- 정의 위치: `Shared/bootloader_shared.h`(부트로더/앱 공용, `BootMeta_Load()`(부트로더)/`Bootloader_ConfirmBootOk()`(앱)이 각각 사용).
- 개념 구조:

```c
typedef struct {
    uint8_t  major;
    uint8_t  minor;
    uint8_t  patch;
    uint8_t  reserved;
} BlFwVersion_t;

typedef struct {
    uint32_t      size;
    uint32_t      crc32;
    BlFwVersion_t version;
    uint8_t       valid;
    uint8_t       reserved[3];
} BlSlotInfo_t;

typedef struct {
    uint32_t      magic;
    uint32_t      sequence;
    BlSlotId_t    active_slot;   /* 0=A, 1=B */
    BlBootState_t state;         /* 0=CONFIRMED, 1=PENDING_TEST */
    uint32_t      retry_count;
    BlSlotInfo_t  slot_info[2];  /* index = BlSlotId_t */
    uint32_t      crc32;         /* 이 구조체 자신에 대한 CRC32(magic~slot_info) */
} BlBootMetadata_t;
```

- 저장 위치: Flash `0x0807C000~0x0807E000`(2KB 페이지 2개, ping-pong). 두 페이지 중 `magic`과 자체 `crc32`가 모두 유효하면서 `sequence`가 더 큰 쪽을 "현재값"으로 취급한다 - 한쪽 페이지가 쓰다가 정전으로 손상돼도 반대쪽의 직전 값이 항상 유효하게 남아있다.
- 쓰기 주체:
  - 부트로더(`boot_meta.c`, `boot_protocol.c`의 `HandleFinalize`): 새 이미지 CRC32 검증 통과 시 `active_slot`을 해당 슬롯으로, `state`를 `PENDING_TEST`로 갱신.
  - 부트로더(부팅 시 롤백 판단): `state==PENDING_TEST`가 확정되지 못한 채 재부팅되면 반대 슬롯으로 `active_slot`을 되돌리고 `state=CONFIRMED`로 갱신.
  - 앱(`a_bootloader_link.c`의 `Bootloader_ConfirmBootOk()`): `Application_Init()` 성공 직후, 현재 슬롯이 `PENDING_TEST`이면 `CONFIRMED`로 전환.
- 기본값(유효 레코드가 전혀 없는 경우, 예: 공장 초기 상태로 부트로더 없이 ST-Link로 앱만 직접 플래싱한 경우): `active_slot=SlotA`, `state=CONFIRMED`.
- 관련 요구사항: REQ-021

## 미결정 사항

- 없음

## 변경 이력

- 2026-06-26: `interface_test/interface_main` 소스에서 데이터 모델 초안 추출.
- 2026-06-27: 통합 map entry, ID 정책, GST 영역, RAM config, polling/write 상태 반영.
- 2026-06-27: Active ID list와 순차 poll 정책을 데이터 모델에 반영.
- 2026-06-27: DIO `1~3`, Heating `1~7`, Gas `20~29`/`40~49` active ID와 poll/write item 데이터 모델 반영.
- 2026-06-27: USART1/GST global map과 write 실패 시 pending 폐기 정책 반영.
- 2026-06-27: `07-implementation-plan.md` 구현 단계 참조 반영.
- 2026-06-27: 통신 task 이름과 USART1 GST queue 기반 3-task 구조를 데이터 모델에 반영.
- 2026-06-27: USART1/GST 공용 영역의 통합 map 동기화 주체를 USART1 GST task로 명확화.
- 2026-06-28: UART frame queue, Confirmed poll/write list, pending write 상태, timeout/fail 실패 기준을 현재 코드 기준으로 갱신.
- 2026-06-28: 슬레이브별 통신 상태 레지스터 131~145 (DATA-012) 추가.
- 2026-06-30: 현재 코드 기준으로 CommConfig 필드, active ID 기본값, SharedMap 660-entry 동적 등록 구조, 즉시 ACK write 흐름을 갱신.
- 2026-06-30: SharedMap entry pool과 관련 상태 변수의 RAM2 전용 section 배치 정책을 반영.
- 2026-07-06: 현재 코드 기준으로 `CommConfig_t`에 `gas_types`/`revision` 필드 추가 반영. Gas Detector 기본 active ID를 `{31, 40, 30}`으로 정정(오기 수정). 코드에 이미 구현되어 있던 런타임 Config Apply(HR110~122) 메커니즘을 DATA-013으로 신규 반영하고, DATA-005의 "PLC register 설정 변경 제외" 제약 사항 문구를 정정.
- 2026-07-06: DATA-014 추가. USB 부트로더 A/B 이중 슬롯의 부트 메타데이터(`BlBootMetadata_t`) 구조/저장 위치/ping-pong 정책 반영, 실 보드 검증 전.
- 2026-08-03: DATA-006 갱신. DIO/Heating/Gas 그룹의 `199` comm bit를 폴링 결과 직접 세팅에서 슬레이브별 상태(DATA-012) AND 조합 재계산 방식으로 변경 반영(구성된 슬레이브 중 하나라도 오프라인이면 그룹 비트 0 유지, PLC write 포워딩 결과는 제외). USART1 GST는 변경 없음.
