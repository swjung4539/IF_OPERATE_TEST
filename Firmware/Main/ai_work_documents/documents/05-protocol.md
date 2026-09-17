# Protocol

## 목적

UART, Modbus RTU, GST 사용자 프로토콜의 통신 프레임, 명령, 응답, 타임아웃, 예외 처리를 ID 기반으로 누적 관리한다.

## 프로토콜 항목 목록

| ID | 분류 | 이름 | 채널 | 상태 | 관련 요구사항 |
|---|---|---|---|---|---|
| PROTO-001 | 통신 채널 | UART 물리 채널 설정 | LPUART1, USART1, USART2, USART3, UART4 | Confirmed | REQ-001, REQ-002 |
| PROTO-002 | 프레임 형식 | PLC Modbus RTU Slave | UART4 | Confirmed | REQ-003, REQ-007, REQ-008, REQ-009 |
| PROTO-003 | 프레임 형식 | 하위 Modbus RTU Master | LPUART1, USART2, USART3 | Confirmed | REQ-005, REQ-014, REQ-015 |
| PROTO-004 | 프레임 형식 | GST 사용자 프로토콜 | USART1 | Confirmed | REQ-006, REQ-011 |
| PROTO-005 | 주소 정책 | 외부통신프로토콜 사양서 주소와 통합 map | 전체 | Confirmed | REQ-007, REQ-010, REQ-011 |
| PROTO-006 | 오류 정책 | 응답 없음과 통신 실패 판정 | 전체 | Confirmed | REQ-008, REQ-009, REQ-015 |
| PROTO-007 | 폐기 | Transparent bridge hub protocol | UART4 -> 하위 UART | Confirmed | REQ-004 |
| PROTO-008 | 스케줄 | 장치별 poll/write item | LPUART1, USART2, USART3 | Confirmed | REQ-017 |
| PROTO-009 | 안전 정책 | UART4 PLC watchdog fail-safe | UART4, LPUART1 | Confirmed | REQ-019 |
| PROTO-010 | 설정 프로토콜 | 런타임 Config Apply (HR110~122) | UART4 | Confirmed | REQ-020 |
| PROTO-011 | 업데이트 프로토콜 | USB 펌웨어 업데이트 프레임 프로토콜 | 앱/부트로더 USB CDC | Done | REQ-021 |
| PROTO-012 | 주소 정책 | UART4 PLC 4-그룹 통합 Read/Write 맵 | UART4 | Confirmed | REQ-003, REQ-007, REQ-010 |

## 구현 계획 참조

- 프로토콜 구현 순서는 `07-implementation-plan.md`의 `IMP-004~IMP-008`을 따른다.
- `UART4_Task` read/write path는 `IMP-005`, `IMP-006`에서 구현한다.
- 하위 Modbus master polling은 `LPUART1_Task`, `USART2_Task`, `USART3_Task` 기준으로 `IMP-007`에서 구현한다.
- USART1 GST 3-task map 연동은 `IMP-008`에서 구현한다.
- SDD/subagent 구현 시 protocol item table과 실제 코드 table의 주소/function code 일치 여부를 리뷰한다.

## 상세 프로토콜

### PROTO-001 - UART 물리 채널 설정

- 분류: 통신 채널
- 상태: Confirmed
- 채널:
  - UART4: PLC용 Modbus RTU slave, RS485, 115200 bps, 8N1.
  - LPUART1: DIO용 Modbus RTU master, RS485, 38400 bps, 8N1.
  - USART2: Heating Jacket용 Modbus RTU master, RS485, 19200 bps, 8N1.
  - USART3: Gas Detector용 Modbus RTU master, RS485, 9600 bps, even parity, 1 stop bit.
  - USART1: GST 사용자 프로토콜, RS422, 115200 bps, 8N1.
- 공통: no flow control, LSB first.
- 수신: DMA circular + IDLE callback 기반.
- 관련 요구사항: REQ-001, REQ-002

### PROTO-002 - PLC Modbus RTU Slave

- 분류: 프레임 형식
- 상태: Confirmed
- 채널: UART4
- 처리 task: `UART4_Task`
- 목적: PLC가 통합 map을 Modbus RTU로 읽고 쓴다.
- 프레임 형식: `[Slave ID][Function Code][Payload][CRC Lo][CRC Hi]`.
- ID 정책:
  - `0~247`: 허용.
  - `248~255`: 미허용.
  - `0`: broadcast가 아니라 일반 ID로 응답.
- 요청 처리:
  - Slave ID가 허용 범위인지 확인한다.
  - 대상 주소가 global 영역(설정 `110~125`, 슬레이브 상태 `131~145`, GST `199~322`)이 아니면 대상 장치군의 active_id_list에 Slave ID가 포함되는지 확인한다.
  - 대상 주소가 위 global 영역에 완전히 포함되면 Slave ID와 무관하게 해당 global map을 사용한다. 단, 이 global 판정은 FC03 Holding Register 요청에만 적용되며, 다른 function code로 global 영역 주소를 요청하면 응답하지 않는다.
  - 대상 주소가 GST global 영역 `199~322`이면 USART1로 Modbus frame을 forwarding하지 않는다.
  - GST global 영역의 실제 GST 송수신과 통합 map 동기화는 USART1 GST task가 담당한다.
  - 설정(`110~122`)/슬레이브 상태(`131~145`) global 영역은 UART4가 직접 SharedMap을 조회/갱신하며, 별도 하위 통신으로 forwarding하지 않는다.
  - `id + table_type + address`로 통합 map entry 조회.
  - entry가 있으면 function code와 접근 방향을 확인한다.
  - active ID가 아니거나 entry가 없으면 응답하지 않는다.
- Read 정책: 하위 장치에 즉시 read하지 않고 현재 map 값을 반환한다.
- Write 정책: SharedMap에 즉시 커밋한 뒤 PLC에 정상 ACK를 즉시 응답한다.
- 하위 write 전달 정책: PLC ACK 이후 대상 하위 장치 또는 GST peer로 best-effort 전달한다. 하위 전달 실패는 이미 전송한 PLC ACK를 되돌리지 않는다.
- 지원 function code:
  - FC01: Read Coils.
  - FC02: Read Discrete Inputs.
  - FC03: Read Holding Registers.
  - FC04: Read Input Registers.
  - FC05: Write Single Coil.
  - FC06: Write Single Register.
  - FC0F: Write Multiple Coils.
  - FC10: Write Multiple Registers.
- 관련 요구사항: REQ-003, REQ-007, REQ-008, REQ-009, REQ-010

### PROTO-003 - 하위 Modbus RTU Master

- 분류: 프레임 형식
- 상태: Confirmed
- 채널: LPUART1, USART2, USART3
- 처리 task:
  - `LPUART1_Task`: DIO 하위 장치와 송수신하며 통합 map을 동기화한다.
  - `USART2_Task`: Heating Jacket 하위 장치와 송수신하며 통합 map을 동기화한다.
  - `USART3_Task`: Gas Detector 하위 장치와 송수신하며 통합 map을 동기화한다.
- 목적: 하위 장치 값을 주기적으로 읽어 통합 map에 반영하고, PLC write 요청을 하위 장치로 전달한다.
- 포트:
  - LPUART1: DIO, active ID comm_config 기본 `{1, 2}` (슬롯 2개).
  - USART2: Heating Jacket, active ID comm_config 기본 `{20, 21, 22}` (슬롯 3개).
  - USART3: Gas Detector, active ID comm_config 기본 `{30, 40, 50}` (슬롯 3개, Gas Type 설정값으로 STD/TFT 결정).
- 요청:
  - Periodic read는 active_id_list와 port poll list 기준으로 수행한다.
  - Pending write가 있으면 대상 entry에 맞는 Modbus write function을 수행한다.
- 응답:
  - Read 성공 시 map value와 last update time을 갱신한다.
  - Write 성공/실패는 하위 전달 상태와 통신 상태에 반영한다. PLC ACK는 UART4 수신 시 이미 전송된다.
  - Write forward 전송 직후에는 해당 포트의 response timeout 동안 응답 대기 상태로 전환하며, 이 대기가 끝나기 전에는 같은 포트의 다음 pending write/poll을 전송하지 않는다 (RS485 반이중 버스 충돌 방지 목적).
  - Write forward는 read poll과 달리 재시도하지 않는다 (best-effort 정책). 대기 중 응답을 받으면 즉시, 응답이 없으면 response timeout 경과 시 로그만 남기고 다음 큐 항목으로 진행한다.
- 타이밍:
  - LPUART1 polling period 기본값: 50 ms.
  - USART2 polling period 기본값: 50 ms.
  - USART3 polling period 기본값: 50 ms.
  - TX timeout 기본값: 50 ms.
  - LPUART1 response timeout 기본값: 50 ms.
  - USART2 response timeout 기본값: 200 ms.
  - USART3 response timeout 기본값: 100 ms.
  - retry count 기본값: 3회 (read poll에만 적용. write forward는 재시도 없음).
- 순차 poll 정책:
  - pending write가 있으면 read polling보다 우선 처리한다.
  - pending write가 없으면 포트별 active_id_list의 현재 ID를 선택한다.
  - 선택한 ID 안에서 외부통신프로토콜 사양서의 장치별 주소 표 순서대로 poll item을 처리한다.
  - poll item이 끝나면 다음 poll item으로 이동한다.
  - 해당 ID의 poll item을 모두 처리하면 다음 active ID로 이동한다.
  - 마지막 active ID까지 처리하면 처음 active ID로 rollover한다.
- ID 정책:
  - 허용 ID 범위 전체를 polling하지 않는다.
  - active_id_list에 등록된 실제 사용 ID만 polling한다.
- 실패 처리:
  - 연속 3회 timeout/fail이면 해당 장치군 상태 bit를 0으로 설정한다.
  - 1회 성공하면 상태 bit를 1로 복구한다.
- 관련 요구사항: REQ-005, REQ-014, REQ-015

### PROTO-004 - GST 사용자 프로토콜

- 분류: 프레임 형식
- 상태: Confirmed
- 채널: USART1
- 목적: USART1 RS422 peer와 GST frame으로 데이터를 송수신한다.
- USART1 역할: GST protocol endpoint. Modbus master/slave가 아니다.
- task 구성:
  - `USART1_RxTask`: USART1 수신 데이터를 확인한다. 수신 frame이 있으면 GST 수신 queue에 넣고, queue가 가득 찬 경우 오래된 frame을 버리고 최신 frame 기준으로 재동기화한다. 짧은 주기, 예: 1 ms, 로 반복 실행한다.
  - `USART1_ProtocolTask`: GST 수신 queue에서 frame을 꺼내 GST packet을 해석한다. 수신된 GST read 영역 값은 통합 메모리 맵의 GST read 영역에 반영한다. 주기적으로 통합 메모리 맵의 GST write 영역 값을 읽어 GST 송신 frame 데이터를 만들고 GST 송신 queue에 넣는다. 수신 성공 여부와 연속 timeout/fail 기준으로 USART1/GST 통신 상태를 갱신한다.
  - `USART1_TxTask`: GST 송신 queue에서 송신 요청을 기다린다. 송신 요청이 들어오면 USART1로 GST write frame을 전송한다. 송신 실패 시 짧은 delay 후 재시도한다.
- 프레임 형식: `[Header 3][Function Code 1][Address Hi 1][Address Lo 1][Length 1][Data N][CRC16 2][Tail 2]`.
- 상수:
  - Header: `0x47 0x53 0x54` (`G`, `S`, `T`).
  - Tail: `0x53 0x49` (`S`, `I`).
  - CRC: Header부터 Data 끝까지 Modbus CRC16, Lo/Hi 순서.
- 명령:
  - `0x01`: Read.
  - `0x02`: Write.
- 주소:
  - `199`: 통신 상태 heartbeat bit flag.
  - `200`: PLC-UART4 heartbeat counter.
  - `201~241`: GST write 영역, 41개 register.
  - `242~282`: GST read 영역, 41개 register.
  - `283~322`: 예약 영역, read/write 허용, 기본값 0.
- 주기: USART1 period 기본값은 50 ms.
- 실패 처리: 연속 3회 timeout/fail이면 `199` bit 3을 0으로 설정하고, 1회 성공하면 1로 복구한다.
- UART4 연동: PLC는 UART4 Modbus 요청으로 GST 공용 영역 `199~322`의 map 값을 조회하거나 write 요청을 낼 수 있다. 이때 `UART4_Task`는 USART1로 Modbus frame을 전달하지 않으며, USART1 GST peer와의 송수신 및 GST 영역 map 동기화는 `USART1_ProtocolTask`가 담당한다.
- PLC write의 GST 전달 분기: UART4가 write를 즉시 commit/ACK한 뒤, 주소가 `201~241`(GST write 영역)이면 `GstProtocol_ForwardWrite()`가 실제 GST write frame을 GST peer에 전송하고 응답을 기다린다. 주소가 `283~322`(예약 영역)이면 GST peer로 실제 frame을 보내지 않고 로컬 SharedMap 커밋만으로 즉시 완료 처리한다.
- 관련 요구사항: REQ-006, REQ-011, REQ-012, REQ-014, REQ-015

### PROTO-005 - 외부통신프로토콜 사양서 주소와 통합 map

- 분류: 주소 정책
- 상태: Confirmed
- 목적: PLC 주소 체계는 외부통신프로토콜 사양서 숫자를 그대로 사용한다.
- map key: `id + table_type + address`.
- 영역:
  - Coil: `1`번부터 사용.
  - Discrete Input: `1`번부터 사용.
  - Input Register: `30000`번부터 사용.
  - Holding Register: `40000`번 영역 표기는 영역 설명용이며 실제 요청 address는 사양서 숫자를 그대로 저장한다.
- Static entry 정책:
  - 사양서에 있는 모든 주소는 static address template으로 구현한다.
  - 예약 영역 `283~322`도 read/write 가능한 entry로 구현한다.
  - 기본값은 0이다.
  - 런타임 자동 address template 생성은 하지 않는다.
  - ID별 응답과 polling은 active_id_list에 포함된 ID에 대해서만 허용한다.
- 슬레이브 엔트리 등록 정책:
  - DIO/Heating/Gas 엔트리는 SharedMap_Init() 시 생성하지 않음.
  - task 시작/설정 변경 시 SharedMap_RegisterSlave(actual_id, group) 호출로 config 기준 등록(probe 없음).
  - Gas는 H2와 TFT 주소 합집합을 등록 (Coil 1~4, DI 1~16, DI 10001~10008, IR TFT blocks, IR 30000~30003, HR TFT blocks).
- 사양서 기준 엔트리 범위:
  - DIO: Holding Register `129~130`, LPUART1, 슬롯 2개, active ID comm_config 기본 `{1, 2}`.
  - USART1/GST: Holding Register `199~322`, USART1, ID 없음/global. (init 시 등록)
  - Heating Jacket: Input Register `1000~1035`, Holding Register `2000~2020`, USART2, 슬롯 3개, active ID comm_config 기본 `{20, 21, 22}`.
  - Gas Detector: USART3, 슬롯 3개, active ID comm_config 기본 `{30, 40, 50}`. H2+TFT 주소 합집합 등록, 실제 타입은 Gas Type 설정값(`1=STD`, `2=TFT`, `0=동작 안 함`)으로 결정.
- Gas Detector 주소 주의:
  - H2 Detector 표는 `30000~30003`, `10001~10008`처럼 절대형 주소를 사용한다.
  - H2 Detector(TFT) 표는 table type 내부 주소 `1~96` 형태를 사용한다.
  - 내부 map은 `table_type + address`로 구분하므로, TFT 항목은 table type과 사양서 주소를 함께 저장한다.
- 관련 요구사항: REQ-007, REQ-010, REQ-011

### PROTO-006 - 응답 없음과 통신 실패 판정

- 분류: 오류 정책
- 상태: Confirmed
- 목적: PLC와 하위 장치 통신 오류 처리 방식을 정의한다.
- 응답 없음 조건:
  - CRC 오류.
  - frame 형식 오류.
  - ID `248~255` 요청.
  - GST global 영역이 아닌 요청에서 허용 범위 안이지만 active_id_list에 없는 ID 요청.
  - static map에 없는 `id + table_type + address` 요청.
- exception 정책: 기본 정책은 Modbus exception 응답 대신 응답 없음이다.
- 통신 실패 기준: 연속 3회 timeout/fail이면 비정상.
- 통신 복구 기준: 1회 성공하면 정상.
- `199` 상태 bit: 장치군별 정상/비정상 상태를 표시한다.
- 관련 요구사항: REQ-008, REQ-009, REQ-015

### PROTO-007 - Transparent bridge hub protocol

- 분류: 폐기
- 상태: Confirmed
- 목적: 이전 구조의 PLC request 원본 forwarding 방식은 사용하지 않음을 명확히 한다.
- 폐기 내용:
  - UART4 수신 frame을 주소 기반으로 하위 UART에 그대로 forwarding.
  - 하위 UART response를 CRC 검증 후 UART4로 그대로 반환.
  - bridge worker queue 기반 transparent routing.
- 대체 방식: `UART4_Task`는 PLC Modbus 요청을 통합 map 인터페이스에 연결하고, 하위 포트는 `LPUART1_Task`, `USART2_Task`, `USART3_Task`, USART1 GST 3-task 구조로 map을 갱신한다.
- 관련 요구사항: REQ-004

### PROTO-008 - 장치별 poll/write item

- 분류: 스케줄
- 상태: Confirmed
- 목적: 외부통신프로토콜 사양서의 장치별 주소 표를 기준으로 `LPUART1_Task`, `USART2_Task`, `USART3_Task`가 처리할 poll item과 write item을 정의한다.
- 기본 active ID:
  - DIO: comm_config 기본 `{1, 2}` (슬롯 2개).
  - Heating Jacket: comm_config 기본 `{20, 21, 22}` (슬롯 3개).
  - Gas Detector: comm_config 기본 `{30, 40, 50}` (슬롯 3개, Gas Type 설정값으로 STD/TFT 결정).
- 슬레이브 등록 정책: Discovery(probe)는 사용하지 않는다. task 시작 시와 설정 변경(revision 증가) 시 config의 ID/Type 값을 그대로 등록한다(`ModbusMaster_RegisterConfiguredSlaves()`). Gas Type이 `0(동작 안 함)`인 슬롯은 polling/등록 대상에서 제외한다.
- 공통 순서:
  - pending write item을 read poll item보다 우선 처리한다.
  - pending write가 없으면 active ID를 오름차순으로 선택한다.
  - 선택한 ID 안에서 아래 poll item 표의 순서대로 1개 item씩 처리한다.
  - 마지막 item 이후 다음 active ID로 이동하고, 마지막 active ID 이후 처음으로 rollover한다.

#### DIO - LPUART1

Active ID: comm_config 기본 `{1, 2}`, 슬롯 2개.

Poll item:

| 순서 | FC | Table type | 주소 | 수량 | 방향 | 항목 | 처리 |
|---:|---|---|---:|---:|---|---|---|
| 1 | FC03 | Holding Register | 130 | 1 | R | DI Read Command | 주기 polling |

Write item:

| 우선순위 | FC | Table type | 주소 | 수량 | 방향 | 항목 | 처리 |
|---:|---|---|---:|---:|---|---|---|
| 1 | FC10 | Holding Register | 129 | 1 | W | DO Write Command | PLC pending write 발생 시 우선 처리 |

#### Heating Jacket - USART2

Active ID: comm_config 기본 `{20, 21, 22}`, 슬롯 3개.

Poll item:

| 순서 | FC | Table type | 주소 | 수량 | 방향 | 항목 | 처리 |
|---:|---|---|---:|---:|---|---|---|
| 1 | FC04 | Input Register | 1000 | 18 | R | CH1~CH18 히터 온도 | 주기 polling |
| 2 | FC04 | Input Register | 1018 | 18 | R | CH1~CH18 알람 | 주기 polling |
| 3 | FC03 | Holding Register | 2000 | 21 | R/W | Machine, Save, Reset, CH1~CH18 SV readback | 주기 polling |

Write item:

| 우선순위 | FC | Table type | 주소 | 수량 | 방향 | 항목 | 처리 |
|---:|---|---|---:|---:|---|---|---|
| 1 | FC06 또는 FC10 | Holding Register | 2000 | 1 | R/W | Machine ON/OFF | PLC pending write 발생 시 우선 처리 |
| 1 | FC06 또는 FC10 | Holding Register | 2001 | 1 | R/W | EEPROM Save | PLC pending write 발생 시 우선 처리 |
| 1 | FC06 또는 FC10 | Holding Register | 2002 | 1 | R/W | ALARM Reset | PLC pending write 발생 시 우선 처리 |
| 1 | FC06 또는 FC10 | Holding Register | 2003 | 18 | R/W | CH1~CH18 SV 온도 설정 | PLC pending write 발생 시 우선 처리 |

#### H2 Detector - USART3

Active ID: comm_config 기본 `{30, 40, 50}` 중 Gas Type이 `1(STD)`로 설정된 ID, 슬롯 3개.

Poll item:

| 순서 | FC | Table type | 주소 | 수량 | 방향 | 항목 | 처리 |
|---:|---|---|---:|---:|---|---|---|
| 1 | FC04 | Input Register | 30000 | 4 | R | 측정가스 농도, High Scale, 1차/2차 Alarm 설정값 | 주기 polling |
| 2 | FC02 | Discrete Input | 10001 | 8 | R | Alarm1, Alarm2, Fault, MENU, Test, CAL, Reserved, Toggle | 주기 polling |
| 3 | FC01 | Coil | 2 | 2 | R/W | Normal Operation restart, Test Pattern On readback | 주기 polling |

Write item:

| 우선순위 | FC | Table type | 주소 | 수량 | 방향 | 항목 | 처리 |
|---:|---|---|---:|---:|---|---|---|
| 1 | FC05 또는 FC0F | Coil | 3 | 1 | R/W | Gas Detector Test Pattern On, write `0xff` | PLC pending write 발생 시 우선 처리 |
| 1 | FC05 또는 FC0F | Coil | 2 | 1 | R/W | Gas Detector Normal Operation restart, write `0x00` | PLC pending write 발생 시 우선 처리 |

#### H2 Detector(TFT) - USART3

Active ID: comm_config 기본 `{30, 40, 50}` 중 Gas Type이 `2(TFT)`로 설정된 ID, 슬롯 3개. H2와 USART3 공유.

Poll item:

| 순서 | FC | Table type | 주소 | 수량 | 방향 | 항목 | 처리 |
|---:|---|---|---:|---:|---|---|---|
| 1 | FC01 | Coil | 1 | 4 | R/W | CH1/CH2 Remote Test Mode Reset/Set readback | 주기 polling |
| 2 | FC02 | Discrete Input | 1 | 16 | R | CH1/CH2 Alarm, Trouble, Maintenance, Test, Calibration, Toggle | 주기 polling |
| 3 | FC04 | Input Register | 1 | 15 | R/W | CH1 Detector status, concentration, alarm set point, high scale | 주기 polling |
| 4 | FC04 | Input Register | 21 | 15 | R/W | CH2 Detector status, concentration, alarm set point, high scale | 주기 polling |
| 5 | FC04 | Input Register | 85 | 2 | R/W | CH1/CH2 Detector Status2 | 주기 polling |
| 6 | FC04 | Input Register | 89 | 1 | R/W | Heart beat | 주기 polling |
| 7 | FC04 | Input Register | 91 | 6 | R/W | Detector Serial Number-1~6 | 주기 polling |
| 8 | FC03 | Holding Register | 1 | 15 | R/W | CH1 holding readback | 주기 polling |
| 9 | FC03 | Holding Register | 21 | 15 | R/W | CH2 holding readback | 주기 polling |
| 10 | FC03 | Holding Register | 85 | 2 | R/W | CH1/CH2 Detector Status2 readback | 주기 polling |
| 11 | FC03 | Holding Register | 89 | 1 | R/W | Heart beat readback | 주기 polling |
| 12 | FC03 | Holding Register | 91 | 6 | R/W | Detector Serial Number-1~6 readback | 주기 polling |

Write item:

| 우선순위 | FC | Table type | 주소 | 수량 | 방향 | 항목 | 처리 |
|---:|---|---|---:|---:|---|---|---|
| 1 | FC05 또는 FC0F | Coil | 1 | 4 | R/W | CH1/CH2 Remote Test Mode Reset/Set | PLC pending write 발생 시 우선 처리 |
| 1 | FC06 또는 FC10 | Holding Register | 1 | 15 | R/W | CH1 Detector status, concentration, alarm set point, high scale | PLC pending write 발생 시 우선 처리 |
| 1 | FC06 또는 FC10 | Holding Register | 21 | 15 | R/W | CH2 Detector status, concentration, alarm set point, high scale | PLC pending write 발생 시 우선 처리 |
| 1 | FC06 또는 FC10 | Holding Register | 85 | 2 | R/W | CH1/CH2 Detector Status2 | PLC pending write 발생 시 우선 처리 |
| 1 | FC06 또는 FC10 | Holding Register | 89 | 1 | R/W | Heart beat | PLC pending write 발생 시 우선 처리 |
| 1 | FC06 또는 FC10 | Holding Register | 91 | 6 | R/W | Detector Serial Number-1~6 | PLC pending write 발생 시 우선 처리 |

### PROTO-009 - UART4 PLC watchdog fail-safe

- 대상: UART4 PLC Modbus RTU slave 수신 상태.
- timeout 설정:
  - `APP_UART4_WATCHDOG_TIMEOUT_MS`, 단위 ms, 기본값 `5000`(2026-08-03 이전 `1000`).
  - `0U`이면 watchdog 비활성.
  - 메모리 맵에는 watchdog 설정 register를 추가하지 않는다.
- timeout 판정:
  - UART4에서 유효한 Modbus RTU 요청을 처리한 시각을 기준으로 한다.
  - 설정 시간 이상 새 요청이 없으면 PLC 통신 timeout으로 판단한다.
- fail-safe 출력:
  - DIO active ID 전체에 대해 Holding Register `129`, quantity `1`, value `0` write 요청을 생성한다.
  - write 전달은 기존 `ModbusMaster_ForwardWrite()` 경로를 사용한다.
  - timeout 유지 중에는 동일 fail-safe write를 1회만 enqueue한다.
  - UART4 통신 재개 시 latch를 해제해 다음 timeout 발생 시 다시 fail-safe write가 가능하게 한다.

### PROTO-010 - 런타임 Config Apply (HR110~122)

- 분류: 설정 프로토콜
- 상태: Confirmed
- 채널: UART4 (Modbus Holding Register 접근)
- 처리 task: `UART4_Task` (`ServiceConfigApply()`, 매 tick 폴링)
- 목적: PLC가 재컴파일/재부팅 없이 DIO/Heating/Gas active ID와 Gas profile 힌트를 런타임에 변경한다.
- 레지스터:
  - `110~111`: DIO active ID 슬롯 0~1.
  - `112~114`: Heating active ID 슬롯 0~2.
  - `115~117`: Gas active ID 슬롯 0~2.
  - `118~120`: Gas Type 슬롯 0~2 (`0=동작 안 함`, `1=STD(H2)`, `2=TFT`).
  - `121`: apply trigger. `1`을 write하면 적용을 시도하고, 처리 완료 후 `0`으로 clear된다. `0`이면 아무 효과가 없다. PLC Slave ID `8` Write group의 addr `317`과 동일 레지스터(306~317 → 110~121 연속 매핑)이므로, PLC는 `317`에 `1`을 write해서 trigger를 건다. (과거에는 306~316 write 시 자동으로 trigger가 세팅됐으나, `317` 추가 이후 자동 세팅은 제거되고 명시적 write만 trigger로 인정된다.)
  - `122`: 결과 코드(`0=IDLE`, `1=OK`, `2=INVALID_ID`, `3=DUPLICATE_ID`, `4=INVALID_GAS_TYPE`, `5=INTERNAL_ERROR`).
- 검증 규칙: ID는 `0` 또는 `1~247`, 그룹 내 `0`이 아닌 ID 중복 불허, gas profile 값은 `0~2` 범위. 검증 실패 시 기존 active list는 변경되지 않는다.
- 반영 후 동작: 성공 시 `CommConfig_t.revision`이 1 증가하고, 해당 하위 포트 task(`LPUART1_Task`/`USART2_Task`/`USART3_Task`)가 이를 감지해 config 기준 재등록(`ModbusMaster_RegisterConfiguredSlaves()`)을 수행한다.
- 저장 정책: RAM에만 반영되며 Flash에는 저장하지 않는다. 전원 재인가 시 compile-time 기본값으로 복귀한다.
- 관련 요구사항: REQ-014, REQ-020

### PROTO-011 - USB 펌웨어 업데이트 프레임 프로토콜

- 분류: 업데이트 프로토콜
- 상태: Done (코드 작성/개별 빌드 확인 완료, 2026-07-30 실 보드 종단 검증 완료)
- 채널: 앱/부트로더 각각의 USB CDC(가상 COM포트). 앱은 평상시에도 상시 열려 있고, 부트로더는 업데이트 진입 요청이 있을 때만 켠다.
- 프레임 형식(리틀엔디안): `[0xAA][0x55][CMD:1][LEN:2][PAYLOAD:LEN][CRC16:2]`. CRC16은 SOF 2바이트를 제외한 CMD+LEN+PAYLOAD 구간(Modbus와 동일 다항식, `BootShared_Crc16`).
- 앱 측(처리 task: `StartDefaultTask`, `AppBootLink_Process()` 20ms 주기 폴링):
  - `ENTER_UPDATE`(0x00, payload 없음)만 인식. CRC16이 맞으면 `.noinit_boot_flag`에 매직값을 세팅하고 `HAL_NVIC_SystemReset()`.
  - 다른 커맨드/깨진 프레임은 조용히 무시(응답 없음).
- 부트로더 측(bare-metal 슈퍼루프에서 `BootProtocol_Process()` 폴링, USB 인터럽트는 링버퍼에 적재만 하고 즉시 리턴):
  - `PING`(0x01, payload 없음) → 응답: `[status][bl_major][bl_minor][bl_patch][active_slot][slotA_valid][slotA_major/minor/patch][slotB_valid][slotB_major/minor/patch]`(13바이트).
  - `ERASE_SLOT`(0x02) → 요청 `[target_slot:1][total_size:4]`, 대상 슬롯을 total_size만큼 커버하는 2KB 페이지 수만큼 소거.
  - `WRITE_CHUNK`(0x03) → 요청 `[offset:4][data:N]`(N≤256, offset은 8의 배수 필수), double-word(8바이트) 단위 프로그래밍. offset이 8의 배수가 아니거나 ERASE_SLOT 이전이면 실패 응답.
  - `FINALIZE`(0x04) → 요청 `[total_size:4][crc32:4][major:1][minor:1][patch:1]`. 플래시에 실제로 기록된 내용을 리드백해 CRC32를 재계산하고 요청값과 대조 - 일치해야만 부트 메타데이터를 `active_slot=target`, `state=PENDING_TEST`로 갱신(`DATA-014` 참고).
  - `REBOOT`(0x05, payload 없음) → 응답 후 `NVIC_SystemReset()`.
  - 모든 응답: 요청과 동일한 `CMD`를 에코, `PAYLOAD[0]`이 상태(`0=OK`/`1=FAIL`).
- PC GUI 측(`Tools/FirmwareUpdater`): `PING`으로 현재 active_slot을 확인해 반대 슬롯에만 쓰기 진행. WRITE_CHUNK 실패 시 청크 단위로 최대 3회 재시도, 그래도 실패하면 전체 업데이트를 중단(대상 슬롯이 아직 활성화되지 않았으므로 기존 슬롯은 안전).
- 관련 요구사항: REQ-021
- 상세: `02-architecture.md`의 `ARCH-008`, `03-interfaces.md`의 `IF-010`, `04-data-model.md`의 `DATA-014` 참고.

### PROTO-012 - UART4 PLC 4-그룹 통합 Read/Write 맵

- 분류: 주소 정책
- 상태: Confirmed
- 채널: UART4
- 목적: PLC가 물리 장치별 개별 Slave ID(DIO 1,2 / Heating 20,21,22 / Gas 30,40,50 등)를 몰라도, Slave ID 그룹과 외부통신프로토콜 사양서의 절대주소만으로 통합 map에 접근하도록 `UART4_Task`에 번역 계층을 추가한다.
- 적용 범위: `UART4_Task`의 PLC-facing 처리 로직에만 적용된다. SharedMap 구조(entry key: `id + table_type + address`)와 하위 Modbus master task(`LPUART1_Task`/`USART2_Task`/`USART3_Task`), USART1 GST task의 SharedMap 접근 방식은 변경하지 않는다.
- 주소 체계: Read는 addr `1~164` 연속, Write는 addr `201~317` 연속. 외부통신프로토콜 사양서의 Address 컬럼 값을 그대로 사용하며, 그룹마다 0으로 재시작하지 않는다.
- Slave ID 배정:
  - Read: `1`=DIO, `2`=Heating Jacket 1/2/3(병합), `3`=Gas(STD+TFT 병합), `4`=Backup Interface.
  - Write: `5`=DIO, `6`=Heating Jacket 1/2/3(병합), `7`=Backup Interface, `8`=Slave ID 설정. Gas는 읽기 전용이라 Write 그룹이 없다.
- Read 그룹 상세:
  - `ID=1` DIO (addr `1~2`): `1`=DI#1, `2`=DI#2.
  - `ID=2` Heating (addr `3~110`, 108개): Jacket1 온도 `3~20`/알람 `21~38`, Jacket2 온도 `39~56`/알람 `57~74`, Jacket3 온도 `75~92`/알람 `93~110`.
  - `ID=3` Gas (addr `111~122`, 12개): `111`=STD1 농도, `112`=STD1 Gas Status, `113`=STD2 농도, `114`=STD2 Gas Status, `115`=STD3 농도, `116`=STD3 Gas Status, `117`=TFT1 농도(Lower word), `118`=TFT1 Gas Status, `119`=TFT2 농도, `120`=TFT2 Gas Status, `121`=TFT3 농도, `122`=TFT3 Gas Status.
  - `ID=4` Backup Interface (addr `123~164`, 42개): `123~162`=User Read Data 01~40, `163`=Heart Beat, `164`=Heart Beat(PLC-IF).
- Write 그룹 상세:
  - `ID=5` DIO (addr `201~202`): `201`=DO#1, `202`=DO#2.
  - `ID=6` Heating (addr `203~265`, 63개): Jacket1 [Machine ON/OFF `203`, EEPROM Save `204`, ALARM Reset `205`, 1~18CH SV 온도 설정 `206~223`], Jacket2 `[224,225,226,227~244]`, Jacket3 `[245,246,247,248~265]`.
  - `ID=7` Backup Interface (addr `266~305`, 40개): User Write Data 01~40.
  - `ID=8` Slave ID 설정 (addr `306~317`, 12개): `306`=DIO1 ID, `307`=DIO2 ID, `308~310`=Heating Jacket1/2/3 ID, `311~313`=Gas STD1/2/3 ID, `314~316`=Gas Type1/2/3, `317`=Apply Trigger(global HR `121`과 동일 레지스터). PLC가 `317`에 `1`을 write해야 `ServiceConfigApply()`가 반영을 시도한다.
- Gas 그룹 동작 정책:
  - 물리 슬롯 3개(USART3), 기본 active ID `{30, 40, 50}`.
  - Discovery(자동 판별) 로직을 사용하지 않는다. PLC가 write한 ID/Type 값을 그대로 보고 실시간으로 동작한다.
  - Gas Type 값: `0`=동작 안 함, `1`=STD, `2`=TFT. 슬롯의 Type에 따라 STD 자리 또는 TFT 자리만 값이 채워지고, 나머지 자리는 0으로 유지된다.
  - STD 1장치 = 측정가스 농도(1) + Gas Status 8bit 패킹(1) = 2개 레지스터. High Scale/1차·2차 Alarm 설정값은 포함하지 않는다.
  - TFT 1장치 = 측정가스 농도 Lower word만(1, Upper word 미사용) + Gas Status 8bit 패킹(1) = 2개 레지스터. 설정값 없음, Remote Test Mode Reset/Set 코일 미사용.
  - Gas Status 8bit 패킹(STD/TFT 공통): `bit0`=Alarm1, `bit1`=Alarm2, `bit2`=Fault/Trouble, `bit3`=MENU/Maintenance, `bit4`=Test, `bit5`=CAL, `bit6`=Reserved(항상 1), `bit7`=Toggle(2초 간격 반전).
- 제외 항목: 펌웨어 버전 레지스터, Gas STD의 High Scale/1차·2차 Alarm 설정값, Gas TFT의 농도 Upper word, Gas TFT Remote Test Mode Reset/Set 코일.
- 관련 요구사항: REQ-003, REQ-007, REQ-010
- 상세: `03-interfaces.md`의 `IF-011` 참고.

## 미결정 사항

- 하위 장치 write best-effort 실패를 PLC가 어떤 상태 레지스터로 판단할지 운용 기준 검증 필요.

## 변경 이력

- 2026-06-26: `interface_test/interface_main` 소스에서 프로토콜 초안 추출.
- 2026-06-27: UART4 slave, 하위 Modbus master, USART1 GST, 통합 map, 응답 없음 정책 반영.
- 2026-06-27: active_id_list 기반 응답 제한과 장치별 순차 polling 정책 반영.
- 2026-06-27: 외부통신프로토콜 사양서 기준 DIO, Heating Jacket, H2 Detector, H2 Detector(TFT) poll/write item 표 반영.
- 2026-06-27: USART1/GST ID 없는 global map과 write 실패 시 pending 폐기 정책 반영.
- 2026-06-27: `07-implementation-plan.md` 구현 단계 참조 반영.
- 2026-06-27: `UART4_Task`, 하위 Modbus task, USART1 GST 3-task 구조를 프로토콜 문서에 반영.
- 2026-06-27: USART1/GST 공용 영역의 통합 map 동기화 주체를 USART1 GST task로 명확화.
- 2026-06-28: timeout/fail 실패 기준과 USART1 GST link 상태 처리 문구를 현재 코드 기준으로 갱신.
- 2026-06-28: DIO active ID `1~3` → `1~2` (slave 3 미연결 제거), write timeout 50 ms → 30 ms 반영. PROTO-003, PROTO-008 갱신.
- 2026-06-28: Heating `1~7` → `1~5`, Gas H2 `20~29` → `20~24`, H2_TFT `40~49` → `40~44` 반영. Discovery 정책(시작 1회, retry 5회, 미응답 → offline 마킹 + polling 제외) PROTO-008에 추가.
- 2026-06-28: Heating 슬롯 3개, Gas 슬롯 3개로 확정. active ID 표기를 comm_config 기본값 기준으로 변경. PROTO-003 포트 active ID 갱신. PROTO-005 슬레이브 엔트리 동적 등록 정책 추가. PROTO-008 Discovery 정책에 RegisterSlave 호출 및 Gas type probe 순서 반영.
- 2026-06-30: 현재 코드 기준으로 즉시 ACK write 정책, active ID 기본값, response timeout 기본값을 갱신.
- 2026-06-30: PROTO-009 UART4 PLC watchdog fail-safe 정책 추가.
- 2026-07-06: PROTO-002의 global 영역 판정을 GST 단독이 아니라 설정(110~125)/슬레이브상태(131~145)/GST(199~322) 3개 range 기준으로 정정. PROTO-004에 PLC write의 201~241(실제 GST 전송) vs 283~322(로컬 즉시 완료) 분기 설명 추가. 코드에 이미 구현되어 있던 런타임 Config Apply(HR110~122)를 PROTO-010으로 신규 반영.
- 2026-07-06: PROTO-011 추가. USB 펌웨어 업데이트 프레임 프로토콜(앱 ENTER_UPDATE 트리거 + 부트로더 PING/ERASE_SLOT/WRITE_CHUNK/FINALIZE/REBOOT) 반영, 실 보드 검증 전.
- 2026-07-30: PROTO-011 실 보드 종단 검증 완료로 상태를 Done으로 갱신.
- 2026-07-14: PROTO-012 추가. UART4 PLC 4-그룹 통합 Read/Write 맵(Slave ID 1~4 Read, 5~8 Write) 신규 반영. Gas active ID 기본값 `{31,40,30}` → `{30,40,50}` 정정, Gas Type `0=AUTO` → `0=동작 안 함`(자동판별 개념 삭제), Discovery 로직 미사용(ID/Type 값을 즉시 실시간 반영), Gas STD/TFT 각 2개 레지스터(농도+Gas Status 8bit 패킹)로 축소, DI/DO 주소 스왑 정정, "User Data" → "User Write Data" 명칭 정정 반영.
- 2026-07-16: PROTO-003에 write forward 응답 대기/무재시도 정책 반영. 하나의 PLC 요청에서 여러 write forward(예: DIO#1, DIO#2)가 발생할 때 같은 포트에서 버스 충돌 없이 순차 전송되도록, write forward 전송 후 포트별 response timeout 동안 응답을 대기(재시도 없음)한 뒤 다음 큐 항목을 처리하도록 `a_modbus_master.c`에 `pending_is_write` 상태 추가.
- 2026-07-16: PROTO-012 Gas TFT 농도 레지스터를 Upper word(IR 4)에서 Lower word(IR 3)로 변경. 사용자 확인 결과 실제 유효값이 Lower word에 있음.
