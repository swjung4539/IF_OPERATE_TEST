# Implementation Plan

## 목적

통합 메모리 맵 기반 통신 구조를 실제 코드에 반영하기 위한 구현 순서, 단계별 완료 기준, 검증 기준, SDD/subagent 활용 방식을 정의한다.

## 구현 전제

- UART4는 PLC용 Modbus RTU slave로 동작한다.
- `UART4_Task`는 PLC 요청을 수신하고 통합 메모리 맵 기준으로 read 응답 또는 write pending을 생성한다.
- `LPUART1_Task`, `USART2_Task`, `USART3_Task`는 하위 장치용 Modbus RTU master로 동작하며 하위 장치 데이터와 통합 메모리 맵을 동기화한다.
- USART1은 RS422 GST protocol 전용이며 Modbus master/slave가 아니다.
- USART1은 `USART1_RxTask`, `USART1_ProtocolTask`, `USART1_TxTask` 3개 task로 구성한다.
- 브릿지 허브 기능은 폐기한다.
- 통합 map은 일반 Modbus 장치에 대해 `id + table_type + address`를 key로 사용한다.
- USART1/GST 영역 `199~322`는 ID 없는 global map으로 처리한다.
- PLC read는 현재 map 값을 즉시 반환한다.
- PLC write는 SharedMap에 즉시 커밋하고 PLC에 즉시 ACK한다.
- 하위 장치 또는 GST peer 전달은 ACK 이후 별도 task에서 best-effort로 처리한다.
- DIO active ID는 `{1, 2}`이다.
- Heating Jacket active ID는 `{20, 21, 22}`이다.
- Gas Detector active ID는 `{31, 40, 30}`이며, H2/H2_TFT profile은 Discovery 결과로 판별한다.
- 모든 포트 period 기본값은 50 ms, TX timeout 기본값은 50 ms, response timeout 기본값은 LPUART1 50 ms / USART2 200 ms / USART3 100 ms, retry count 기본값은 3회다.

## 통신 Task 구성

- `UART4_Task`: PLC와의 상위 통신을 담당한다. PLC 요청을 수신한 뒤 통합 메모리 맵 기준으로 read 응답 또는 write pending을 생성한다.
- `LPUART1_Task`: LPUART1 하위 장치와 주기적으로 송수신하며, 하위 장치 데이터와 통합 메모리 맵을 동기화한다.
- `USART2_Task`: USART2 하위 장치와 주기적으로 송수신하며, 하위 장치 데이터와 통합 메모리 맵을 동기화한다.
- `USART3_Task`: USART3 하위 장치와 주기적으로 송수신하며, 하위 장치 데이터와 통합 메모리 맵을 동기화한다.
- `USART1_RxTask`: USART1 수신 데이터를 확인하고 수신 frame을 GST 수신 queue에 넣는다. queue가 가득 찬 경우 오래된 frame을 버리고 최신 frame 기준으로 재동기화한다.
- `USART1_ProtocolTask`: GST 수신 queue에서 frame을 꺼내 GST packet을 해석한다. 수신된 GST read 영역 값은 통합 메모리 맵의 GST read 영역에 반영하고, 주기적으로 GST write 영역 값을 읽어 GST 송신 frame 데이터를 만든 뒤 GST 송신 queue에 넣는다. 수신 성공 여부와 연속 timeout/fail 기준으로 USART1/GST 통신 상태를 갱신한다.
- `USART1_TxTask`: GST 송신 queue에서 송신 요청을 기다린다. 송신 요청이 들어오면 USART1로 GST write frame을 전송하고, 송신 실패 시 짧은 delay 후 재시도한다.

USART1/GST 공용 영역 `199~322`의 값 동기화 주체는 `USART1_ProtocolTask`다. `UART4_Task`는 PLC가 해당 map 영역을 Modbus로 조회하거나 write 요청을 낼 때 map API와 pending write 상태만 처리하며, USART1 GST frame 송수신을 수행하지 않는다.

## 구현 단계 목록

| ID | 단계 | 주요 파일 후보 | 의존성 | 완료 기준 |
|---|---|---|---|---|
| IMP-001 | 기존 통신 코드 기준점 확인 | 기존 `User/Application/*` | 없음 | UART, Modbus, GST, 기존 bridge 코드의 재사용/폐기 범위가 정리됨 |
| IMP-002 | 통신 설정 모듈 추가 | `a_comm_config.c/h` | IMP-001 | period, timeout, retry, active ID 기본값을 RAM config로 제공 |
| IMP-003 | 통합 map 모듈 추가 | `a_shared_map.c/h` | IMP-002 | 일반 key와 GST global key를 모두 지원하고 pending write 상태를 관리 |
| IMP-004 | poll/write item table 추가 | `a_poll_table.c/h` 또는 map 내부 const table | IMP-003 | DIO, Heating, H2 Detector, H2 Detector(TFT) 항목이 사양서 순서대로 정의됨 |
| IMP-005 | UART4 read path 전환 | `a_modbus_slave.c` | IMP-003 | PLC read가 하위 장치 즉시 통신 없이 map 값으로 응답 |
| IMP-006 | UART4 write immediate ACK path 전환 | `a_modbus_slave.c`, `a_application.c`, 하위 master queue/event | IMP-003, IMP-004 | SharedMap 즉시 커밋 후 PLC ACK, 하위 전달은 비동기 처리 |
| IMP-007 | 하위 Modbus master task 구현 | `a_modbus_master.c/h`, `a_application.c/h`, task별 진입점 | IMP-004, IMP-006 | `LPUART1_Task`, `USART2_Task`, `USART3_Task`가 pending write 우선 처리 후 active ID 기준 순차 polling 수행 |
| IMP-008 | USART1 GST 3-task map 연동 | `a_gst_protocol.c/h`, `a_application.c/h` | IMP-003 | `USART1_RxTask`, `USART1_ProtocolTask`, `USART1_TxTask`가 `201~241` 송신, `242~282` 수신, `199` bit 3 갱신 |
| IMP-009 | 기존 bridge hub 경로 제거/비활성화 | `a_modbus_slave.c`, `a_application.c` | IMP-005~IMP-008 | transparent forwarding 경로가 동작하지 않음 |
| IMP-010 | 빌드 및 단계별 검증 | CubeIDE build, 장비 테스트 | IMP-009 | 빌드와 핵심 통신 시나리오 검증 결과 기록 |
| IMP-013 | 런타임 Config Apply (HR110~122) | `a_application.c`, `a_comm_config.c` | IMP-002 | PLC가 HR121 trigger로 active ID/Gas profile을 런타임 변경하고 결과가 HR122에 기록됨. 코드에 이미 구현되어 있던 내용을 문서에 소급 반영(자세한 내용은 `08-implementation-notes.md`의 `IMP-013` 참고) |
| IMP-014 | USB 펌웨어 업데이트 (부트로더 A/B 이중 슬롯 + C# GUI) | `Bootloader/`(신규 프로젝트), `Shared/bootloader_shared.*`, `a_boot_link.c`, `a_bootloader_link.c`, `Tools/FirmwareUpdater/` | IMP-002, IMP-010 | 실행 중 슬롯을 건드리지 않는 A/B 업데이트, IWDG 기반 자동 롤백, C# GUI로 종단 갱신. 개별 빌드 확인 완료, 실 보드 검증은 진행 중(자세한 내용은 `08-implementation-notes.md`의 `IMP-014` 참고) |

## 단계별 상세 계획

### IMP-001 - 기존 통신 코드 기준점 확인

- 목적: 기존 코드를 무리하게 지우기 전에 재사용 가능한 부분과 폐기할 부분을 분리한다.
- 확인 항목:
  - `a_uart.c/h`: DMA circular + IDLE 수신 계층.
  - `m_modbus.c/h`: Modbus frame 생성/검증/응답 함수.
  - `a_modbus_slave.c`: UART4 slave, 기존 bridge routing.
  - `a_modbus_master.c/h`: master query/response 처리 재사용 가능성.
  - `a_gst_protocol.c/h`: GST frame parse/build와 USART1 송수신.
- 완료 기준:
  - 재사용할 함수 목록과 수정할 함수 목록을 구현 메모에 남긴다.
  - 문서 범위 밖 기능은 구현 범위에 포함하지 않는다.

### IMP-002 - 통신 설정 모듈 추가

- 목적: compile-time 기본값을 RAM config에 복사하고 모든 task가 동일한 설정을 참조하게 한다.
- 기본값:
  - `lpuart1_poll_period_ms = 50`
  - `usart2_poll_period_ms = 50`
  - `usart3_poll_period_ms = 50`
  - `usart1_period_ms = 50`
  - `tx_timeout_ms = 50`
  - `lpuart1_response_timeout_ms = 50`
  - `usart2_response_timeout_ms = 200`
  - `usart3_response_timeout_ms = 100`
  - `retry_count = 3`
  - `dio_ids = {1, 2}`
  - `heating_ids = {20, 21, 22}`
  - `gas_ids = {31, 40, 30}`
- 완료 기준:
  - 설정값 getter 또는 const pointer API 제공.
  - active ID 포함 여부 검사 API 제공.
  - Flash 저장은 포함하지 않음. PLC register 기반 런타임 설정 변경은 이후 `IMP-013`(현재 코드 기준 소급 반영)에서 추가로 제공됨.

### IMP-003 - 통합 map 모듈 추가

- 목적: `UART4_Task`, `LPUART1_Task`, `USART2_Task`, `USART3_Task`, USART1 GST task가 공유하는 단일 데이터 저장소를 제공한다.
- 핵심 API 후보:
  - `SharedMap_Init()`
  - `SharedMap_Find(id, table, address)`
  - `SharedMap_FindGlobal(table, address)`
  - `SharedMap_Read(...)`
  - `SharedMap_SetPendingWrite(...)`
  - `SharedMap_CommitWrite(...)`
  - `SharedMap_DiscardPendingWrite(...)`
  - `SharedMap_UpdateFromPoll(...)`
  - `SharedMap_SetCommStatus(...)`
  - `SharedMap_IncrementPlcHeartbeat()`
- 완료 기준:
  - 일반 Modbus 장치는 `id + table_type + address`로 조회된다.
  - GST `199~322`는 ID 없이 global로 조회된다.
  - write 실패 시 pending 값 폐기 동작이 구현된다.
  - 동시 접근 보호 방식이 적용된다.

### IMP-004 - poll/write item table 추가

- 목적: 외부통신프로토콜 사양서의 장치별 표 순서를 코드의 scheduler 입력으로 제공한다.
- 포함 항목:
  - DIO: FC03 Holding 130 poll, FC10 Holding 129 write.
  - Heating Jacket: FC04 Input 1000~1017, FC04 Input 1018~1035, FC03 Holding 2000~2020 readback, FC06/FC10 write.
  - H2 Detector: FC04 Input 30000~30003, FC02 Discrete 10001~10008, FC01 Coil 2~3 readback, FC05/FC0F write.
  - H2 Detector(TFT): Coil, Discrete Input, Input Register, Holding Register 항목을 `05-protocol.md`의 `PROTO-008` 순서로 처리.
- 완료 기준:
  - active ID와 profile에 따라 poll item이 선택된다.
  - pending write item은 read poll item보다 우선 처리 가능하다.

### IMP-005 - UART4 read path 전환

- 목적: PLC read 요청을 하위 장치 즉시 통신이 아니라 통합 map 값으로 응답하게 한다.
- 처리 정책:
  - GST global 영역 `199~322`는 slave ID와 무관하게 map을 조회한다.
  - 그 외 영역은 active ID와 static map entry가 모두 유효해야 응답한다.
  - 유효하지 않은 요청은 응답하지 않는다.
- 완료 기준:
  - FC01, FC02, FC03, FC04 read 요청이 map 값으로 응답한다.
  - `200` heartbeat counter가 UART4 정상 통신 기준으로 증가한다.

### IMP-006 - UART4 write immediate ACK path 전환

- 목적: PLC write 요청을 SharedMap에 즉시 커밋하고 PLC에 즉시 ACK한 뒤 하위 장치 전달은 비동기 처리한다.
- 처리 정책:
  - PLC write 수신 시 `pending_value` 저장 및 `dirty` set.
  - `UART4_Task`가 `value = pending_value`, `dirty` clear 후 UART4 ACK를 전송한다.
  - 대상 하위 Modbus task 또는 USART1 GST task에 write request를 비동기 전달한다.
  - 하위 전달 실패는 PLC ACK를 되돌리지 않는다.
- 완료 기준:
  - FC05, FC06, FC0F, FC10 write 요청을 처리한다.
  - 실패 시 desired/actual 값을 별도로 남기지 않는다.

### IMP-007 - 하위 Modbus master task 구현

- 목적: `LPUART1_Task`, `USART2_Task`, `USART3_Task`가 active ID 기준으로 순차 polling과 pending write를 수행한다.
- 공통 흐름:
  - pending write가 있으면 우선 처리.
  - pending write가 없으면 active ID의 현재 poll item 처리.
  - poll item 완료 후 다음 item으로 이동.
  - ID의 마지막 item 이후 다음 active ID로 이동.
  - 마지막 active ID 이후 처음으로 rollover.
- 완료 기준:
  - `LPUART1_Task`가 DIO ID `{1, 2}` 처리.
  - `USART2_Task`가 Heating ID `{20, 21, 22}` 처리.
  - `USART3_Task`가 Gas ID `{31, 40, 30}` 처리.
  - `USART3_Task`가 Discovery 결과로 H2 Detector 또는 H2 Detector(TFT) profile을 선택.
  - 연속 3회 timeout/fail 시 `199` 해당 bit clear, 1회 성공 시 set.

### IMP-008 - USART1 GST 3-task map 연동

- 목적: 기존 GST local buffer를 통합 map의 global GST 영역과 연결한다.
- 처리 정책:
  - `USART1_RxTask`는 USART1 수신 데이터를 확인해 GST 수신 queue에 넣는다. queue full 시 최신 frame 기준으로 재동기화한다.
  - `USART1_ProtocolTask`는 GST 수신 queue에서 frame을 꺼내 GST packet을 해석하고, 수신된 GST read 영역 값을 통합 map `242~282`에 반영한다.
  - `USART1_ProtocolTask`는 주기적으로 통합 map `201~241` 값을 읽어 GST 송신 frame 데이터를 만들고 GST 송신 queue에 넣는다.
  - `USART1_TxTask`는 GST 송신 queue에서 송신 요청을 기다린 뒤 USART1로 GST write frame을 전송한다. 송신 실패 시 짧은 delay 후 재시도한다.
  - `201~241` write 영역을 GST TX frame으로 송신.
  - 수신된 GST data는 `242~282` read 영역에 저장.
  - `199` bit 3은 USART1/GST 통신 상태로 갱신.
  - USART1/GST 자체에는 ID를 사용하지 않는다.
  - GST 공용 영역 `199~322`의 실제 GST 송수신과 map 동기화는 USART1 GST task가 담당한다.
  - `UART4_Task`는 GST 공용 영역 요청을 USART1로 Modbus frame forwarding하지 않는다.
- GST frame 형식:
  - `[Header 3][Function Code 1][Address Hi 1][Address Lo 1][Length 1][Data N][CRC16 2][Tail 2]`.
  - Header: `0x47 0x53 0x54`.
  - Tail: `0x53 0x49`.
  - CRC: Modbus CRC16, Lo/Hi 순서.
  - Function Code: `0x01` Read, `0x02` Write.
- 완료 기준:
  - GST 송수신이 global map을 기준으로 동작한다.
  - `283~322` 예약 영역은 read/write 가능하고 기본값 0이다.

### IMP-009 - 기존 bridge hub 경로 제거/비활성화

- 목적: 새 구조와 충돌하지 않도록 transparent bridge forwarding 경로를 제거하거나 명확히 비활성화한다.
- 완료 기준:
  - UART4 요청을 하위 UART로 원본 forwarding하지 않는다.
  - USART1 GST 공용 영역의 실제 송수신과 map 동기화는 USART1 GST task만 담당한다.
  - UART4 요청을 USART1 GST frame으로 변환하거나 forwarding하지 않는다.
  - 기존 bridge queue/worker가 새 master polling과 동시에 같은 UART를 제어하지 않는다.
  - 사용하지 않는 코드가 남는 경우 주석 또는 compile-time flag로 폐기 상태가 명확하다.

### IMP-010 - 빌드 및 단계별 검증

- 목적: 구현 결과가 문서 요구사항과 일치하는지 확인한다.
- 검증 항목:
  - 빌드 성공.
  - active ID 필터 동작.
  - GST global map 동작.
  - pending write 즉시 커밋과 하위 전달 상태 전이.
  - UART4 read 응답.
  - UART4 write 즉시 ACK와 하위 전달 best-effort 처리.
  - DIO, Heating, H2 Detector, H2 Detector(TFT) poll 순서.
  - `199` 통신 상태 bit와 `200` heartbeat counter.
- 완료 기준:
  - 실행한 검증 명령과 결과를 기록한다.
  - 장비 검증이 필요한 항목은 미검증 상태로 명확히 표시한다.

## 현재 구현 상태

- 갱신 일자: 2026-06-28
- `Core/Src/main.c`는 `Application_Init()` 후 `Application_CreateTasks()`를 호출하고, `a_application.c`는 `UART4_Task`, `LPUART1_Task`, `USART2_Task`, `USART3_Task`, `USART1_RxTask`, `USART1_ProtocolTask`, `USART1_TxTask`를 생성한다.
- `Application_RunOnce()` 단일 호출 구조와 `defaultTask` 연결은 현재 코드에서 사용하지 않는다.
- `a_uart`는 UART별 DMA/IDLE 수신 frame queue를 제공하며 queue full 시 오래된 frame을 버리고 최신 frame을 유지한다.
- 하위 Modbus pending write는 `UART4_Task`가 PLC에 즉시 ACK한 뒤 `ModbusMaster_ForwardWrite()`로 tx_queue에 제출하며, 대상 하위 task가 비동기 처리한다.
- USART1/GST는 3-task queue 구조로 `201~241` 송신, `242~282` 수신, `199` bit 3 상태 갱신을 수행한다.
- `UART4_Task`는 매 tick `ServiceConfigApply()`를 호출해 HR121 trigger 기준으로 HR110~120의 active ID/Gas profile 설정을 런타임 반영하고 결과를 HR122에 기록한다(IMP-013).
- IWDG(3초 타임아웃)가 활성화되어 `UART4_Task`가 매 tick 먹인다(feed). `Application_Init()` 성공 직후 `Bootloader_ConfirmBootOk()`를 호출해 A/B 부트 메타데이터를 확정(CONFIRMED)한다(IMP-014).
- USB CDC로 상시 연결 가능하며, `ENTER_UPDATE` 트리거를 받으면 부트로더 모드로 재진입해 A/B 이중 슬롯 펌웨어 업데이트를 수행한다(IMP-014, 2026-07-30 실 보드 검증 완료: A/B 왕복 업데이트, IWDG 롤백 시나리오 확인).
- Debug clean-first 빌드는 성공했고, 2026-07-30 기준 IMP-007/IMP-008/IMP-010/IMP-014 실 장비 통신 검증(DIO/Heating/Gas polling, GST 송수신, PLC read/write, HR121 config apply, USB A/B 업데이트/롤백)이 모두 완료되었다. `Release_SlotA`/`Release_SlotB` 빌드 구성도 개별 빌드 확인 완료.

## SDD / Subagent 구현 계획

SDD는 구현 계획을 실행할 때 사용하는 방식이다. 적용 전에는 사용자 승인이 필요하다.

구현 시작 전에 다음과 같이 확인한다.

```text
이 구현은 SDD가 적합합니다. SDD로 진행할까요?
```

사용자가 승인한 경우에만 subagent 기반으로 진행한다.

### Subagent 분할안

| 작업 | 구현 subagent 역할 | 리뷰 subagent 확인 항목 |
|---|---|---|
| IMP-002 | comm config 구현 | 기본값, active ID, Flash/PLC 설정 제외 여부 |
| IMP-003 | shared map 구현 | key 정책, GST global, pending 폐기, 동시 접근 |
| IMP-004 | poll/write table 구현 | 사양서 주소, function code, profile 매핑 |
| IMP-005~IMP-006 | UART4 slave 전환 | read/write 정책, 즉시 ACK 조건, 응답 없음 조건, heartbeat |
| IMP-007 | 하위 master task 구현 | `LPUART1_Task`/`USART2_Task`/`USART3_Task`, pending 우선, 순차 poll, timeout/retry, 상태 bit |
| IMP-008 | GST 3-task map 연동 | `USART1_RxTask`/`USART1_ProtocolTask`/`USART1_TxTask`, ID 미사용, `201~241`, `242~282`, bit 3 |
| IMP-009~IMP-010 | 정리 및 검증 | bridge 폐기, 빌드, 테스트 기록 |

### SDD 진행 규칙

- 각 구현 작업은 독립 subagent에 맡긴다.
- 작업 완료 후 별도 리뷰 subagent가 요구사항, 코드 품질, 회귀 가능성, 테스트 누락을 확인한다.
- 리뷰 없이 다음 구현 단계로 넘어가지 않는다.
- 리뷰 결과 수정이 필요하면 같은 단계에서 수정 후 재검토한다.
- subagent 사용이 불가능한 환경이면 메인 에이전트가 같은 작업 분할을 따르고, 각 단계마다 자체 리뷰 체크를 수행한다.
- 구현, 리뷰, 검증을 진행한 뒤에는 `ai_work_documents/checklists/03-implementation-completion-checklist.md`에 작업 상태, 리뷰 상태, 검증 상태, 메모를 갱신한다.

### Subagent 전달 패킷

각 구현 subagent에는 다음 정보를 전달한다.

- 관련 문서: `01-requirements.md`, `02-architecture.md`, `03-interfaces.md`, `04-data-model.md`, `05-protocol.md`, `06-design-decisions.md`.
- 해당 단계 ID와 완료 기준.
- 수정 허용 파일 범위.
- 금지 사항:
  - 문서 범위 밖 기능 추가 금지.
  - transparent bridge hub 재도입 금지.
  - USART1/GST에 Modbus ID 도입 금지.
  - write 실패 값을 desired/actual로 보존하는 구조 도입 금지.

## 구현 완료 기준

- 문서의 Confirmed 요구사항과 설계 결정이 코드에 반영된다.
- 빌드 결과가 확인된다.
- 장비 없이 가능한 검증과 장비 필요 검증이 구분되어 기록된다.
- 폐기된 bridge hub 경로가 새 경로와 충돌하지 않는다.
- `ai_work_documents/documents/` 문서와 코드가 서로 모순되지 않는다.

## 변경 이력

- 2026-06-27: 통합 map 기반 통신 구조 구현 계획과 SDD/subagent 적용 계획 최초 작성.
- 2026-06-27: 구현/리뷰/검증 후 완료 체크리스트 갱신 규칙 추가.
- 2026-06-27: 통신 task 구성을 `UART4_Task`, 하위 3포트 task, USART1 GST 3-task 구조로 구체화.
- 2026-06-27: USART1/GST 공용 영역의 통합 map 동기화 주체를 USART1 GST task로 명확화.
- 2026-06-28: 7-task 현재 구현 상태, `Application_CreateTasks()` 생성 구조, timeout/fail 실패 기준, USART1 GST link 상태 처리 문구 갱신.
- 2026-06-30: 현재 코드 기준으로 즉시 ACK write 정책, active ID 기본값, CommConfig timeout 필드를 갱신.
- 2026-07-06: IMP-013(런타임 Config Apply, HR110~122) 추가. IMP-002 완료 기준의 "PLC register 설정 변경 미포함" 문구를 IMP-013 참조로 정정.
- 2026-07-06: IMP-014(USB 펌웨어 업데이트, 부트로더 A/B 이중 슬롯 + C# GUI) 추가. IWDG/ConfirmBootOk/Release_SlotA·SlotB 빌드 구성을 "현재 구현 상태"에 반영, 실 보드 검증 전.
- 2026-07-30: IMP-007/IMP-008/IMP-010/IMP-014 실 장비 검증 완료를 "현재 구현 상태"에 반영. 관련 상태는 `checklists/03-implementation-completion-checklist.md`에도 갱신.
