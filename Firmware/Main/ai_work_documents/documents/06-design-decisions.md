# Design Decisions

## 목적

중요한 설계 결정, 선택 이유, 대안, 포기한 선택지, 결정 일자를 ID 기반으로 누적 관리한다.

## 결정 목록

| ID | 제목 | 상태 | 결정일 | 관련 문서 |
|---|---|---|---|---|
| DEC-001 | UART 수신은 DMA circular + IDLE callback 공통 계층으로 처리 | Accepted | 2026-06-26 | `02-architecture.md`, `03-interfaces.md`, `04-data-model.md` |
| DEC-002 | Transparent bridge hub 기능은 폐기 | Accepted | 2026-06-27 | `01-requirements.md`, `02-architecture.md`, `05-protocol.md` |
| DEC-003 | UART4는 PLC용 Modbus RTU slave로 유지 | Accepted | 2026-06-27 | `01-requirements.md`, `05-protocol.md` |
| DEC-004 | LPUART1, USART2, USART3는 Modbus RTU master로 동작 | Accepted | 2026-06-27 | `02-architecture.md`, `05-protocol.md` |
| DEC-005 | USART1은 GST 프로토콜 전용이며 Modbus master/slave가 아님 | Accepted | 2026-06-27 | `01-requirements.md`, `05-protocol.md` |
| DEC-006 | PLC-visible 데이터는 통합 메모리 맵으로 관리 | Accepted | 2026-06-27 | `04-data-model.md`, `03-interfaces.md` |
| DEC-007 | map key는 ID, table type, address로 구성 | Accepted | 2026-06-27 | `04-data-model.md`, `05-protocol.md` |
| DEC-008 | 미정의 entry와 실패 요청은 응답하지 않음 | Accepted | 2026-06-27 | `05-protocol.md`, `03-interfaces.md` |
| DEC-009 | PLC write는 하위 장치 write 성공 후 응답 | Superseded by DEC-017 | 2026-06-27 | `01-requirements.md`, `02-architecture.md` |
| DEC-010 | 통신 설정은 컴파일 기본값과 RAM 설정값으로 관리 | Accepted | 2026-06-27 | `04-data-model.md`, `03-interfaces.md` |
| DEC-011 | 포트별 period 기본값은 모두 50 ms | Accepted | 2026-06-27 | `01-requirements.md`, `04-data-model.md` |
| DEC-012 | 실제 사용 ID만 active_id_list로 관리 | Accepted | 2026-06-27 | `01-requirements.md`, `04-data-model.md`, `05-protocol.md` |
| DEC-013 | USART1/GST 영역은 ID 없는 global map으로 처리 | Accepted | 2026-06-27 | `01-requirements.md`, `04-data-model.md`, `05-protocol.md` |
| DEC-014 | write 실패 시 pending 값을 폐기 | Superseded by DEC-017 | 2026-06-27 | `01-requirements.md`, `04-data-model.md`, `05-protocol.md` |
| DEC-015 | 구현은 implementation-plan 단계와 SDD 승인 규칙을 따른다 | Accepted | 2026-06-27 | `07-implementation-plan.md`, `../agent-guides/03-sdd.md` |
| DEC-016 | 통신 task는 UART4/하위 3포트/USART1 3-task 구조로 구성 | Accepted | 2026-06-27 | `02-architecture.md`, `03-interfaces.md`, `05-protocol.md` |
| DEC-017 | PLC write는 SharedMap 즉시 ACK + 백그라운드 슬레이브 전달 | Accepted | 2026-06-28 | `03-interfaces.md`, `05-protocol.md` |
| DEC-018 | Modbus master는 TX/RX 큐를 분리하고 TxStep/RxStep/ProcessStep 3단계로 동작 | Accepted | 2026-06-28 | `03-interfaces.md`, `05-protocol.md` |
| DEC-019 | SharedMap 슬레이브 엔트리는 Discovery 완료 후 실제 ID로 동적 등록 | Accepted | 2026-06-28 | `04-data-model.md`, `03-interfaces.md`, `05-protocol.md` |
| DEC-020 | SharedMap 저장 영역은 RAM2로 분리 | Accepted | 2026-06-30 | `04-data-model.md`, `08-implementation-notes.md` |
| DEC-021 | UART4 PLC timeout 시 DIO 출력 fail-safe 0 처리 | Accepted | 2026-06-30 | `01-requirements.md`, `05-protocol.md` |
| DEC-022 | 런타임 통신 설정(active ID/Gas profile)을 PLC HR110~122로 변경 가능 | Accepted | 2026-07-06 | `03-interfaces.md`, `04-data-model.md`, `05-protocol.md` |
| DEC-023 | USB 펌웨어 업데이트: 바이트 스왑 대신 슬롯 고정 링크 A/B + 앱 상주 USB 트리거 + IWDG 활성화 | Accepted | 2026-07-06 | `02-architecture.md`, `03-interfaces.md`, `04-data-model.md`, `05-protocol.md` |

## 상세 결정

### DEC-001 - UART 수신은 DMA circular + IDLE callback 공통 계층으로 처리

- 결정: 모든 UART 수신은 공통 DMA circular + IDLE callback 기반 계층으로 통합한다.
- 상태: Accepted
- 결정일: 2026-06-26
- 이유: 여러 protocol의 가변 길이 frame을 동일한 방식으로 수신하고 task에서 소비하기 위함이다.
- 대안: UART별 blocking receive, byte interrupt 기반 parser, protocol별 DMA 처리.
- 영향 범위: UART4 slave, LPUART1/USART2/USART3 Modbus master, USART1 GST task.
- 관련 요구사항: REQ-002

### DEC-002 - Transparent bridge hub 기능은 폐기

- 결정: UART4 수신 데이터를 하위 UART로 그대로 보내는 bridge hub 기능은 사용하지 않는다.
- 상태: Accepted
- 결정일: 2026-06-27
- 이유: PLC와 하위 장치 사이의 데이터 일관성을 통합 map에서 관리하고, 하위 통신을 Interface Board가 주기적으로 제어하기 위함이다.
- 대안: 기존 주소 기반 bridge worker forwarding 구조 유지.
- 포기한 선택지: transparent bridge hub.
- 영향 범위: UART4 처리 방식, 하위 UART task 구조, Modbus route table.
- 관련 요구사항: REQ-004

### DEC-003 - UART4는 PLC용 Modbus RTU slave로 유지

- 결정: UART4는 PLC 요청을 받는 Modbus RTU slave 역할만 수행한다.
- 상태: Accepted
- 결정일: 2026-06-27
- 이유: PLC와 Interface Board 사이의 외부통신프로토콜 사양서 주소 체계를 유지하기 위함이다.
- 대안: UART4를 단순 bridge input 또는 master로 변경.
- 포기한 선택지: UART4 polling 구조.
- 영향 범위: UART4 task, Modbus parser, map read/write 처리.
- 관련 요구사항: REQ-003

### DEC-004 - LPUART1, USART2, USART3는 Modbus RTU master로 동작

- 결정: LPUART1, USART2, USART3는 하위 장치에 대해 Modbus RTU master로 동작한다.
- 상태: Accepted
- 결정일: 2026-06-27
- 이유: 하위 장치 값을 주기적으로 통합 map에 갱신하고 PLC write를 하위 장치에 반영하기 위함이다.
- 포트 배정: LPUART1은 DIO, USART2는 Heating Jacket, USART3는 Gas Detector.
- 영향 범위: `LPUART1_Task`, `USART2_Task`, `USART3_Task`, poll list, pending write 처리.
- 관련 요구사항: REQ-005

### DEC-005 - USART1은 GST 프로토콜 전용이며 Modbus master/slave가 아님

- 결정: USART1은 RS422 GST protocol 전용으로 사용하며 `USART1_RxTask`, `USART1_ProtocolTask`, `USART1_TxTask` 3개 task로 구성한다.
- 상태: Accepted
- 결정일: 2026-06-27
- 이유: USART1은 사용자 프로토콜 peer와 GST frame을 송수신하는 채널이며 Modbus RTU 주소 체계로 직접 동작하지 않는다.
- 대안: USART1도 Modbus master 또는 slave map을 갖도록 구성.
- 포기한 선택지: USART1 Modbus master/slave 역할.
- 영향 범위: USART1 GST 3-task 구조, 통합 map의 199~322 영역.
- 관련 요구사항: REQ-006, REQ-011

### DEC-006 - PLC-visible 데이터는 통합 메모리 맵으로 관리

- 결정: PLC가 읽고 쓰는 모든 데이터는 통합 메모리 맵에 저장한다.
- 상태: Accepted
- 결정일: 2026-06-27
- 이유: `UART4_Task`, 하위 Modbus task, USART1 GST task 사이의 공유 데이터를 한 곳에서 일관 관리하기 위함이다.
- 대안: 포트별 별도 map, protocol별 local buffer.
- 포기한 선택지: `gst_holdings` 같은 특정 프로토콜 전용 map만 PLC에 노출.
- 영향 범위: data model, `UART4_Task` 응답 처리, 하위 task 갱신 처리.
- 관련 요구사항: REQ-007, REQ-010

### DEC-007 - map key는 ID, table type, address로 구성

- 결정: 통합 map entry key는 `id + table_type + address`로 구성한다.
- 상태: Accepted
- 결정일: 2026-06-27
- 이유: 같은 주소 숫자라도 slave ID와 Modbus 영역이 다르면 다른 데이터이기 때문이다.
- 대안: address만 key로 사용하는 단순 배열.
- 포기한 선택지: address-only map.
- 영향 범위: map lookup, PLC 요청 처리, 하위 polling/write 처리.
- 관련 요구사항: REQ-007

### DEC-008 - 미정의 entry와 실패 요청은 응답하지 않음

- 결정: map entry가 없거나 허용되지 않는 ID이면 UART4는 응답하지 않는다. PLC write는 DEC-017 기준으로 SharedMap 커밋 성공 시 즉시 ACK한다.
- 상태: Accepted
- 결정일: 2026-06-27
- 이유: 사용자가 timeout 기반 실패 판단 정책을 선택했기 때문이다.
- 대안: Modbus exception 응답 사용.
- 포기한 선택지: `Illegal Data Address`, `Gateway Target Device Failed to Respond` 같은 exception 응답.
- 영향 범위: UART4 slave error path, PLC timeout 설정, 즉시 ACK write path.
- 관련 요구사항: REQ-008, REQ-009, REQ-010

### DEC-009 - PLC write는 하위 장치 write 성공 후 응답

- 결정: PLC write 요청은 하위 장치 write 성공이 확인된 후 정상 응답한다.
- 상태: Accepted
- 결정일: 2026-06-27
- 이유: PLC가 실제 하위 장치 반영 성공 여부를 UART4 응답으로 판단해야 하기 때문이다.
- 대안: map write만 성공하면 즉시 ACK하고 하위 write는 비동기 처리.
- 포기한 선택지: fire-and-forget map write.
- 영향 범위: pending write 상태, `UART4_Task` 응답 대기, 하위 Modbus task 동기화.
- 관련 요구사항: REQ-009

### DEC-010 - 통신 설정은 컴파일 기본값과 RAM 설정값으로 관리

- 결정: timeout, retry, period는 compile-time macro 기본값을 부팅 시 RAM config로 복사해 사용한다.
- 상태: Accepted
- 결정일: 2026-06-27
- 이유: 초기 시험 단계에서 Flash 저장이나 PLC 설정 register 없이 값을 조정할 수 있게 하기 위함이다.
- 대안: compile-time constant만 사용, Flash 저장, PLC register 설정.
- 포기한 선택지: 초기 구현에서 Flash persistence 제공.
- 영향 범위: config module, 모든 통신 task.
- 관련 요구사항: REQ-014

### DEC-011 - 포트별 period 기본값은 모두 50 ms

- 결정: LPUART1, USART2, USART3, USART1의 period 기본값은 모두 50 ms로 한다.
- 상태: Accepted
- 결정일: 2026-06-27
- 이유: 초기 시험 기준을 단순화하고, 이후 RAM 설정값으로 10~100 ms 범위를 확인하기 위함이다.
- 대안: 포트별 다른 기본값, ID별 period.
- 포기한 선택지: ID별 polling period.
- 영향 범위: RAM config, `LPUART1_Task`, `USART2_Task`, `USART3_Task`, `USART1_ProtocolTask` scheduling.
- 관련 요구사항: REQ-014

### DEC-012 - 실제 사용 ID만 active_id_list로 관리

- 결정: 허용 ID 범위 전체를 polling하지 않고, 실제 사용 ID만 active_id_list에 등록해 polling과 PLC 응답을 허용한다.
- 상태: Accepted
- 결정일: 2026-06-27
- 이유: 허용 ID 전체를 순회하면 없는 장치로 인한 timeout과 통신 부하가 커지고, 최신값이 보장되지 않는 ID에 PLC가 접근할 수 있기 때문이다.
- 정책:
  - polling은 active_id_list에 포함된 ID만 수행한다.
  - 기본 active ID는 DIO `{1, 2}`, Heating Jacket `{20, 21, 22}`, Gas Detector `{31, 40, 30}`이다.
  - Gas Detector profile은 Discovery 결과에 따라 H2 Detector 또는 H2 Detector(TFT)로 판별한다.
  - PLC read/write도 active_id_list에 포함된 ID만 응답한다.
  - 허용 범위 안이어도 active_id_list에 없으면 응답하지 않는다.
  - pending write는 read polling보다 우선 처리한다.
  - read polling은 외부통신프로토콜 사양서의 장치별 주소 표 순서대로 처리한다.
- 대안: 허용 범위 전체 ID를 항상 polling, PLC 요청은 허용 범위 전체에 응답.
- 포기한 선택지: 전체 ID polling.
- 영향 범위: RAM config, UART4 slave 응답 조건, 하위 Modbus master scheduler.
- 관련 요구사항: REQ-016

### DEC-013 - USART1/GST 영역은 ID 없는 global map으로 처리

- 결정: USART1/GST 영역 `199~322`는 Modbus slave ID와 무관한 global map으로 처리한다.
- 상태: Accepted
- 결정일: 2026-06-27
- 이유: USART1은 GST protocol 전용이며 Modbus master/slave가 아니므로 ID 개념을 부여하지 않는다.
- 정책:
  - USART1/GST protocol 자체에는 ID가 없다.
  - `USART1_ProtocolTask`가 GST peer와 송수신하며 `201~282` 영역을 통합 map에 동기화한다.
  - UART4 Modbus request가 `199~322` 영역에 접근하면 slave ID와 무관하게 동일한 global GST map entry를 사용한다.
  - UART4는 USART1 GST frame 송수신을 수행하지 않는다.
  - 이 영역에는 active_id_list를 적용하지 않는다.
- 대안: Interface Board 전용 Modbus ID를 두는 방식.
- 포기한 선택지: GST 영역에 ID를 부여하는 방식.
- 영향 범위: UART4 slave map lookup, 통합 map key 정책, RAM config.
- 관련 요구사항: REQ-011

### DEC-014 - write 실패 시 pending 값을 폐기

- 결정: 하위 장치 write가 실패하면 `pending_value`를 폐기하고 UART4는 응답하지 않는다.
- 상태: Superseded by DEC-017
- 결정일: 2026-06-27
- 이유: 실패한 write 값을 desired/actual로 유지하지 않고, 마지막 확정값과 pending 상태를 단순하게 관리하기 위함이다.
- 정책:
  - PLC write 수신 시 `pending_value`와 `dirty`를 설정한다.
  - 하위 write 성공 시 `value = pending_value`, `dirty` clear, UART4 ACK.
  - 하위 write 실패 또는 retry 초과 시 `pending_value` 폐기, `dirty` clear, UART4 응답 없음.
- 대안: rollback 기록 유지, desired/actual 이중값 유지.
- 포기한 선택지: desired/actual 분리.
- 영향 범위: pending write 상태, `UART4_Task` 응답 대기, 하위 Modbus task 실패 처리.
- 관련 요구사항: REQ-009

### DEC-015 - 구현은 implementation-plan 단계와 SDD 승인 규칙을 따른다

- 결정: 실제 코드 구현은 `07-implementation-plan.md`의 `IMP-001~IMP-010` 순서와 완료 기준을 따른다.
- 상태: Accepted
- 결정일: 2026-06-27
- 이유: 통신 구조 변경 범위가 크므로 shared map, UART4 slave, 하위 master, GST 연동을 단계별로 분리해 회귀 위험을 줄이기 위함이다.
- SDD 정책:
  - SDD/subagent 방식은 구현 시작 전에 사용자 승인을 받은 경우에만 적용한다.
  - 승인 시 각 구현 단계는 구현 subagent와 리뷰 subagent를 분리한다.
  - 리뷰 없이 다음 구현 단계로 넘어가지 않는다.
  - subagent를 사용할 수 없는 환경이면 메인 에이전트가 같은 단계 분할과 자체 리뷰 체크를 수행한다.
- 대안: 메인 에이전트가 전체 구현을 한 번에 수행.
- 포기한 선택지: 단계/리뷰 없는 일괄 구현.
- 영향 범위: 구현 진행 방식, 검증 보고, 리뷰 절차.
- 관련 문서: `07-implementation-plan.md`, `ai_work_documents/agent-guides/03-sdd.md`

### DEC-016 - 통신 task는 UART4/하위 3포트/USART1 3-task 구조로 구성

- 결정: 통신 task는 `UART4_Task`, `LPUART1_Task`, `USART2_Task`, `USART3_Task`, `USART1_RxTask`, `USART1_ProtocolTask`, `USART1_TxTask`로 구성한다.
- 상태: Accepted
- 결정일: 2026-06-27
- 이유: PLC 상위 통신, 하위 Modbus polling/write, USART1 GST 전용 송수신을 역할별로 분리하고 통합 메모리 맵을 중심으로 데이터 흐름을 고정하기 위함이다.
- 정책:
  - `UART4_Task`는 PLC 요청을 수신하고 통합 메모리 맵 기준으로 read 응답 또는 write pending을 생성한다.
  - `LPUART1_Task`, `USART2_Task`, `USART3_Task`는 각 하위 장치와 주기적으로 송수신하며 하위 장치 데이터와 통합 메모리 맵을 동기화한다.
  - USART1은 Modbus master/slave가 아니라 GST 전용 protocol endpoint로 동작한다.
  - `USART1_RxTask`는 GST 수신 queue를 생산하고, `USART1_ProtocolTask`는 GST packet 해석과 map 동기화 및 송신 queue 생성을 담당하며, `USART1_TxTask`는 GST write frame 송신을 담당한다.
  - GST 공용 영역 `199~322`의 실제 송수신과 map 동기화는 USART1 GST task가 담당한다.
  - `UART4_Task`는 GST 공용 영역 요청을 USART1로 Modbus frame forwarding하지 않는다.
- 대안: 단일 application loop에서 모든 통신 step을 순차 실행, USART1도 하위 Modbus bridge worker로 구성.
- 포기한 선택지: `Application_RunOnce()` 단일 주기 호출 구조, USART1 Modbus bridge worker 구조.
- 영향 범위: FreeRTOS task 생성, application 초기화, 통합 map 동기화, UART4 pending write 처리, USART1 GST queue 구성.
- 관련 요구사항: REQ-001, REQ-003, REQ-005, REQ-006, REQ-011

### DEC-017 - PLC write는 SharedMap 즉시 ACK + 백그라운드 슬레이브 전달

- 결정: PLC write 수신 시 SharedMap에 즉시 커밋하고 UART4로 ACK를 전송한다. 슬레이브로의 실제 write는 tx_queue를 통해 비동기 전달(fire-and-forget)한다.
- 상태: Accepted
- 결정일: 2026-06-28
- Supersedes: DEC-009, DEC-014
- 이유: 슬레이브 응답 대기(최대 response_timeout × retry_count)가 PLC 응답 지연으로 이어지는 문제를 해결하기 위함이다. PLC 응답은 SharedMap 기준이므로 즉시 ACK가 가능하다.
- write 실패 피드백: 슬레이브 상태 레지스터(holding 131~145)와 통신 상태(199번지)로 PLC가 조회한다.
- 대안: 슬레이브 write 성공 후 ACK (DEC-009 기존 방식).
- 포기한 선택지: 슬레이브 응답 대기 블로킹 ACK.
- 영향 범위: `UART4_Task` write 처리, `ModbusMaster_ForwardWrite`, tx_queue.
- 관련 요구사항: REQ-009

### DEC-018 - Modbus master는 TX/RX 큐를 분리하고 TxStep/RxStep/ProcessStep 3단계로 동작

- 결정: Modbus master의 단일 `ModbusMaster_Step`을 `TxStep`, `RxStep`, `ProcessStep` 3개 함수로 분리하고, 송신 큐(tx_queue)와 수신 큐(rx_queue)를 별도로 운용한다.
- 상태: Accepted
- 결정일: 2026-06-28
- 이유: 단일 큐로 송수신을 처리하면 poll 응답 대기 중 write 처리가 지연된다. 분리하면 TxStep은 tx_queue에서 write 요청을 즉시 처리하고 RxStep은 UART 수신을 큐에 적재하며 ProcessStep은 rx_queue를 파싱해 SharedMap을 갱신한다.
- 큐 구조:
  - tx_queue: `ModbusSlaveWriteRequest_t`, depth 4 (UART4_Task → LPUART1/USART2/USART3 Task write 전달)
  - rx_queue: `ModbusMasterRxMsg_t`, depth 4 (UART DMA 수신 → 응답 파싱 대기)
- 단계별 역할:
  - TxStep: waiting_response 확인 → timeout 시 RetryOrGivePoll → tx_queue write 처리 → poll 주기 도래 시 SendPollRequest
  - RxStep: waiting_response 중 Uart_GetData → rx_queue에 적재
  - ProcessStep: rx_queue dequeue → ParseReadResponse → SharedMap 갱신 또는 RetryOrGivePoll
- 대안: 단일 `ModbusMaster_Step` + blocking Uart_WaitData (이전 방식).
- 포기한 선택지: 단일 큐 blocking poll.
- 영향 범위: `a_modbus_master.h/c`, `LPUART1_Task`, `USART2_Task`, `USART3_Task`.
- 관련 요구사항: REQ-005, REQ-014

### DEC-019 - SharedMap 슬레이브 엔트리는 Discovery 완료 후 실제 ID로 동적 등록

- 결정: DIO/Heating/Gas 슬레이브 엔트리는 SharedMap_Init() 시점에 생성하지 않는다. 각 포트 Discovery 완료 후 SharedMap_RegisterSlave(actual_id, group)를 호출해 실제 하드웨어 ID로 등록한다.
- 상태: Accepted
- 결정일: 2026-06-28
- 이유: slave ID는 0~255 중 하드웨어 설정값이므로 컴파일 타임에 알 수 없다. ID 범위 기반 pre-allocation은 임의 ID에 대응할 수 없으며, comm_config IDs를 실제 하드웨어 ID와 일치시키면 Discovery 후 실제 ID로 등록이 가능하다.
- 슬롯 수: DIO=2, Heating=3, Gas=3. comm_config 기본 ID는 DIO `{1, 2}`, Heating `{20, 21, 22}`, Gas `{31, 40, 30}`.
- Gas 엔트리 정책: H2와 TFT 주소 합집합으로 등록 (Coil 1~4, DI 1~16, DI 10001~10008, IR TFT blocks, IR 30000~30003, HR TFT blocks). Discovery로 타입이 판별되어도 주소는 양쪽 모두 등록해 어느 타입이든 PLC 조회 가능.
- 등록 순서: Discovery 후 발견된 ID를 오름차순 정렬 → 낮은 ID 우선 등록 (폴링 우선순위 반영).
- pool 크기: 660 entries (DIO 4 + Heating 171 + Gas 330 + Global 155), _Static_assert로 검증.
- PLC 조회 방식: PLC는 실제 slave ID로 SharedMap 조회. SharedMap 키는 기존과 동일하게 (actual_id, table, address).
- 대안: 슬롯 인덱스(1~N)로 pre-allocate, PLC도 슬롯 인덱스로 조회.
- 포기한 선택지: 슬롯 인덱스 기반 pre-allocation (PLC가 공표 레지스터로 실제 ID를 별도 조회해야 하는 복잡성 때문에 포기).
- 영향 범위: `a_shared_map.c/h`, `a_modbus_master.c` (DiscoverSlaves 완료 후 RegisterSlave 호출).
- 관련 결정: DEC-007, DEC-012

### DEC-020 - SharedMap 저장 영역은 RAM2로 분리

- 결정: SharedMap entry pool과 관련 상태 변수는 SRAM1 기본 `.bss`가 아니라 RAM2 전용 section에 배치한다.
- 상태: Accepted
- 결정일: 2026-06-30
- 이유: 현재 SRAM1 사용률이 높고 SharedMap pool이 약 10 KB 이상을 차지하므로, 사용 중인 RAM2 32 KB 영역으로 분리해 SRAM1 여유를 확보하기 위함이다.
- 배치 대상: `s_entries`, `s_entry_count`, `s_initialized`, `s_current_time_ms`, `s_comm_failure_counts`.
- 링커 정책: `.sharedmap_ram2 (NOLOAD)` section을 `RAM2`에 배치한다.
- 초기화 정책: `SharedMap_Init()`에서 SharedMap 내부 상태를 명시 초기화하므로 startup의 기본 `.bss` zero clear 대상에 포함되지 않아도 된다.
- 영향 범위: `a_shared_map.c`, `STM32L452VETX_FLASH.ld`, 필요 시 `STM32L452VETX_RAM.ld`.
- 관련 결정: DEC-006, DEC-019.

### DEC-021 - UART4 PLC timeout 시 DIO 출력 fail-safe 0 처리

- 결정: UART4 PLC 통신이 `APP_UART4_WATCHDOG_TIMEOUT_MS` 동안 수신되지 않으면 DIO active slave 전체에 `HR129=0` write를 전달한다.
- 상태: Accepted
- 결정일: 2026-06-30
- timeout은 SharedMap/PLC register가 아니라 compile-time `#define`으로 조정한다.
- 기본값은 `5000 ms`이며, `0U`은 watchdog 비활성으로 사용한다. (2026-06-30 최초 `1000 ms` → 2026-08-03 `5000 ms`로 변경)
- 메모리 맵에는 신규 주소를 추가하지 않는다.
- 이유: PLC 통신 장애 시 DIO 출력이 마지막 상태로 유지되는 위험을 줄이기 위함이다.
- 구현 위치: `a_application.c`의 UART4 task loop와 기존 `ModbusMaster_ForwardWrite()` 경로.
- 관련 결정: DEC-003, DEC-006, DEC-017, DEC-018.

### DEC-022 - 런타임 통신 설정(active ID/Gas profile)을 PLC HR110~122로 변경 가능

- 결정: DIO/Heating/Gas active ID와 Gas profile 힌트는 PLC가 HR `110~120`에 값을 쓰고 HR `121`(trigger)에 `1`을 write하면 재부팅 없이 런타임으로 변경할 수 있다. 결과는 HR `122`에 코드로 기록된다.
- 상태: Accepted
- 결정일: 2026-07-06 (코드에는 이미 구현되어 있었으나 `documents/` 각 문서에 "Flash 저장과 PLC register 설정 변경은 초기 구현 범위에서 제외"로 잘못 기록되어 있던 것을 코드 감사 중 발견하고 소급 반영)
- 이유: 배포 후 현장에서 슬레이브 ID 배치가 바뀌는 경우에도 재컴파일 없이 대응하기 위함.
- 정책:
  - 검증(ID `0` 또는 `1~247`, 그룹 내 `0` 아닌 ID 중복 불허, Gas profile `0~2` 범위) 실패 시 기존 active list는 유지되고 결과 코드만 기록된다.
  - 성공 시 `CommConfig_t.revision`이 증가하고, 해당 하위 포트 task가 이를 감지해 재-Discovery를 수행한다.
  - Flash 비휘발성 저장은 제공하지 않는다. 전원 재인가 시 compile-time 기본값으로 복귀한다(DEC-010과 공존).
- 대안: Flash 저장 + 재부팅 방식.
- 포기한 선택지: 없음(기존 "설정 변경 미제공" 방침을 대체하는 결정이며, 대안 검토 기록은 남아 있지 않다).
- 영향 범위: `a_application.c`(`ServiceConfigApply`), `a_comm_config.c`(`CommConfig_ApplyDeviceSettings`), HR `110~122`.
- 관련 결정: DEC-010(컴파일 기본값 + RAM 설정), DEC-012(active_id_list 정책).
- 관련 요구사항: REQ-014, REQ-020

### DEC-023 - USB 펌웨어 업데이트: 바이트 스왑 대신 슬롯 고정 링크 A/B + 앱 상주 USB 트리거 + IWDG 활성화

- 결정: 512KB 단일뱅크 Flash에서 진짜 바이트 단위 스왑(MCUboot 방식)을 구현하는 대신, Slot A(0x0800C000)/Slot B(0x08044000)를 **각각 고정 실행주소로 링크한 별개의 앱 바이너리**로 취급한다. 현재 실행 중이 아닌 슬롯에만 새 이미지를 쓰고, 부트로더가 최종 CRC32 검증 후에만 그 슬롯을 활성화한다.
- 상태: Accepted
- 결정일: 2026-07-06
- 이유:
  - 스왑 방식은 전원 손실에도 안전하지만 섹터별 스크래치 스왑 상태머신이 필요해 구현 난이도와 버그(브릭) 위험이 크다.
  - 슬롯 고정 링크 방식은 실행 중인 슬롯을 절대 건드리지 않으므로, 업데이트 도중 정전/USB 단절이 나도 기존 펌웨어가 100% 보존된다 - 같은 안전 목표를 훨씬 낮은 구현 위험으로 달성한다.
  - 대가는 앱 릴리스 산출물이 슬롯별로 2개(`firmware_slotA.bin`/`_slotB.bin`) 필요하다는 것과, 앱 최대 크기가 224KB로 제한된다는 것(현재 사용량 대비 여유는 충분).
- 트리거 방식: 최초 설계안은 "부트로더가 매 부팅마다 짧은 USB 대기창을 두고 눈치보는" 방식이었으나, **앱이 이미 USB CDC를 상시 열어두고 있다**는 사실을 코드 감사로 확인한 뒤 폐기했다. 대신 앱이 정상 동작 중에 USB로 `ENTER_UPDATE` 프레임을 받으면 `.noinit_boot_flag`(소프트 리셋에도 보존되는 RAM)에 매직값을 세팅하고 확정적으로 재부팅하는 방식으로 변경했다 - 리셋 타이밍을 맞춰야 하는 레이스 컨디션이 없다.
- IWDG 활성화: 기존에 비활성 상태였던 IWDG(3초 타임아웃, Prescaler 64/Reload 1499/Window 4095)를 활성화했다. 새 펌웨어가 `Application_Init()` 도중 멈추거나 크래시하면 `Bootloader_ConfirmBootOk()`가 호출되지 못해 메타데이터가 계속 `PENDING_TEST`로 남는데, IWDG가 리셋시켜야 부트로더가 다음 부팅 때 이를 감지하고 자동 롤백할 수 있다 - IWDG 활성화 자체가 A/B 롤백 안전장치의 전제조건이다.
- 대안: MCUboot류 스왑 알고리즘, 부팅 시 USB 대기창 트리거.
- 포기한 선택지: 위 대안들은 구현 위험/UX 레이스 컨디션 문제로 채택하지 않았다(상세 비교는 최초 계획 수립 시 논의로 남아 있으며 별도 문서화하지 않음).
- 영향 범위: `Bootloader/`(신규 프로젝트), `Shared/bootloader_shared.h/.c`, `IF_main`의 `a_boot_link.c`/`a_bootloader_link.c`/링커스크립트 2종/`stm32l4xx_hal_conf.h`, `Tools/FirmwareUpdater/`.
- 관련 요구사항: REQ-021
- 검증 상태: 코드 작성 및 개별 빌드 확인 완료. 실 보드 테스트에서 여러 버그를 발견/수정했고(상세는 `08-implementation-notes.md`의 `IMP-014` 참고), 그중 근본 원인은 `Shared/bootloader_shared.h`가 두 프로젝트에 링크가 아닌 복사본으로 들어가 있어 SlotB 주소가 부트로더와 앱 사이에서 16KB 어긋난 것이었다. 이 수정 후 SlotA→SlotB 업데이트로 정상 부팅(초록 LED)까지 확인했다. A/B 왕복(SlotB→SlotA) 재업데이트, 정전/단절, IWDG 롤백 시나리오, Modbus 통신 확인은 아직 미검증.

## 미결정 사항

- 하위 장치 write best-effort 실패를 PLC가 어떤 상태 레지스터로 판단할지 운용 기준 검증.

## 변경 이력

- 2026-06-26: `interface_test/interface_main` 소스에서 설계 결정 초안 추출.
- 2026-06-27: 사용자 확정 내용을 기준으로 bridge 폐기, 통합 map, ID 정책, response 정책, RAM config 결정 반영.
- 2026-06-27: active_id_list 기반 polling 및 PLC 응답 제한 결정 반영.
- 2026-06-27: DIO `1~3`, Heating `1~7`, Gas `20~29`/`40~49` active ID 기본값 반영.
- 2026-06-27: USART1/GST global map과 write 실패 시 pending 폐기 결정 반영.
- 2026-06-27: `07-implementation-plan.md`와 SDD/subagent 적용 결정 반영.
- 2026-06-27: 통신 task를 `UART4_Task`, 하위 3포트 task, USART1 GST 3-task 구조로 구성하는 결정 추가.
- 2026-06-27: USART1/GST 공용 영역의 통합 map 동기화 주체를 USART1 GST task로 명확화.
- 2026-06-28: DEC-009를 Superseded로 변경. DEC-017(즉시 ACK + 백그라운드 전달), DEC-018(TX/RX 큐 분리 + 3단계 Step) 추가.
- 2026-06-28: DEC-019 추가. SharedMap 슬레이브 엔트리를 Discovery 완료 후 실제 ID로 동적 등록, Gas H2+TFT 합집합 pre-allocate, pool 660개, Heating/Gas 슬롯 수 3개로 확정.
- 2026-06-30: 현재 코드 기준으로 DEC-012/DEC-019 active ID 기본값을 갱신하고 DEC-014를 Superseded로 변경.
- 2026-06-30: DEC-020 추가. SharedMap 저장 영역을 RAM2 전용 section으로 분리하는 결정 반영.
- 2026-06-30: DEC-021 추가. UART4 PLC timeout 시 DIO 출력 fail-safe 0 처리 결정 반영.
- 2026-07-06: DEC-022 추가. 코드에 이미 구현되어 있던 런타임 Config Apply(HR110~122)를 소급 반영.
- 2026-07-06: DEC-023 추가. USB 펌웨어 업데이트의 슬롯 고정 링크 A/B 방식, 앱 상주 USB 트리거, IWDG 활성화 결정과 근거 반영.
- 2026-07-06: DEC-023 검증 상태 갱신. 실 보드 테스트에서 근본 원인(bootloader_shared.h 복사본 불일치로 인한 SlotB 16KB 주소 어긋남)을 찾아 수정하고 SlotB 정상 부팅을 확인.
