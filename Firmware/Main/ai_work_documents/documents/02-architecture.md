# Architecture

## 목적

시스템 구성, 주요 모듈, 책임 분리, 데이터 흐름, 외부 의존성을 ID 기반으로 누적 관리한다.

## 아키텍처 항목 목록

| ID | 분류 | 제목 | 상태 | 관련 요구사항 |
|---|---|---|---|---|
| ARCH-001 | 시스템 구성 | STM32L452 FreeRTOS 통신 애플리케이션 구조 | Confirmed | REQ-001, REQ-002 |
| ARCH-002 | 모듈 구조 | 공통 UART DMA/IDLE 수신 계층 | Confirmed | REQ-002 |
| ARCH-003 | 데이터 흐름 | UART4 PLC Modbus Slave와 통합 map 처리 | Confirmed | REQ-003, REQ-007, REQ-008, REQ-009 |
| ARCH-004 | 데이터 흐름 | 하위 Modbus Master task와 map 동기화 | Confirmed | REQ-005, REQ-014, REQ-015 |
| ARCH-005 | 데이터 흐름 | USART1 GST 3-task 구조 | Confirmed | REQ-006, REQ-011, REQ-012 |
| ARCH-006 | 모듈 구조 | 통합 메모리 맵, active ID list, 설정 RAM config | Confirmed | REQ-007, REQ-010, REQ-014, REQ-016 |
| ARCH-007 | 폐기 | Transparent bridge hub 구조 폐기 | Confirmed | REQ-004 |
| ARCH-008 | 모듈 구조 | USB 부트로더 + A/B 이중 슬롯 펌웨어 업데이트 구조 | Done | REQ-021 |

## 구현 계획 참조

- 아키텍처 구현 순서는 `07-implementation-plan.md`의 `IMP-001~IMP-010`을 따른다.
- 모듈 추가 순서는 `a_comm_config`, `a_shared_map`, poll/write table, `UART4_Task`, 하위 `LPUART1_Task`/`USART2_Task`/`USART3_Task`, USART1 GST 3-task 연동 순서다.
- SDD/subagent를 사용할 경우 각 구현 단계 후 별도 리뷰 단계를 거친다.

## 상세 아키텍처

### ARCH-001 - STM32L452 FreeRTOS 통신 애플리케이션 구조

- 분류: 시스템 구성
- 상태: Confirmed
- 관련 요구사항: REQ-001, REQ-002
- 설명: CubeMX 초기화 코드 위에서 사용자 통신 모듈과 FreeRTOS task가 실행된다.
- 구성 요소: CubeMX peripheral init, `a_uart`, `UART4_Task`, `LPUART1_Task`, `USART2_Task`, `USART3_Task`, `USART1_RxTask`, `USART1_ProtocolTask`, `USART1_TxTask`, 통합 map module.
- 책임: CubeMX 영역은 주변장치 초기화, User/Application 영역은 통신 프로토콜, map 갱신, task 스케줄링 담당.
- 데이터 흐름: HAL 초기화 -> 사용자 통신 초기화 -> 통합 map 초기화 -> RAM config 기본값 로드 -> FreeRTOS task 실행.
- 외부 의존성: STM32 HAL, CMSIS-RTOS2, FreeRTOS, STM32Cube FW_L4.
- 통신 task 구성:
  - `UART4_Task`: PLC 요청을 수신하고 통합 메모리 맵 기준으로 read 응답 또는 write pending을 생성한다.
  - `LPUART1_Task`: LPUART1 하위 장치와 주기적으로 송수신하며 하위 장치 데이터와 통합 메모리 맵을 동기화한다.
  - `USART2_Task`: USART2 하위 장치와 주기적으로 송수신하며 하위 장치 데이터와 통합 메모리 맵을 동기화한다.
  - `USART3_Task`: USART3 하위 장치와 주기적으로 송수신하며 하위 장치 데이터와 통합 메모리 맵을 동기화한다.
  - `USART1_RxTask`, `USART1_ProtocolTask`, `USART1_TxTask`: USART1 GST 전용 protocol endpoint를 구성한다.

### ARCH-002 - 공통 UART DMA/IDLE 수신 계층

- 분류: 모듈 구조
- 상태: Confirmed
- 관련 요구사항: REQ-002
- 설명: LPUART1, USART1, USART2, USART3, UART4는 공통 UART 수신 계층을 사용한다.
- 구성 요소: UART별 DMA buffer, frame queue, RX event callback, error callback.
- 책임: DMA/IDLE 수신 시작, IDLE 이벤트 기반 frame 감지, 통신 task에 수신 데이터 제공, UART 오류 복구.
- 데이터 흐름: UART DMA buffer -> RX event callback -> UART별 frame queue -> 통신 task.
- 제약 사항: 수신 버퍼 크기와 task 처리 지연이 frame drop 여부에 영향을 준다.

### ARCH-003 - UART4 PLC Modbus Slave와 통합 map 처리

- 분류: 데이터 흐름
- 상태: Confirmed
- 관련 요구사항: REQ-003, REQ-007, REQ-008, REQ-009
- 설명: `UART4_Task`는 PLC용 Modbus RTU slave로 동작하며, 요청을 통합 map 기준으로 처리한다.
- 구성 요소: `UART4_Task`, Modbus RTU parser, map lookup, map read response builder, write request dispatcher.
- 책임:
  - PLC request의 ID, function code, address, quantity 검증.
  - GST 공용 영역 `199~322`가 아닌 요청은 대상 장치군의 active_id_list에 ID가 포함되는지 확인.
  - GST 공용 영역 `199~322` 요청은 slave ID와 무관하게 ID 없는 global map entry를 사용한다.
  - `UART4_Task`는 GST 공용 영역 요청을 USART1로 Modbus frame forwarding하지 않는다.
  - USART1 GST peer와의 실제 송수신, GST read/write 영역의 통합 map 동기화는 `USART1_ProtocolTask`가 담당한다.
  - `id + table_type + address` 기준 map entry 조회.
  - read 요청은 현재 map 값으로 즉시 응답.
  - write 요청은 SharedMap에 즉시 커밋하고 PLC에 즉시 ACK한 뒤, 하위 장치 또는 GST peer 전달은 별도 task에서 best-effort로 처리한다.
  - map entry가 없거나 허용되지 않거나 active 상태가 아닌 ID이면 응답하지 않음.
- 데이터 흐름:
  - Read: PLC -> UART4 -> map lookup -> map value response -> PLC.
  - Write: PLC -> UART4 -> SharedMap 즉시 커밋 -> PLC ACK -> 대상 task/GST peer 비동기 전달.
- 제약 사항: PLC write ACK는 하위 장치의 실제 반영 성공을 보장하지 않는다.

### ARCH-004 - 하위 Modbus Master task와 map 동기화

- 분류: 데이터 흐름
- 상태: Confirmed
- 관련 요구사항: REQ-005, REQ-014, REQ-015
- 설명: `LPUART1_Task`, `USART2_Task`, `USART3_Task`는 각각 하위 장치용 Modbus RTU master 동작을 수행한다.
- 포트 책임:
  - `LPUART1_Task`: DIO 장치, active ID `{1, 2}`, RS485.
  - `USART2_Task`: Heating Jacket 장치, active ID `{20, 21, 22}`, RS485.
  - `USART3_Task`: Gas Detector 장치, active ID `{31, 40, 30}`, RS485.
- 데이터 흐름:
  - Periodic read: 하위 task -> 하위 장치 read -> 성공 시 통합 map actual 값 갱신.
  - Pending write: UART4 write request -> SharedMap 즉시 커밋/ACK -> 대상 하위 task가 tx_queue write를 비동기 처리.
- 스케줄링: 각 포트는 RAM config의 polling period를 사용한다. 기본값은 모두 50 ms.
- 제약 사항: polling period는 전체 map 갱신 주기가 아니라 poll list의 다음 항목을 처리하기 위한 최소 시작 간격이다.
- 순차 처리 정책:
  - pending write가 있으면 read polling보다 우선 처리한다.
  - pending write가 없으면 포트별 active_id_list의 현재 ID를 선택한다.
  - 선택한 ID에서 외부통신프로토콜 사양서의 장치별 주소 표 순서대로 poll item을 처리한다.
  - poll item과 active ID index는 끝까지 처리하면 처음으로 rollover한다.
- ID 범위: 허용 ID 범위 전체를 polling하지 않고 실제 사용 ID만 polling한다.

### ARCH-005 - USART1 GST 3-task 구조

- 분류: 데이터 흐름
- 상태: Confirmed
- 관련 요구사항: REQ-006, REQ-011, REQ-012
- 설명: USART1은 RS422 GST 사용자 프로토콜만 수행하며 Modbus master/slave로 동작하지 않는다.
- 구성 요소: `USART1_RxTask`, `USART1_ProtocolTask`, `USART1_TxTask`, GST 수신 queue, GST 송신 queue, GST frame parser, GST TX builder, 통합 map GST 영역 adapter.
- 책임:
  - `USART1_RxTask`: USART1 수신 데이터를 확인하고 수신 frame을 GST 수신 queue에 넣는다. queue가 가득 찬 경우 오래된 frame을 버리고 최신 frame 기준으로 재동기화한다.
  - `USART1_ProtocolTask`: GST 수신 queue에서 frame을 꺼내 GST packet을 해석한다. 수신된 GST read 영역 값은 통합 map `242~282`에 반영한다. 주기적으로 통합 map `201~241` 값을 읽어 GST 송신 frame 데이터를 만들고 GST 송신 queue에 넣는다. 수신 성공 여부와 연속 timeout/fail 기준으로 USART1/GST 통신 상태를 갱신한다.
  - `USART1_TxTask`: GST 송신 queue에서 송신 요청을 기다리고, 요청이 들어오면 USART1로 GST write frame을 전송한다. 송신 실패 시 짧은 delay 후 재시도한다.
  - 통합 map의 `201~241` write 영역을 GST peer로 송신.
  - GST peer에서 수신한 데이터를 `242~282` read 영역에 반영.
  - USART1 통신 성공/실패 상태를 `199` bit 3에 반영.
- 데이터 흐름: 통합 map write area -> `USART1_ProtocolTask` -> GST TX queue -> `USART1_TxTask` -> peer -> `USART1_RxTask` -> GST RX queue -> `USART1_ProtocolTask` -> 통합 map read area.
- 스케줄링: RAM config의 USART1 period를 사용하며 기본값은 50 ms.
- GST frame 형식: `[Header 3][Function Code 1][Address Hi 1][Address Lo 1][Length 1][Data N][CRC16 2][Tail 2]`.
- GST 상수: Header `0x47 0x53 0x54`, Tail `0x53 0x49`, CRC는 Modbus CRC16 Lo/Hi 순서.
- GST Function Code: `0x01` Read, `0x02` Write.
- GST 공용 메모리 맵:
  - `199`: 통신 상태 heartbeat bit flag.
  - `200`: PLC-UART4 heartbeat counter.
  - `201~241`: GST write 영역.
  - `242~282`: GST read 영역.
  - `283~322`: 예약 영역.

### ARCH-006 - 통합 메모리 맵, active ID list, 설정 RAM config

- 분류: 모듈 구조
- 상태: Confirmed
- 관련 요구사항: REQ-007, REQ-010, REQ-014, REQ-016
- 설명: 모든 PLC-visible data는 통합 메모리 맵에 저장된다.
- 구성 요소: map entry table, map lookup API, active_id_list, pending write 상태, 통신 상태 flag, RAM config.
- 책임:
  - 외부통신프로토콜 사양서 주소를 static entry로 제공.
  - ID, table type, address별 값을 분리.
  - PLC 응답과 polling 대상 ID를 active_id_list로 제한.
  - 예약 영역도 read/write 가능한 entry로 유지.
  - 기본 설정값을 compile-time macro에서 RAM config로 로드.
- 데이터 흐름: `UART4_Task`, `LPUART1_Task`, `USART2_Task`, `USART3_Task`, `USART1_ProtocolTask`가 통합 map을 공유한다.
- 제약 사항: 공유 자원 접근은 짧은 critical section 기반으로 동기화한다.
- 런타임 설정 변경: RAM config 기본값은 부팅 시 compile-time 값으로 로드되지만, PLC가 `UART4_Task`를 통해 HR `110~122`(Config Apply, `05-protocol.md`의 `PROTO-010` 참고)를 write하면 재부팅 없이 active ID/Gas profile을 변경할 수 있다. 변경 성공 시 `CommConfig_t.revision`이 증가하고 해당 하위 포트 task가 재-Discovery를 수행한다. Flash 비휘발성 저장은 제공하지 않는다.

### ARCH-007 - Transparent bridge hub 구조 폐기

- 분류: 폐기
- 상태: Confirmed
- 관련 요구사항: REQ-004
- 설명: PLC 요청을 하위 UART로 원본 forwarding하고 응답을 그대로 돌려주는 구조는 사용하지 않는다.
- 폐기 이유: PLC read/write를 통합 map 중심으로 일관 처리하고, 하위 통신 상태와 주기 갱신을 Interface Board가 관리하기 위함이다.
- 영향 범위: 기존 route table, bridge worker, stale response filtering 구조는 신규 구조에서 직접 사용하지 않는다.
- 대체 구조: `UART4_Task` + 통합 map + `LPUART1_Task`/`USART2_Task`/`USART3_Task` + USART1 GST 3-task 구조.

### ARCH-008 - USB 부트로더 + A/B 이중 슬롯 펌웨어 업데이트 구조

- 분류: 모듈 구조
- 상태: Done (코드 작성/개별 빌드 확인 완료, 2026-07-30 실 보드 종단 검증 완료)
- 관련 요구사항: REQ-021
- 전체 구성: Flash 512KB를 부트로더(48KB, 0x08000000) + Slot A(224KB, 0x0800C000) + Slot B(224KB, 0x08044000) + 부트 메타데이터(4KB, 0x0807C000) + 예비(12KB)로 분할한다.
- 앱(`IF_main`, 이미 상시 동작 중인 USB CDC 재사용): `a_boot_link.c`가 `ENTER_UPDATE` 프레임(CRC16 검증)을 인식하면 `.noinit_boot_flag`(소프트 리셋에도 보존되는 RAM 영역) 매직값을 세팅하고 `HAL_NVIC_SystemReset()`을 호출한다. 부팅 시 짧은 대기창을 두고 눈치보는 방식이 아니라, 앱이 살아있는 동안 언제든 확정적으로 트리거를 받는 구조라 타이밍 레이스가 없다.
- 부트로더(신규 별도 STM32CubeIDE 프로젝트 `Bootloader/`, FreeRTOS 없는 bare-metal): 부팅 시 `.noinit_boot_flag`와 부트 메타데이터(`PENDING_TEST` 롤백 여부)를 확인해 (a) 정상이면 즉시 해당 슬롯으로 VTOR 재설정 후 점프(USB 켜지 않음, 지연 없음), (b) 업데이트 요청이 있으면 USB CDC를 열고 상주하며 PC GUI의 `PING`/`ERASE_SLOT`/`WRITE_CHUNK`/`FINALIZE`/`REBOOT` 프로토콜을 처리한다.
- 안전장치: 실행 중이 아닌(inactive) 슬롯에만 쓰기 때문에 업데이트 도중 정전/USB 단절이 나도 기존 슬롯은 훼손되지 않는다. `FINALIZE`는 플래시 리드백 CRC32 검증 후에만 메타데이터를 `PENDING_TEST`로 갱신한다. 새 펌웨어는 `Application_Init()` 성공 직후 `Bootloader_ConfirmBootOk()`로 스스로 `CONFIRMED` 전환해야 하며, 실패/hang 시 IWDG(3초 타임아웃, 신규 활성화)가 리셋시켜 부트로더가 자동으로 이전 슬롯으로 롤백한다.
- PC GUI: `Tools/FirmwareUpdater/`(C#/.NET WinForms)에서 COM포트 선택, 슬롯별 `.bin` 선택, 업데이트 진행률/로그 표시.
- 상세: `04-data-model.md`의 `DATA-014`(부트 메타데이터 레이아웃), `05-protocol.md`의 `PROTO-011`(USB 프레임 프로토콜), `06-design-decisions.md`의 `DEC-023`(스왑 대신 슬롯 고정 링크 방식 채택 근거) 참고.

## 미결정 사항

- 없음.

## 변경 이력

- 2026-06-26: `interface_test/interface_main` 소스에서 아키텍처 초안 추출.
- 2026-06-27: 브릿지 허브 폐기, 통합 map, UART4 slave, 포트별 Modbus master/GST 구조 반영.
- 2026-06-27: active_id_list 기반 순차 polling 및 PLC 응답 제한 정책 반영.
- 2026-06-27: DIO `1~3`, Heating `1~7`, Gas `20~29`/`40~49` active ID 기본값 반영.
- 2026-06-27: USART1/GST ID 없는 global map과 write 실패 시 pending 폐기 정책 반영.
- 2026-06-27: `07-implementation-plan.md` 구현 단계 참조 반영.
- 2026-06-27: 통신 task 구성을 `UART4_Task`, `LPUART1_Task`, `USART2_Task`, `USART3_Task`, USART1 3-task 구조로 명확화.
- 2026-06-27: USART1/GST 공용 영역의 통합 map 동기화 주체를 USART1 GST task로 명확화.
- 2026-06-28: UART frame queue, 확정된 poll chunk, critical section 동기화, USART1 link 상태 처리 문구를 현재 코드 기준으로 갱신.
- 2026-06-30: 현재 코드 기준으로 UART4 write 즉시 ACK 정책과 active ID 기본값을 갱신.
- 2026-07-06: ARCH-006에 코드에 이미 구현되어 있던 런타임 Config Apply(HR110~122) 능력을 반영.
- 2026-07-06: ARCH-008 추가. USB 부트로더 + A/B 이중 슬롯 펌웨어 업데이트 구조 반영(실 보드 검증 전).
- 2026-07-30: ARCH-008 실 보드 종단 검증 완료로 상태를 Done으로 갱신.
