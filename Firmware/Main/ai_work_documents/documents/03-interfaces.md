# Interfaces

## 목적

모듈 간 함수, API, 메시지, 입출력, 에러 처리 규칙을 ID 기반으로 누적 관리한다.

## 인터페이스 목록

| ID | 분류 | 이름 | 제공자 | 소비자 | 상태 |
|---|---|---|---|---|---|
| IF-001 | 함수 | `InitApplication` | application init module | 애플리케이션 초기화 흐름 | Confirmed |
| IF-002 | API | UART 공통 수신 API | `a_uart` | `UART4_Task`, 하위 Modbus task, USART1 GST task | Confirmed |
| IF-003 | API | UART4 PLC Modbus Slave API | UART4 Modbus slave module | PLC, 통합 map, 하위 write dispatcher | Confirmed |
| IF-004 | API | 통합 메모리 맵 API | shared map module | UART4, LPUART1, USART2, USART3, USART1 | Confirmed |
| IF-005 | API | 하위 Modbus Master Port API | Modbus master modules | `LPUART1_Task`, `USART2_Task`, `USART3_Task` | Confirmed |
| IF-006 | API | GST Protocol API | GST module | `USART1_RxTask`, `USART1_ProtocolTask`, `USART1_TxTask`, 통합 map | Confirmed |
| IF-007 | 설정 | RAM 통신 설정 및 active ID API | config module | 모든 통신 task | Confirmed |
| IF-008 | 하드웨어 입출력 | RS485/RS422 DE/RE GPIO 제어 | GPIO/HAL | 송신 경로 | Confirmed |
| IF-009 | API | 런타임 Config Apply API | `a_application.c`(ServiceConfigApply), `a_comm_config.c`(CommConfig_ApplyDeviceSettings) | PLC(HR110~122), 하위 통신 task | Confirmed |
| IF-010 | API | USB 펌웨어 업데이트 프로토콜 API | `a_boot_link.c`(앱), `boot_protocol.c`(부트로더), `Tools/FirmwareUpdater`(PC GUI) | 앱 USB CDC, 부트로더 USB CDC, PC GUI | In Progress |
| IF-011 | API | UART4 PLC 4-그룹 통합 Read/Write 맵 API | UART4 Modbus slave module(그룹 번역 계층), shared map module | PLC | Confirmed |

## 구현 계획 참조

- 인터페이스의 실제 함수명과 파일명은 `07-implementation-plan.md`의 `IMP-002~IMP-008` 단계에서 확정한다.
- `a_comm_config.c/h`, `a_shared_map.c/h`, poll/write item table, `UART4_Task`, `LPUART1_Task`, `USART2_Task`, `USART3_Task`, USART1 GST 3-task 연동 API를 우선 구현 대상으로 둔다.
- SDD/subagent 구현 시 인터페이스 변경은 단계별 리뷰 subagent의 검토를 통과해야 한다.

## 상세 인터페이스

### IF-001 - `InitApplication`

- 분류: 함수
- 상태: Confirmed
- 제공자: 애플리케이션 초기화 모듈
- 소비자: 애플리케이션 초기화 흐름
- 목적: UART, 통합 map, RAM config, FreeRTOS 통신 task를 초기화한다.
- 입력: 없음
- 출력: 없음
- 호출 조건: HAL/CubeMX peripheral 초기화 이후.
- 타이밍 / 순서: UART 초기화 -> 통합 map static entry 초기화 -> RAM config 기본값 로드 -> UART 수신 시작 -> `UART4_Task`, `LPUART1_Task`, `USART2_Task`, `USART3_Task`, `USART1_RxTask`, `USART1_ProtocolTask`, `USART1_TxTask` 생성.
- 에러 처리: queue/task/map 초기화 실패 시 fault 정책 또는 error 상태 기록 필요.
- 관련 요구사항: REQ-001, REQ-002, REQ-014

### IF-002 - UART 공통 수신 API

- 분류: API
- 상태: Confirmed
- 제공자: `a_uart`
- 소비자: `UART4_Task`, `LPUART1_Task`, `USART2_Task`, `USART3_Task`, `USART1_RxTask`
- 목적: UART DMA + IDLE 수신 frame을 task에서 공통 방식으로 조회한다.
- 입력: UART handle, destination buffer, length pointer.
- 출력: 수신 데이터가 있으면 true, 없으면 false.
- 호출 조건: UART 초기화와 DMA 수신 시작이 선행되어야 한다.
- 에러 처리: UART/DMA error callback에서 수신을 재시작한다.
- 타이밍 / 순서: IDLE callback이 UART별 frame queue에 수신 frame을 넣고 task가 queue에서 데이터를 소비한다. queue full 시 오래된 frame을 버리고 최신 frame을 유지한다.
- 관련 요구사항: REQ-002

### IF-003 - UART4 PLC Modbus Slave API

- 분류: API
- 상태: Confirmed
- 제공자: UART4 Modbus slave module
- 소비자: PLC, 통합 map, 하위 write dispatcher
- 목적: PLC Modbus RTU 요청을 통합 map 기준으로 처리한다.
- 입력: UART4 수신 Modbus RTU frame.
- 출력:
  - read 요청: map 값 기반 Modbus RTU response.
  - write 요청: SharedMap 즉시 커밋 후 Modbus RTU ACK.
  - 미정의 entry, 미허용 ID, inactive ID: 응답 없음.
- ID 정책: `0~247` 허용, `248~255` 미허용. ID `0`은 broadcast가 아니라 일반 ID로 응답한다.
- active ID 정책: global 영역(설정 `110~125`, 슬레이브 상태 `131~145`, GST `199~322`)이 아닌 요청은 허용 범위 안이어도 대상 장치군 active_id_list에 없는 ID는 응답하지 않는다.
- global 영역 판정 정책: 위 3개 range(설정/슬레이브상태/GST)에 완전히 포함되는 주소는 FC03 Holding Register 요청에 한해 slave ID와 무관한 global map으로 취급한다. 다른 function code로 global 영역 주소를 요청하면 응답하지 않는다.
- GST global 정책: USART1/GST 영역 `199~322`는 UART4 request의 slave ID와 무관한 global map이다. `UART4_Task`는 PLC 요청을 map API로 응답하거나 write pending으로 등록하며, USART1로 Modbus frame forwarding하지 않는다. GST peer와의 실제 송수신 및 `201~282` 영역 동기화는 USART1 GST task가 담당한다.
- 지원 function code: 외부통신프로토콜 사양서에 정의된 FC01, FC02, FC03, FC04, FC05, FC06, FC0F, FC10 계열을 사용한다.
- 호출 조건: 통합 map이 초기화되어 있고 UART4 DMA 수신이 동작해야 한다.
- 에러 처리: CRC 오류, frame 길이 오류, 미정의 entry는 응답하지 않는다.
- 관련 요구사항: REQ-003, REQ-007, REQ-008, REQ-009, REQ-010

### IF-004 - 통합 메모리 맵 API

- 분류: API
- 상태: Confirmed
- 제공자: shared map module
- 소비자: UART4, LPUART1, USART2, USART3, USART1
- 목적: ID, 영역, 주소로 분리된 통합 map entry를 조회하고 갱신한다.
- Key: `id + table_type + address`.
- Table type:
  - Coil.
  - Discrete Input.
  - Input Register.
  - Holding Register.
- 주요 동작:
  - SharedMap_Init(): global 엔트리(slave status 131~145, GST 199~322)만 초기화. DIO/Heating/Gas 엔트리는 이 시점에 생성하지 않음.
  - SharedMap_RegisterSlave(id, group): config의 ID/Type 기준으로 그룹별 엔트리 추가(probe 없음). Gas는 H2+TFT 주소 합집합 등록.
  - entry 존재 여부 확인.
  - read 값 조회.
  - polling 성공 값 갱신.
  - PLC write pending 값 설정.
  - PLC write 요청 시 값 즉시 커밋.
  - 하위 write 전달은 ACK 이후 best-effort로 처리.
  - 통신 상태 bit와 heartbeat counter 갱신.
- pool 크기: 660 entries (DIO 4 + Heating 171 + Gas 330 + Global 155).
- 출력: 성공/실패 상태, entry pointer 또는 value.
- 에러 처리: 등록되지 않은 key는 실패를 반환하며 자동 생성하지 않는다.
- 동기화: 다중 task 접근은 짧은 critical section으로 보호한다.
- 관련 요구사항: REQ-007, REQ-010, REQ-011, REQ-012, REQ-013

### IF-005 - 하위 Modbus Master Port API

- 분류: API
- 상태: Confirmed
- 제공자: Modbus master modules (`a_modbus_master.c`)
- 소비자: `LPUART1_Task`, `USART2_Task`, `USART3_Task`
- 목적: 하위 장치 read polling과 pending write 전송을 수행한다.
- 주요 공개 함수:
  - `ModbusMaster_InitContext`: port context 초기화.
  - `ModbusMaster_InitQueues`: 포트별 tx_queue / rx_queue 생성.
  - `ModbusMaster_ForwardWrite`: UART4_Task에서 write 요청을 tx_queue에 enqueue.
  - `ModbusMaster_TxStep`: tx_queue write 우선 처리 → poll 주기 도래 시 poll 송신 → timeout 시 RetryOrGivePoll.
  - `ModbusMaster_RxStep`: waiting_response 중 UART 수신 확인 → rx_queue enqueue.
  - `ModbusMaster_ProcessStep`: rx_queue dequeue → ParseReadResponse → SharedMap 갱신 / RetryOrGivePoll.
- 큐 구조:
  - tx_queue: `ModbusSlaveWriteRequest_t`, depth 4 (write 전달용).
  - rx_queue: `ModbusMasterRxMsg_t`, depth 4 (poll 응답 수신용).
- 포트:
  - `LPUART1_Task`: DIO, active ID comm_config 기본 `{1, 2}` (슬롯 2개).
  - `USART2_Task`: Heating Jacket, active ID comm_config 기본 `{20, 21, 22}` (슬롯 3개).
  - `USART3_Task`: Gas Detector, active ID comm_config 기본 `{30, 40, 50}` (슬롯 3개, Gas Type 설정값으로 STD/TFT 결정).
- 타이밍: 포트별 polling period 기본값은 모두 50 ms.
- 순차 처리: active_id_list의 실제 사용 ID만 선택하고, 각 ID 안에서는 외부통신프로토콜 사양서의 장치별 주소 표 순서대로 poll item을 처리한다.
- 우선순위: tx_queue의 pending write가 있으면 poll보다 우선 처리한다.
- 재시도: 기본 3회, RAM config로 변경 가능.
- response timeout: LPUART1 기본 50 ms, USART2 기본 200 ms, USART3 기본 100 ms, RAM config로 변경 가능.
- 에러 처리: 연속 3회 timeout/fail이면 해당 통신 상태 bit를 0으로 설정하고, 1회 성공하면 1로 복구한다.
- 관련 요구사항: REQ-005, REQ-014, REQ-015

### IF-006 - GST Protocol API

- 분류: API
- 상태: Confirmed
- 제공자: GST module
- 소비자: `USART1_RxTask`, `USART1_ProtocolTask`, `USART1_TxTask`, 통합 map
- 목적: USART1 RS422 GST frame 송수신과 통합 map의 GST 공용 영역 연동을 제공한다.
- 입력: 통합 map의 `201~241` write 영역, 수신 GST frame.
- 출력: GST TX frame, 통합 map `242~282` read 영역 갱신, USART1 통신 상태 bit 갱신.
- 호출 조건: USART1 DMA 수신과 GST 3-task 구조가 동작해야 한다.
- 타이밍: USART1 period 기본값은 50 ms.
- 에러 처리: header, tail, length, CRC 오류 frame은 drop한다. 연속 3회 timeout/fail이면 `199` bit 3을 0으로 설정하고 1회 성공 시 1로 복구한다.
- task별 역할:
  - `USART1_RxTask`: USART1 수신 데이터를 확인하고 GST 수신 queue에 frame을 넣는다. queue full 시 최신 frame 기준으로 재동기화한다.
  - `USART1_ProtocolTask`: GST 수신 queue에서 frame을 꺼내 packet을 해석하고, 통합 map `242~282` read 영역을 갱신한다. 주기적으로 `201~241` write 영역을 읽어 GST 송신 frame 데이터를 만들고 GST 송신 queue에 넣는다.
  - `USART1_TxTask`: GST 송신 queue에서 송신 요청을 기다린 뒤 USART1로 GST write frame을 전송한다. 송신 실패 시 짧은 delay 후 재시도한다.
- GST frame 형식: `[Header 3][Function Code 1][Address Hi 1][Address Lo 1][Length 1][Data N][CRC16 2][Tail 2]`.
- GST 상수: Header `0x47 0x53 0x54`, Tail `0x53 0x49`, CRC는 Modbus CRC16 Lo/Hi 순서.
- GST Function Code: `0x01` Read, `0x02` Write.
- 관련 요구사항: REQ-006, REQ-011, REQ-012, REQ-014, REQ-015

### IF-007 - RAM 통신 설정 및 active ID API

- 분류: 설정
- 상태: Confirmed
- 제공자: config module
- 소비자: `UART4_Task`, `LPUART1_Task`, `USART2_Task`, `USART3_Task`, USART1 GST task
- 목적: compile-time default를 RAM 설정값으로 복사하고 task가 실행 중 참조하게 한다.
- 설정값:
  - `lpuart1_poll_period_ms`: 기본 50.
  - `usart2_poll_period_ms`: 기본 50.
  - `usart3_poll_period_ms`: 기본 50.
  - `usart1_period_ms`: 기본 50.
  - `tx_timeout_ms`: 기본 50.
  - `lpuart1_response_timeout_ms`: 기본 50.
  - `usart2_response_timeout_ms`: 기본 200.
  - `usart3_response_timeout_ms`: 기본 100.
  - `retry_count`: 기본 3.
  - `dio_ids`: DIO 실제 사용 ID 목록, 기본값 `{1, 2}`, 슬롯 2개.
  - `heating_ids`: Heating Jacket 실제 사용 ID 목록, 기본값 `{20, 21, 22}`, 슬롯 3개.
  - `gas_ids`: Gas Detector 실제 사용 ID 목록, 기본값 `{30, 40, 50}`, 슬롯 3개. 실제 하드웨어 ID와 일치해야 함.
  - `gas_types`: Gas 슬롯별 Type(`0=동작 안 함`/`1=STD`/`2=TFT`), 기본값은 3슬롯 모두 `1(STD)`.
  - `revision`: 런타임 설정 변경(IF-009) 성공 시마다 1 증가하는 카운터.
- 저장 위치: RAM.
- 제약 사항: Flash(비휘발성) 저장은 제공하지 않는다. PLC register 기반 런타임 설정 변경 자체는 IF-009에서 제공한다.
- 관련 요구사항: REQ-014, REQ-016

### IF-008 - RS485/RS422 DE/RE GPIO 제어

- 분류: 하드웨어 입출력
- 상태: Confirmed
- 제공자: GPIO/HAL
- 소비자: Modbus master 송신 경로, GST 송신 경로
- 목적: transceiver 송수신 방향을 제어한다.
- 입력: GPIO port/pin, TX/RX mode.
- 출력: DE/RE pin state.
- 채널:
  - USART1: RS422 GST 통신.
  - LPUART1, USART2, USART3, UART4: RS485 통신.
- 관련 요구사항: REQ-003, REQ-005, REQ-006

### IF-009 - 런타임 Config Apply API

- 분류: API
- 상태: Confirmed
- 제공자: `a_application.c`(`ServiceConfigApply`), `a_comm_config.c`(`CommConfig_ApplyDeviceSettings`)
- 소비자: PLC(HR110~122 Modbus 접근), `LPUART1_Task`/`USART2_Task`/`USART3_Task`(revision 변경 감지 후 config 기준 재등록)
- 목적: 재컴파일/재부팅 없이 PLC가 DIO/Heating/Gas active ID와 Gas profile 힌트를 런타임에 변경할 수 있게 한다.
- 입력: HR `110~120`(DIO/Heating/Gas ID + gas profile 힌트), HR `121`(apply trigger, PLC Slave ID `8` Write group addr `317`과 동일 레지스터).
- 출력: HR `122`(결과 코드), `CommConfig_t` active ID list 갱신, `CommConfig_t.revision` 증가.
- 호출 조건: `UART4_Task` 루프에서 매 tick 폴링(`ServiceConfigApply()`), HR121이 `1`일 때만 실제 적용을 시도한다.
- 검증 규칙: ID는 `0` 또는 `1~247`, 그룹 내 `0`이 아닌 ID 중복 불허, gas profile 값은 `0~2` 범위. 실패 시 기존 active list는 변경하지 않고 결과 코드만 기록한다.
- 결과 코드: `0=IDLE`, `1=OK`, `2=INVALID_ID`, `3=DUPLICATE_ID`, `4=INVALID_GAS_TYPE`, `5=INTERNAL_ERROR`.
- 후속 동작: `revision` 변경을 감지한 하위 포트 task는 `ReinitXxxContext()` + `ModbusMaster_RegisterConfiguredSlaves()`를 재실행해 새 ID/Type을 즉시 반영한다.
- 제약 사항: Flash 저장은 제공하지 않는다. 전원 재인가 시 compile-time 기본값으로 되돌아간다.
- 관련 요구사항: REQ-014, REQ-020
- 상세 레지스터 배치: `04-data-model.md`의 `DATA-013` 참고.

### IF-010 - USB 펌웨어 업데이트 프로토콜 API

- 분류: API
- 상태: In Progress (코드 작성/개별 빌드 확인 완료, 실 보드 종단 검증 전)
- 제공자: `a_boot_link.c`(앱 측 트리거 인식), `Bootloader/Bootloader/Core/Src/boot_protocol.c`(부트로더 측 전체 프로토콜)
- 소비자: `Tools/FirmwareUpdater`(PC GUI, C#/.NET WinForms)
- 목적: 앱이 상시 열어두는 USB CDC(가상 COM포트)를 통해 PC GUI가 재컴파일/재플래시 없이 펌웨어를 갱신할 수 있게 한다.
- 프레임 형식(리틀엔디안): `[0xAA][0x55][CMD:1][LEN:2][PAYLOAD:LEN][CRC16:2]`. CRC16은 SOF 2바이트를 제외한 CMD+LEN+PAYLOAD 구간에 대해 계산(Modbus와 동일 다항식).
- 앱이 인식하는 커맨드: `ENTER_UPDATE`(0x00, payload 없음) 하나뿐. 유효(CRC16 일치) 프레임을 받으면 `.noinit_boot_flag`에 매직값을 세팅하고 `HAL_NVIC_SystemReset()`을 호출한다.
- 부트로더가 처리하는 커맨드:
  - `PING`(0x01): 부트로더 버전, 현재 active_slot, Slot A/B 각각의 유효 여부와 버전을 반환.
  - `ERASE_SLOT`(0x02): `[target_slot:1][total_size:4]` - 대상 슬롯을 total_size만큼 커버하는 페이지 수만큼 소거.
  - `WRITE_CHUNK`(0x03): `[offset:4][data:N]`(N≤256, offset은 8의 배수) - double-word 정렬 프로그래밍.
  - `FINALIZE`(0x04): `[total_size:4][crc32:4][major:1][minor:1][patch:1]` - 플래시 리드백 CRC32와 대조 후 일치해야 메타데이터를 `PENDING_TEST`로 갱신.
  - `REBOOT`(0x05): 응답 후 `NVIC_SystemReset()`.
- 응답 형식: 요청과 동일한 프레임에 `CMD`를 에코, `PAYLOAD[0]`이 상태(`0=OK`/`1=FAIL`).
- 호출 조건: 앱은 `StartDefaultTask` 루프에서 20ms 주기로 `AppBootLink_Process()` 폴링. 부트로더는 `.noinit_boot_flag`가 세팅된 경우에만 USB CDC를 초기화하고 상주(평상시 전원 인가 시에는 USB를 켜지 않고 즉시 슬롯으로 점프).
- 관련 요구사항: REQ-021
- 상세: `02-architecture.md`의 `ARCH-008`, `04-data-model.md`의 `DATA-014`, `05-protocol.md`의 `PROTO-011` 참고.

### IF-011 - UART4 PLC 4-그룹 통합 Read/Write 맵 API

- 분류: API
- 상태: Confirmed
- 제공자: UART4 Modbus slave module(신규 PLC 그룹 번역 계층), shared map module
- 소비자: PLC
- 목적: PLC가 물리 Slave ID를 몰라도 Slave ID `1~4`(Read)/`5~8`(Write) 그룹과 외부통신프로토콜 사양서의 절대주소만으로 통합 map의 여러 물리 장치 entry에 접근하게 한다.
- 입력: UART4 수신 Modbus RTU frame (Slave ID `1~8`, FC01/02/03/04/05/06/0F/10, 사양서 절대주소 `1~164`(Read) / `201~316`(Write)).
- 출력:
  - Read: 그룹 매핑표 기준으로 SharedMap의 여러 entry 값을 조합(Gas Status 등은 8bit 패킹)한 Modbus RTU response.
  - Write: 그룹 매핑표 기준으로 값을 SharedMap의 각 entry로 분해(unpack)해 즉시 커밋 후 ACK.
- 적용 범위: `UART4_Task`의 PLC-facing 처리에만 적용된다. SharedMap 구조와 `IF-003`(기존 UART4 PLC Modbus Slave 처리), `IF-004`(통합 map), `IF-005`(하위 Modbus master), `IF-006`(GST)의 동작 방식은 변경하지 않는다 — 이 계층은 그 위에 얹히는 번역 계층이다.
- Gas 그룹(`Slave ID=3`) 특이사항: 물리 슬롯 3개(active ID 기본값 `{30,40,50}`)가 Write ID=8의 Gas Type 레지스터(`0`=동작 안 함, `1`=STD, `2`=TFT) 값에 따라 STD 자리 또는 TFT 자리 중 하나만 채워진다. Discovery(자동 판별)는 사용하지 않으며, ID/Type 레지스터 값을 그대로 보고 실시간으로 동작을 전환한다.
- 호출 조건: 통합 map이 초기화되어 있고 UART4 DMA 수신이 동작해야 한다.
- 관련 요구사항: REQ-003, REQ-007, REQ-010
- 상세 주소 매핑표: `05-protocol.md`의 `PROTO-012` 참고.

## 미결정 사항

- 없음.

## 변경 이력

- 2026-06-26: `interface_test/interface_main` 소스에서 인터페이스 초안 추출.
- 2026-06-27: 통합 map, UART4 slave, 하위 master, USART1 GST, RAM config 인터페이스로 갱신.
- 2026-06-27: active_id_list API와 inactive ID 응답 없음 정책 반영.
- 2026-06-27: DIO `1~3`, Heating `1~7`, Gas `20~29`/`40~49` active ID 기본값 반영.
- 2026-06-27: USART1/GST ID 없는 global map과 write 실패 시 pending 폐기 정책 반영.
- 2026-06-27: `07-implementation-plan.md` 구현 단계 참조 반영.
- 2026-06-27: 통신 task 이름과 USART1 GST 3-task 역할을 인터페이스 문서에 명확화.
- 2026-06-27: USART1/GST 공용 영역의 통합 map 동기화 주체를 USART1 GST task로 명확화.
- 2026-06-28: UART frame queue, critical section 동기화, task 간 pending wait 구현 상태를 현재 코드 기준으로 갱신.
- 2026-06-28: IF-005 ModbusMaster API를 TxStep/RxStep/ProcessStep 3단계 + tx_queue/rx_queue 분리 구조로 갱신. DIO active ID `1~3` → `1~2`, write timeout 50 ms → 30 ms 반영.
- 2026-06-28: IF-004 SharedMap_RegisterSlave() 추가, pool 660개, Discovery 후 실제 ID 동적 등록 방식 반영. IF-005 Heating 슬롯 3개, Gas 슬롯 3개로 갱신. IF-007 기본 ID 값 갱신.
- 2026-06-30: 현재 코드 기준 SharedMap global config/version entry 포함으로 pool 크기를 660개로 정정.
- 2026-06-30: 현재 코드 기준으로 즉시 ACK write 정책, active ID 기본값, response timeout 필드를 갱신.
- 2026-07-06: IF-003의 global 영역 판정을 GST 단독이 아니라 설정(110~125)/슬레이브상태(131~145)/GST(199~322) 3개 range로 정정. 코드에 이미 구현되어 있던 런타임 Config Apply를 IF-009로 신규 반영하고, IF-007의 "PLC register 설정 변경 제외" 문구를 정정.
- 2026-07-06: IF-010 추가. USB 펌웨어 업데이트 프로토콜 API(앱 측 ENTER_UPDATE 트리거 + 부트로더 측 PING/ERASE_SLOT/WRITE_CHUNK/FINALIZE/REBOOT) 반영, 실 보드 검증 전.
- 2026-07-14: IF-011 추가. UART4 PLC 4-그룹 통합 Read/Write 맵 API(Slave ID 1~4 Read, 5~8 Write) 신규 반영. 기존 IF-003/004/005/006 동작은 변경 없음을 명시.
