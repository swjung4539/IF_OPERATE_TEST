# Requirements

## 목적

`IF_Controller/IF_main`의 통신 구조와 메모리 맵 기반 요구사항을 ID 기반으로 누적 관리한다.

## 요구사항 목록

| ID | 분류 | 제목 | 우선순위 | 상태 | 출처 |
|---|---|---|---|---|---|
| REQ-001 | 제약 | STM32L452 + CubeIDE + FreeRTOS 기반으로 동작 | High | Confirmed | 기존 소스, 사용자 결정 |
| REQ-002 | 기능 | 5개 UART 채널을 DMA circular + IDLE 방식으로 수신 | High | Confirmed | 기존 소스 |
| REQ-003 | 기능 | UART4는 PLC용 Modbus RTU Slave로 동작 | High | Confirmed | 사용자 결정, 외부통신프로토콜 사양서 |
| REQ-004 | 기능 | 브릿지 허브 기능은 폐기하고 통합 메모리 맵을 사용 | High | Confirmed | 사용자 결정 |
| REQ-005 | 기능 | LPUART1, USART2, USART3는 Modbus RTU Master로 동작 | High | Confirmed | 사용자 결정, 외부통신프로토콜 사양서 |
| REQ-006 | 기능 | USART1은 GST 프로토콜만 사용하며 Modbus master/slave가 아님 | High | Confirmed | 사용자 결정 |
| REQ-007 | 기능 | 통합 메모리 맵은 ID, 영역, 주소로 entry를 분리 | High | Confirmed | 사용자 결정 |
| REQ-008 | 기능 | PLC read는 통합 map 값을 즉시 반환 | High | Confirmed | 사용자 결정 |
| REQ-009 | 기능 | PLC write는 SharedMap 즉시 커밋 후 PLC에 즉시 ACK | High | Confirmed | 현재 코드 |
| REQ-010 | 기능 | 외부통신프로토콜 사양서의 주소는 모두 미리 정의된 entry로 구현 | High | Confirmed | 사용자 결정, 외부통신프로토콜 사양서 |
| REQ-011 | 기능 | GST 영역 199~322를 통합 map에 구현 | High | Confirmed | 사용자 결정, 외부통신프로토콜 사양서 |
| REQ-012 | 기능 | 199번 통신 상태 heartbeat bit flag 제공 | High | Confirmed | 사용자 결정 |
| REQ-013 | 기능 | 200번 PLC-UART4 heartbeat counter 제공 | High | Confirmed | 사용자 결정 |
| REQ-014 | 기능 | 통신 주기, timeout, retry는 컴파일 기본값과 RAM 설정값으로 관리 | Medium | Confirmed | 사용자 결정 |
| REQ-015 | 비기능 | 통신 실패 기준과 복구 기준을 정의 | High | Confirmed | 사용자 결정 |
| REQ-016 | 기능 | 실제 사용 ID 목록(active_id_list)만 polling 및 PLC 응답 허용 | High | Confirmed | 사용자 결정 |
| REQ-017 | 기능 | 외부통신프로토콜 사양서 기준 poll/write item 정의 | High | Confirmed | 외부통신프로토콜 사양서, 사용자 결정 |
| REQ-018 | 기능 | MODULE_RUN / MODULE_ERROR LED 동작 정의 | Medium | Confirmed | 사용자 결정 |
| REQ-019 | 안전 | UART4 PLC 통신 timeout 시 DIO 출력 fail-safe 0 처리 | High | Confirmed | 사용자 결정 |
| REQ-020 | 기능 | PLC가 HR110~122로 런타임 active ID/Gas profile 변경 가능 | Medium | Confirmed | 현재 코드 |
| REQ-021 | 기능 | USB로 재컴파일/재플래시 없이 펌웨어 업데이트 가능 | Medium | Done | 사용자 결정 |

## 구현 계획 참조

- 구현 단계와 완료 기준은 `07-implementation-plan.md`를 따른다.
- 요구사항별 구현은 `IMP-002~IMP-010` 단계에서 반영한다.
- SDD/subagent 방식으로 구현하려면 구현 시작 전에 사용자 승인을 받아야 한다.

## 상세 요구사항

### REQ-001 - STM32L452 + CubeIDE + FreeRTOS 기반으로 동작

- 설명: 시스템은 STM32L452VETx, STM32CubeIDE, STM32Cube FW_L4 V1.18.2, FreeRTOS CMSIS V2 기반으로 동작한다.
- 완료 기준: CubeMX 초기화 이후 `UART4_Task`, `LPUART1_Task`, `USART2_Task`, `USART3_Task`, `USART1_RxTask`, `USART1_ProtocolTask`, `USART1_TxTask`가 초기화된다.
- 관련 코드: `interface_main.ioc`, `Core/Src/main.c`, `Core/Src/freertos.c`

### REQ-002 - 5개 UART 채널을 DMA circular + IDLE 방식으로 수신

- 설명: LPUART1, USART1, USART2, USART3, UART4는 공통 UART 수신 계층으로 DMA 수신 상태를 관리한다.
- 완료 기준: 각 UART가 DMA circular + IDLE 기반으로 frame 수신 데이터를 해당 통신 task에 제공한다.
- 관련 코드: `a_uart.c`, `a_uart.h`

### REQ-003 - UART4는 PLC용 Modbus RTU Slave로 동작

- 설명: `UART4_Task`는 PLC가 접근하는 상위 Modbus RTU slave 인터페이스다.
- 완료 기준: `UART4_Task`는 PLC 요청의 ID, function code, 주소를 해석하고 통합 map entry가 존재하는 경우에만 응답한다.
- 제약 사항: UART4는 polling period를 갖지 않는다. PLC 요청에 대해서만 동작한다.
- 통신 설정: 115200 bps, 8 data bit, no parity, 1 stop bit, no flow control, LSB first.

### REQ-004 - 브릿지 허브 기능은 폐기하고 통합 메모리 맵을 사용

- 설명: UART4로 받은 데이터를 하위 UART로 그대로 전달하는 투명 브릿지 허브 방식은 사용하지 않는다.
- 완료 기준: PLC 요청은 통합 메모리 맵과 `LPUART1_Task`, `USART2_Task`, `USART3_Task`의 read/write 동작으로 처리한다.
- 폐기 항목: 주소 기반 transparent forwarding, 하위 UART 응답을 그대로 UART4로 되돌리는 bridge worker 구조.

### REQ-005 - LPUART1, USART2, USART3는 Modbus RTU Master로 동작

- 설명: `LPUART1_Task`, `USART2_Task`, `USART3_Task`는 하위 장치를 대상으로 Modbus RTU master 동작을 수행한다.
- 완료 기준: 각 task는 통합 map을 참조해 주기적으로 하위 장치와 송수신하고, 하위 장치 데이터와 통합 메모리 맵을 동기화한다. PLC write로 발생한 pending write는 대상 하위 장치에 전송한다.
- 포트 배정:
  - LPUART1: DIO, RS485, 38400 bps.
  - USART2: Heating Jacket, RS485, 19200 bps.
  - USART3: Gas Detector, RS485, 9600 bps, even parity.

### REQ-006 - USART1은 GST 프로토콜만 사용하며 Modbus master/slave가 아님

- 설명: USART1은 RS422 기반 GST 사용자 프로토콜 통신만 수행한다.
- 완료 기준: USART1은 Modbus RTU master 또는 slave로 동작하지 않고, `USART1_RxTask`, `USART1_ProtocolTask`, `USART1_TxTask` 3개 task로 GST 공용 메모리 영역을 송수신한다.
- 통신 설정: 115200 bps, 8 data bit, no parity, 1 stop bit, no flow control, LSB first.
- task 구성:
  - `USART1_RxTask`: USART1 수신 frame을 GST 수신 queue에 넣고, queue가 가득 찬 경우 최신 frame 기준으로 재동기화한다.
  - `USART1_ProtocolTask`: GST 수신 queue의 frame을 해석하고, 통합 메모리 맵의 GST read/write 영역을 기준으로 GST 송신 frame 데이터를 생성한다.
  - `USART1_TxTask`: GST 송신 queue의 요청을 USART1 GST write frame으로 전송하고 실패 시 짧은 delay 후 재시도한다.

### REQ-007 - 통합 메모리 맵은 ID, 영역, 주소로 entry를 분리

- 설명: 같은 주소라도 Modbus 영역과 ID가 다르면 서로 다른 entry로 취급한다.
- 완료 기준: 내부 key는 `id + table_type + address`로 정의한다.
- 영역:
  - Coil: `1`번부터, bit 데이터, FC01/FC05/FC0F.
  - Discrete Input: `1`번부터, bit 데이터, FC02.
  - Input Register: `30000`번부터, 16-bit 데이터, FC04.
  - Holding Register: `40000`번 영역 표기는 영역 설명으로만 사용하고, 실제 프로토콜 주소는 외부통신프로토콜 사양서 숫자를 그대로 사용한다.
- ID 범위: `0~247` 허용, `248~255` 미허용.
- ID 0 정책: broadcast가 아니라 일반 ID처럼 read/write 응답을 제공한다.

### REQ-008 - PLC read는 통합 map 값을 즉시 반환

- 설명: PLC가 UART4로 read 요청하면 하위 장치를 즉시 read하지 않고 현재 통합 map 값을 반환한다.
- 완료 기준: `LPUART1_Task`, `USART2_Task`, `USART3_Task`, `USART1_ProtocolTask`가 갱신한 최신 map 값을 기준으로 `UART4_Task` 응답을 생성한다.
- 제약 사항: map entry가 없으면 응답하지 않는다.

### REQ-009 - PLC write는 SharedMap 즉시 커밋 후 PLC에 즉시 ACK

- 설명: PLC가 UART4로 write 요청하면 SharedMap에 값을 즉시 커밋하고 PLC에 Modbus RTU ACK를 즉시 전송한다. 대상 하위 장치 또는 GST peer 전달은 이후 별도 task에서 best-effort로 처리한다.
- 완료 기준: `UART4_Task`는 하위 장치 응답을 기다리지 않고 ACK를 전송하며, 하위 전달 실패는 PLC ACK를 되돌리지 않는다.
- 대상: `LPUART1_Task`, `USART2_Task`, `USART3_Task`의 Modbus write 영역과 USART1 GST write 영역.

### REQ-010 - 외부통신프로토콜 사양서의 주소는 모두 미리 정의된 entry로 구현

- 설명: 런타임에 요청 주소를 기준으로 map entry를 자동 생성하지 않는다.
- 완료 기준: 외부통신프로토콜 사양서에 정의된 Coil, Discrete Input, Input Register, Holding Register 주소와 예약 주소를 모두 static address template으로 가진다.
- 최소 구현 범위:
  - DIO: Holding Register `129~130`.
  - USART1/GST: Holding Register `199~322`.
  - Heating Jacket: Input Register `1000~1035`, Holding Register `2000~2020`.
  - Gas Detector: Coil/Discrete low address `1~99`, Discrete Input `10000~10016`, Input Register `30000~30096`, Holding Register `40000~40030`.
- ID별 map entry는 active_id_list에 등록된 실제 사용 ID에 대해서만 응답 가능 상태로 사용한다.
- 오류 정책: 미정의 ID, 미정의 영역, 미정의 주소 요청은 응답하지 않는다.

### REQ-011 - GST 영역 199~322를 통합 map에 구현

- 설명: USART1 GST 영역은 ID 없는 global Holding Register 영역으로 통합 map에 구현한다.
- 완료 기준:
  - `199`: 통신 상태 heartbeat bit flag.
  - `200`: PLC-UART4 heartbeat counter.
  - `201~241`: GST write 영역, 41개 register.
  - `242~282`: GST read 영역, 41개 register.
  - `283~322`: 예약 영역, read/write 허용, 기본값 0.
- ID 정책: USART1/GST protocol 자체에는 ID가 없으며, UART4 Modbus request의 slave ID와 무관하게 동일한 global GST map을 사용한다.
- 동기화 정책: GST peer와의 실제 송수신 및 `201~282` 영역의 map 동기화는 `USART1_ProtocolTask`가 담당한다. UART4는 PLC Modbus 요청을 map 인터페이스에 연결할 뿐 USART1 GST frame을 생성하거나 수신 데이터를 갱신하지 않는다.

### REQ-012 - 199번 통신 상태 heartbeat bit flag 제공

- 설명: address `199`는 각 통신 상태를 bit flag로 표현한다.
- 완료 기준:
  - bit 0: DIO 통신 상태.
  - bit 1: Heating Jacket 통신 상태.
  - bit 2: Gas Detector 통신 상태.
  - bit 3: USART1 GST 통신 상태.
  - 값 `1`: 정상, 값 `0`: 비정상.
- 초기값: 전원 인가 직후 통신 성공 전에는 `0`.

### REQ-013 - 200번 PLC-UART4 heartbeat counter 제공

- 설명: address `200`은 PLC와 UART4 통신 상태 확인용 counter다.
- 완료 기준: UART4 정상 통신마다 `0~65535` 범위에서 1씩 증가하고, `65535` 초과 시 `0`으로 rollover한다.

### REQ-014 - 통신 주기, timeout, retry는 컴파일 기본값과 RAM 설정값으로 관리

- 설명: 기본값은 compile-time macro로 정의하고 부팅 시 RAM 설정 구조체에 복사한다.
- 완료 기준:
  - LPUART1 polling period 기본값: 50 ms.
  - USART2 polling period 기본값: 50 ms.
  - USART3 polling period 기본값: 50 ms.
  - USART1 GST protocol period 기본값: 50 ms.
  - UART/GST TX timeout 기본값: 50 ms.
  - LPUART1 response timeout 기본값: 50 ms.
  - USART2 response timeout 기본값: 200 ms.
  - USART3 response timeout 기본값: 100 ms.
  - retry count 기본값: 3회.
- 제약 사항: Flash(비휘발성) 저장은 포함하지 않는다. PLC register 기반 런타임 설정 변경 자체는 REQ-020에서 제공한다.

### REQ-015 - 통신 실패 기준과 복구 기준을 정의

- 설명: 통신 상태 bit는 연속 실패와 성공 복구 기준으로 갱신한다.
- 완료 기준:
  - 연속 3회 timeout/fail이면 해당 통신 상태를 비정상으로 판단한다.
  - 1회 성공하면 정상으로 복귀한다.
  - response timeout과 retry count는 RAM 설정값을 따른다.

### REQ-016 - 실제 사용 ID 목록(active_id_list)만 polling 및 PLC 응답 허용

- 설명: 허용 ID 범위 전체를 polling하지 않고 실제 사용 ID만 active_id_list에 등록해 사용한다.
- 완료 기준:
  - LPUART1, USART2, USART3 polling은 active_id_list에 등록된 ID만 수행한다.
  - PLC read/write도 대상 장치군의 active_id_list에 등록된 ID에 대해서만 응답한다.
  - 허용 ID 범위 안이어도 active_id_list에 없으면 응답하지 않는다.
  - active_id_list는 compile-time 기본값을 부팅 시 RAM 설정값으로 복사해 사용한다.
- 처리 순서:
  - pending write가 있으면 read polling보다 우선 처리한다.
  - pending write가 없으면 active_id_list의 현재 ID를 선택한다.
  - 선택한 ID 안에서 외부통신프로토콜 사양서의 장치별 주소 표 순서대로 poll item을 처리한다.
  - 마지막 ID와 마지막 poll item 이후 처음으로 rollover한다.
- 적용 범위:
  - DIO: ID `{1, 2}`.
  - Heating Jacket: ID `{20, 21, 22}`.
  - Gas Detector: ID `{31, 40, 30}`.
  - Gas Detector profile은 Discovery 결과에 따라 H2 Detector 또는 H2 Detector(TFT)로 판별한다.
  - USART1/GST: 하위 Modbus polling 대상이 아니며 active_id_list를 사용하지 않는다. GST 공용 영역 `199~322`의 값은 `USART1_ProtocolTask`가 GST peer와 송수신하며 통합 map에 동기화한다. PLC는 UART4 Modbus를 통해 해당 map 영역을 조회하거나 write 요청을 낼 수 있지만, UART4가 USART1 GST 송수신을 수행하지 않는다.

### REQ-017 - 외부통신프로토콜 사양서 기준 poll/write item 정의

- 설명: 하위 Modbus master의 poll/write item은 외부통신프로토콜 사양서의 장치별 주소 표를 기준으로 정의한다.
- 완료 기준:
  - DIO, Heating Jacket, H2 Detector, H2 Detector(TFT)를 장치별 profile로 나누어 poll item과 write item을 문서화한다.
  - poll item은 장치별 표 순서대로 순차 처리한다.
  - write item은 PLC write 요청으로 발생한 pending write를 read polling보다 우선 처리한다.
  - 사양서의 R 항목은 poll item으로, W 또는 R/W 항목은 write item으로 정의한다.
  - R/W 항목은 write item으로 처리하고, readback이 필요한 경우 poll item에도 포함한다.

### REQ-018 - MODULE_RUN / MODULE_ERROR LED 동작 정의

- 설명: 보드에는 정상 동작 표시용 LED(MODULE_RUN)와 상태 이상 표시용 LED(MODULE_ERROR)가 있다.
- 완료 기준:
  - `MODULE_RUN`: `defaultTask` 시작 시 점등(항상 ON)한다. RTOS가 정상 동작 중임을 나타낸다.
  - `MODULE_ERROR`: `HardFault_Handler`에서 켜고 무한 루프 대기한다. RTOS가 죽은 상태이므로 task가 아닌 인터럽트 핸들러에서 직접 GPIO를 조작한다.
- 제약 사항: `MODULE_ERROR`는 RTOS task에서 제어하지 않는다. HardFault 발생 시 RTOS가 동작하지 않으므로 HAL 함수 없이 GPIO 레지스터를 직접 조작하거나 `HAL_GPIO_WritePin`을 사용한다.
- 관련 코드: `Core/Src/main.c` (defaultTask), `Core/Src/stm32l4xx_it.c` (HardFault_Handler)

### REQ-019 - UART4 PLC 통신 timeout 시 DIO 출력 fail-safe 0 처리

- 설명: UART4로 PLC 요청이 일정 시간 들어오지 않으면 PLC 통신 timeout으로 판단하고, DIO 하위 장치 출력값을 0으로 전환한다.
- timeout 시간은 메모리 맵 register가 아니라 `APP_UART4_WATCHDOG_TIMEOUT_MS` compile-time define으로 관리한다.
- 기본 timeout: 5000 ms (2026-08-03 이전 1000 ms).
- `APP_UART4_WATCHDOG_TIMEOUT_MS == 0U`이면 fail-safe watchdog을 비활성화한다.
- timeout 발생 시 active DIO ID 전체에 Holding Register `129` 값을 `0`으로 write 전달한다.
- timeout 유지 중에는 DIO 0 출력 write를 반복 전송하지 않고 최초 1회만 요청한다.
- UART4 통신이 다시 수신되면 timeout latch를 해제한다.
- 완료 기준:
  - UART4 무수신 시간이 설정 timeout 이상이면 DIO active slave 전체에 `HR129=0` write가 enqueue된다.
  - timeout 값은 코드의 `APP_UART4_WATCHDOG_TIMEOUT_MS` define 변경으로 조정할 수 있다.

### REQ-020 - PLC가 HR110~122로 런타임 active ID/Gas profile 변경 가능

- 설명: PLC는 재컴파일/재부팅 없이 UART4 Modbus Holding Register 접근으로 DIO/Heating/Gas active ID와 Gas profile 힌트를 변경할 수 있다.
- 완료 기준:
  - `110~111`에 DIO active ID, `112~114`에 Heating active ID, `115~117`에 Gas active ID, `118~120`에 Gas profile 힌트(`0=AUTO`, `1=H2`, `2=H2_TFT`)를 write한다.
  - `121`(trigger)에 `1`을 write하면 적용을 시도하고, 처리 후 `0`으로 clear된다.
  - `122`에 결과 코드(`0=IDLE`, `1=OK`, `2=INVALID_ID`, `3=DUPLICATE_ID`, `4=INVALID_GAS_TYPE`, `5=INTERNAL_ERROR`)가 기록된다.
  - 검증(ID `0` 또는 `1~247`, 그룹 내 `0`이 아닌 ID 중복 불허, Gas profile `0~2` 범위) 실패 시 기존 active list는 변경되지 않는다.
  - 성공 시 해당 하위 포트 task가 새 active ID 기준으로 재-Discovery를 수행한다.
- 제약 사항: Flash 비휘발성 저장은 제공하지 않는다. 전원 재인가 시 compile-time 기본값으로 복귀한다.
- 관련 코드: `a_application.c`(`ServiceConfigApply`), `a_comm_config.c`(`CommConfig_ApplyDeviceSettings`)

### REQ-021 - USB로 재컴파일/재플래시 없이 펌웨어 업데이트 가능

- 설명: 현장에 배포된 장치를 ST-Link 등 물리적 재플래싱 없이, 이미 앱에서 상시 동작 중인 USB CDC(가상 COM포트)를 통해 PC용 GUI 툴로 펌웨어를 갱신할 수 있다.
- 완료 기준:
  - 앱이 USB CDC로 `ENTER_UPDATE` 프레임을 받으면 부트로더 재진입을 예약하고 재부팅한다(`ARCH-008`, `IF-010` 참고).
  - 부트로더가 A/B 두 슬롯 중 비활성 슬롯에만 새 이미지를 기록하고(현재 실행 중인 슬롯은 건드리지 않음), 플래시 리드백 CRC32 검증을 통과해야만 활성화(`PENDING_TEST`)한다.
  - 새 펌웨어가 `Application_Init()`을 정상적으로 마치면 스스로 `CONFIRMED`로 전환한다. 초기화가 실패/hang되면 IWDG가 리셋시키고, 다음 부팅 시 부트로더가 자동으로 이전 슬롯으로 롤백한다.
  - 업데이트 도중 정전/USB 단절이 발생해도 실행 중이던 기존 슬롯은 전혀 훼손되지 않는다.
  - PC용 C#/.NET GUI에서 COM포트 선택, 펌웨어 파일 선택, 진행률 표시, 실패 시 안내를 제공한다.
- 제약 사항: 이 기능은 A/B 두 슬롯(각 224KB)에 들어가는 크기로 펌웨어를 유지해야 한다. Flash 소거/쓰기는 실시간성이 없는 부트로더에서만 수행하며 앱에서는 수행하지 않는다.
- 관련 코드: `Bootloader/Bootloader/`(신규 프로젝트), `IF_main/User/Application/Src/a_boot_link.c`, `a_bootloader_link.c`, `Shared/bootloader_shared.h/.c`, `Tools/FirmwareUpdater/`(C# GUI)
- 검증 상태: 코드 작성 및 개별 빌드 확인 완료. 2026-07-30 실제 보드에서 A/B 왕복 업데이트, IWDG 롤백 시나리오 실측 검증 완료(`08-implementation-notes.md`의 `IMP-014` 참고).

## 상태 정의

- Draft: 초안
- Confirmed: 사용자와 정책 확정
- In Progress: 진행 중
- Done: 완료
- Deferred: 보류
- Removed: 제거

## 미결정 사항

- 없음

## 변경 이력

- 2026-06-26: `interface_test/interface_main` 소스에서 요구사항 초안 추출.
- 2026-06-27: 브릿지 허브 폐기, 통합 map, 포트별 master/polling, heartbeat, ID 정책 반영.
- 2026-06-27: active_id_list 기반 polling 및 PLC 응답 제한 정책 반영.
- 2026-06-27: DIO `1~3`, Heating `1~7`, Gas `20~29`/`40~49` active ID와 사양서 기반 poll/write item 요구사항 반영.
- 2026-06-27: USART1/GST ID 없는 global map 정책과 write 실패 시 pending 폐기 정책 반영.
- 2026-06-27: `07-implementation-plan.md` 구현 단계 참조 반영.
- 2026-06-27: 통신 task 구성을 `UART4_Task`, `LPUART1_Task`, `USART2_Task`, `USART3_Task`, USART1 3-task 구조로 명확화.
- 2026-06-27: USART1/GST 공용 영역의 통합 map 동기화 주체를 USART1 GST task로 명확화.
- 2026-06-28: 통신 실패 기준 표현을 timeout/fail 기준으로 갱신.
- 2026-06-28: REQ-018 MODULE_RUN / MODULE_ERROR LED 동작 요구사항 추가.
- 2026-06-30: 현재 코드 기준으로 REQ-009 즉시 ACK 정책, active ID 기본값, response timeout 기본값을 갱신.
- 2026-06-30: REQ-019 UART4 PLC timeout 시 DIO 출력 fail-safe 0 요구사항 추가.
- 2026-07-06: REQ-020 추가. 코드에 이미 구현되어 있던 런타임 Config Apply(HR110~122)를 소급 반영하고, REQ-014의 "PLC register 설정 변경 제외" 제약 문구를 정정.
- 2026-07-06: REQ-021 추가. USB 펌웨어 업데이트(부트로더 A/B 이중 슬롯 + C# GUI) 요구사항 반영, 실 보드 검증 전이라 상태는 In Progress.
- 2026-07-30: REQ-021 실 보드 검증 완료로 상태를 Done으로 갱신.
