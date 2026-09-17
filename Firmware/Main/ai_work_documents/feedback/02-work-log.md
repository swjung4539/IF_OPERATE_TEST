# Work Log

## 목적

사용자가 대화에서 질문하고 결정한 내용을 날짜 기준으로 누적 기록한다.
코드 수정, 요구사항 반영, 설계 결정으로 이어진 질문은 관련 문서 링크를 함께 기록한다.

---

## 2026-06-28

### Q. RX 수신 버퍼가 어떻게 되어 있어?

- 포트당 구조: `dma_buffer[256]` (DMA circular) + `frames[2]` (frame queue)
- `UartFrameSlot_t`: `data[256]` + `length`
- DMA circular + IDLE 이벤트 기반으로 수신, `StoreFrame()`이 `dma_buffer`에서 `frames[tail]`로 복사
- queue full 시 가장 오래된 frame을 버리고 최신 frame 유지
- 관련 파일: `User/Application/Src/a_uart.c`

---

### Q. 링 버퍼를 적용해야 하는거 아니야?

- 결론: 이미 두 레벨 모두 링 버퍼 구조로 되어 있음
  - `dma_buffer`: DMA circular 모드 (하드웨어 수준 링 버퍼)
  - `frames[]`: `head / count % UART_FRAME_QUEUE_DEPTH` 링 queue 패턴
- 핵심 쟁점은 링 버퍼 적용 여부가 아닌 `UART_FRAME_QUEUE_DEPTH` depth가 충분한지 여부

---

### Q. 지금은 depth가 2인가?

- 확인: `a_uart.c:11` `#define UART_FRAME_QUEUE_DEPTH 2U`
- 현재 depth는 2

---

### Q. depth가 3일 때 문제점은?

- RAM 증가: 포트당 +258 B, 5포트 합계 +1290 B (현재 RAM 85% 사용 중)
- Modbus 포트에서 오래된 응답 잔류 위험 증가 (LPUART1, USART2, USART3)
- 근본 원인 미해결: task 소비 속도가 느리면 depth 3도 결국 drop 발생
- depth 변경 없이 유지

---

### Q. 코드가 수정되었는데 시퀀스 다이어그램 수정이 있을거야, 수정해줘

- 확인한 불일치:
  1. task 생성이 `Main`에서 직접 `osThreadNew`하는 것으로 표기되어 있으나, 실제는 `Application_CreateTasks()` 경유
  2. `Uart_StartReceive`가 별도 단계로 표기되어 있으나, `Uart_Init()` 내부에서 호출됨
  3. RS485 방향 핀 초기화(`SetRxMode`) 누락
  4. `defaultTask` 생성 누락
  5. `HAL_UART_ErrorCallback` → `Uart_StartReceive()` 재시작 경로 누락
- 반영 파일: `ai_work_documents/artifacts/01-sequence-diagrams.md`

---

### Q. defaultTask가 1ms 주기로 돌고 있는 게 맞아?

- 확인: `main.c` `StartDefaultTask`에서 `osDelay(1)` → 1ms 주기로 루프만 돌고 아무것도 하지 않음
- 맞음

---

### Q. MODULE_RUN이 상태 LED이고 MODULE_ERROR가 상태 이상 LED야, 요구사항에 반영해줘

- 결정 내용:
  - `MODULE_RUN`: `defaultTask` 시작 시 점등 (RTOS 정상 동작 표시)
  - `MODULE_ERROR`: `HardFault_Handler`에서 켜고 무한 루프 (task가 아닌 인터럽트 핸들러에서 처리)
- 반영 파일: `ai_work_documents/documents/01-requirements.md` (REQ-018 추가)

---

### Q. 코드도 수정해줘 (LED 코드 반영)

- 수정 내용:
  - `Core/Src/main.c` `StartDefaultTask`: `MODULE_RUN` 점등 후 1000ms 대기 루프
  - `Core/Src/stm32l4xx_it.c` `HardFault_Handler`: `MODULE_ERROR` ON 후 무한 루프

---

### Q. 정상 상태 LED는 500ms 점멸이 아니라 점등이래, 수정해줘

- 수정 내용:
  - `Core/Src/main.c`: `HAL_GPIO_TogglePin` → `HAL_GPIO_WritePin(..., GPIO_PIN_SET)` 으로 변경, 루프 진입 전 1회 점등
  - `ai_work_documents/documents/01-requirements.md` REQ-018: "500ms 토글" → "점등(항상 ON)" 수정

---

### Q. 수신 버퍼나 송신 버퍼에 초기화가 없는 것 같은데 맞아?

- 확인 결과:
  - RX 버퍼: `Uart_Init()` → `Uart_ClearData()` + `Uart_StartReceive()`에서 명시적 초기화 있음
  - TX 버퍼(`s_uart4_tx`, `s_pending_ack`): `static` 전역이라 0으로 시작하지만 사용 전 명시적 초기화 없음
- TX 버퍼는 `length` 기반으로 송신 범위가 제한되므로 동작 문제는 없으나, 방어적 초기화 부재

---

### Q. TX 버퍼 초기화 반영해줘

- 수정 내용: `User/Application/Src/a_application.c`에 `memset` 추가
  - `s_uart4_tx`: `ModbusSlave_HandleRequest` 호출 전
  - `s_pending_ack`: `GstProtocol_ProcessPendingWrite` 호출 전
  - `s_pending_ack`: `ModbusMaster_WaitPendingResult` 호출 전

---

### Q. 수신 버퍼는 수신되기 전에 깨끗한 상태여야 하지 않을까? 지금은 아닌 것 같은데 맞아?

- 확인 결과:
  - `dma_buffer`: 문제 없음 (DMA circular, 하드웨어가 계속 덮어씀)
  - `frames[].data[]`: `Uart_GetData()`에서 `length`만 0으로 초기화하고 `data[]`는 유지됨
  - 이전 frame보다 짧은 frame이 수신되면 `data[]` 끝에 이전 데이터 잔류
- `length` 추적이 정확하면 동작 문제 없지만, `length` 버그 시 잔류 데이터 처리 가능성 있음

---

### Q. 버그가 생기면 잔류 데이터가 처리될 수 있으니 초기화해줘

- 수정 내용: `User/Application/Src/a_uart.c` `StoreFrame()`에서 slot 결정 후 `memcpy` 전에 `memset(slot->data, 0, sizeof(slot->data))` 추가

---

### Q. TX 코드가 안 보내지는 이유가 뭐야?

- 원인: `Transceive()`에서 DMA를 TX 전 정지(`Uart_StopReceive`), TX 후 재시작(`Uart_StartReceive`)하는 구조에서
  슬레이브 응답 첫 바이트를 놓치는 타이밍 이슈
- 참조: `interface_main/a_modbus_slave.c` `Bridge_UartWorker_Task` 패턴
- 수정 내용: `User/Application/Src/a_modbus_master.c` `Transceive()`
  - `Uart_StopReceive` → `Uart_ClearData` (DMA 유지, 큐만 비움)
  - TX 구간: `__disable_irq()` / `__enable_irq()` 로 보호
  - `Uart_StartReceive` 제거

---

### Q. 응답 timeout 때문에 TX→TX→RX→RX 패턴이 발생해

- 원인: `DEFAULT_WRITE_TIMEOUT_MS = 50ms` < 슬레이브 실제 응답 시간 → timeout 후 다음 TX로 넘어감,
  슬레이브 늦은 응답이 다음 TX 응답으로 잘못 매칭됨
- 수정 내용: `User/Application/Inc/a_comm_config.h`
  - `DEFAULT_WRITE_TIMEOUT_MS`: 50U → 200U

---

### Q. PLC write 명령을 슬레이브 응답 기다리지 않고 즉시 응답하는 구조로 바꾸면 어때?

- 결정: SharedMap 캐시 기반 즉시 응답 구조로 전환
  - PLC Read: 이미 SharedMap에서 즉시 응답 (기존 동작)
  - PLC Write: SharedMap 저장 후 즉시 ACK, 백그라운드에서 슬레이브 전달
- Write 실패 피드백: 상태 레지스터(131~145) + Heartbeat(199/200번지 기존 사용)

---

### Q. 슬레이브별 상태 레지스터를 구현해줘

- 수정 내용:
  - `User/Application/Inc/a_shared_map.h`: `SharedMap_SetSlaveStatus()` 선언 추가
  - `User/Application/Src/a_shared_map.c`: 131~145 global holding register 추가,
    `SharedMap_SetSlaveStatus(group, slave_index, ok)` 구현
  - `User/Application/Src/a_modbus_slave.c`: `IsGlobalHoldingRange` 131~145 포함
  - `User/Application/Src/a_modbus_master.c`: 폴링 성공/실패 시 `SharedMap_SetSlaveStatus` 호출
- 관련 문서: `../documents/04-data-model.md` DATA-012

---

---

### Q. Modbus master TX/RX 큐를 분리해줘

- 결정: `ModbusMaster_Step` → `TxStep` / `RxStep` / `ProcessStep` 3단계로 분리
- tx_queue (write 전달, `ModbusSlaveWriteRequest_t`, depth 4) + rx_queue (poll 응답, `ModbusMasterRxMsg_t`, depth 4)
- TxStep: tx_queue write 우선 → poll 주기 도래 시 송신 → timeout 시 RetryOrGivePoll
- RxStep: waiting_response 중 Uart_GetData → rx_queue 적재
- ProcessStep: rx_queue dequeue → ParseReadResponse → SharedMap 갱신 / RetryOrGivePoll
- 삭제: `Transceive` (broken), `ValidateWriteResponse`, `SetAckCallback` / `SendAckToPlc` (immediate ACK으로 대체됨)
- 추가: `ParseReadResponse`에 `Modbus_VerifyCrc` 포함
- 관련 설계 결정: `../documents/06-design-decisions.md` DEC-018

---

### Q. DIO slave 3 제거, poll timeout 50ms → 30ms 변경

- 결정:
  - `a_comm_config.c` `dio_ids`: `{1, 2, 3}` → `{1, 2}`, count `3` → `2`
  - `a_comm_config.h` `DEFAULT_WRITE_TIMEOUT_MS`: `50U` → `30U`
- 이유: slave 0x03이 물리적으로 미연결 상태 → polling timeout 3회 × 200ms = 600ms 낭비 해소
- 관련 문서: `../documents/05-protocol.md` PROTO-003, PROTO-008, `../documents/03-interfaces.md` IF-005

---

---

### Q. slave ID가 0~255 중 어떤 값인지 모른다. SharedMap을 ID 기반으로 pre-allocate할 수 없는 게 아닌가?

- 문제: 슬레이브 ID는 하드웨어 설정값(0~255), 컴파일 타임에 알 수 없음. 슬롯 수만 고정(DIO=2, Heating=3, Gas=3).
- 결정:
  - `SharedMap_Init()`: global 엔트리만 생성 (slave status 131~145, GST 199~322 = 139개)
  - `SharedMap_RegisterSlave(actual_id, group)`: Discovery 완료 후 실제 ID로 엔트리 추가
  - Gas: H2 + TFT 주소 합집합(110개/슬롯) pre-allocate → 어느 타입이든 대응 가능
  - Discovery 후 발견된 ID 오름차순 정렬 → `SharedMap_RegisterSlave` 순서 = 폴링 우선순위
- 당시 pool 총 644개: DIO 4 + Heating 171 + Gas 330 + Global 139
- 2026-06-30 현재 코드 기준 pool 총 660개: DIO 4 + Heating 171 + Gas 330 + Global 155
- 관련 설계 결정: `../documents/06-design-decisions.md` DEC-019

---

### Q. Heating, Gas 슬롯 수는 3개야

- 결정:
  - `a_comm_config.c` `heating_ids`: `{1,2,3,4,5}` count=5 → `{1,2,3}` count=3
  - `a_comm_config.c` `gas_ids`: `{1,2,3,4,5}` count=5 → `{1,2,3}` count=3
  - `a_comm_config.h` `MAX_ACTIVE_HEATING_ID_COUNT`, `MAX_ACTIVE_GAS_ID_COUNT`: 이미 3U
- 당시 pool 644개 확정: DIO(2×2=4) + Heating(3×57=171) + Gas(3×110=330) + Global(139)
- 2026-06-30 현재 코드 기준 Global config/version entry 포함으로 pool은 660개.

---

### Q. SharedMap 재설계 구현해줘 (Discovery-based registration)

- 구현 내용:
  - `a_shared_map.c`: 전면 재작성. `BuildStaticEntries()` → global 엔트리만. `IsActiveMapId()` 삭제. `SharedMap_RegisterSlave()` 추가.
  - `a_shared_map.h`: `SharedMap_RegisterSlave(uint8_t id, CommDeviceGroup_t group)` 선언 추가.
  - `a_modbus_master.c` `ModbusMaster_DiscoverSlaves()`: Discovery 완료 후 발견된 ID 오름차순 정렬 → `SharedMap_RegisterSlave()` 호출.
- Gas `RegisterSlave` 등록 주소: Coil 1~4, DI 1~16, DI 10001~10008, IR TFT blocks, IR 30000~30003, HR TFT blocks (H2∪TFT 합집합, 중복 없음)
- 당시 `_Static_assert(SHARED_MAP_ENTRY_CAPACITY == 644U)` 컴파일 검증 포함
- 2026-06-30 현재 코드 기준 `_Static_assert(SHARED_MAP_ENTRY_CAPACITY == 660U)`로 갱신됨.

---

### Q. sharedmap 관련 된거 ram2로 옮길 수 있어?

- 결정:
  - SharedMap entry pool과 관련 상태 변수(`s_entries`, `s_entry_count`, `s_initialized`, `s_current_time_ms`, `s_comm_failure_counts`)를 RAM2 전용 section으로 이동.
  - 링커 스크립트에 `.sharedmap_ram2 (NOLOAD)` section을 추가하고 `RAM2`(`0x10000000`, 32 KB)에 배치.
  - `SharedMap_Init()`에서 명시 초기화하므로 startup `.bss` zero clear 대상이 아니어도 됨.
- 반영:
  - `User/Application/Src/a_shared_map.c`: `SHARED_MAP_RAM2_SECTION` attribute 추가.
  - `STM32L452VETX_FLASH.ld`, `STM32L452VETX_RAM.ld`: `.sharedmap_ram2` section 추가.
  - 관련 문서: `../documents/04-data-model.md`, `../documents/06-design-decisions.md` DEC-020, `../documents/08-implementation-notes.md`.
- 검증:
  - `make -C Debug -j8 all` 빌드 성공.
  - `Debug/IF_main.map`: `.sharedmap_ram2` = `0x10000000~0x10002950` (`0x2950` bytes).
  - SRAM1 `.bss` = `0xdc8c` bytes.
- 빌드 통과 확인 완료

---

### Q. UART4 통신 timeout 시 DIO 출력 0 처리

- 결정:
  - UART4 PLC 요청이 `APP_UART4_WATCHDOG_TIMEOUT_MS` 동안 없으면 DIO active ID 전체에 `HR129=0` write를 전달.
  - timeout 값은 메모리 맵 register가 아니라 `a_application.c`의 compile-time define으로 조정.
  - 기본값은 `1000U` ms, `0U`이면 watchdog 비활성.
  - timeout 유지 중에는 fail-safe write를 1회만 enqueue하고, UART4 통신 재개 시 latch 해제.
- 반영:
  - `ai_work_documents/documents/01-requirements.md`: REQ-019 추가.
  - `ai_work_documents/documents/05-protocol.md`: PROTO-009 추가.
  - `ai_work_documents/documents/06-design-decisions.md`: DEC-021 추가.
  - `ai_work_documents/documents/08-implementation-notes.md`: 구현 기준 추가.
  - `User/Application/Src/a_application.c`: UART4 watchdog 및 DIO fail-safe write enqueue 구현.

---

### Q. HR199 comm status bit가 DIO/Gas처럼 슬레이브 여러 개인 그룹에서, 하나가 빠져도 다시 1로 켜지는 게 맞냐

- 문제 제기: DIO(2개)/Gas(3개)처럼 그룹에 슬레이브가 여러 개 달린 경우, 슬레이브 하나가 통신 안 되면 그룹 bit(`199`)가 계속 0을 유지해야 한다고 생각했는데 실제로는 다른 슬레이브가 살아있으면 그 폴링 성공 순간 다시 1로 켜짐을 확인.
- 원인: `SharedMap_SetCommStatus()`가 그룹 전체 공용 실패 카운터 1개로 관리되어, 그룹 내 아무 슬레이브나 성공하면 즉시 카운터 리셋+bit set되는 구조였음.
- 결정: 그룹 bit를 "구성된(active) 슬레이브가 전부 온라인이어야 1, 하나라도 오프라인이면 계속 0" 방식(AND 조합)으로 변경. 미설정 슬롯은 검사 대상에서 제외. DIO/Heating/Gas 동일 적용, GST는 슬레이브 인덱스 없는 단일 디바이스라 제외(기존 방식 유지). PLC write 포워딩 성공/실패는 노이즈가 커서 그룹 bit에 반영하지 않기로 함(폴링 결과만 반영).
- 수정 내용:
  - `User/Application/Src/a_shared_map.c`: `RecomputeGroupCommBit()` 추가, `SharedMap_SetSlaveStatus()`에서 자동 호출.
  - `User/Application/Src/a_modbus_master.c`: 폴링 경로의 중복 `SharedMap_SetCommStatus()` 호출 제거, write 포워딩 경로의 `SharedMap_SetCommStatus()` 호출 제거.
- 관련 문서: `../documents/04-data-model.md` DATA-006, `../documents/08-implementation-notes.md` IMP-016.
- 검증: STM32CubeIDE 헤드리스 Debug 빌드 0 errors/0 warnings 확인. 실 보드 검증은 미수행.

---

### Q. Heating EEPROM Save/ALARM Reset timeout, UART4 watchdog timeout 1초가 맞냐, 맞으면 5초로 변경

- 확인 결과: 둘 다 1000ms(1초) 맞음.
  - `HEATING_EEPROM_ALARM_TIMEOUT_MS`(`a_plc_group_map.c`): Heating EEPROM Save/ALARM Reset 자동 클리어 timeout.
  - `APP_UART4_WATCHDOG_TIMEOUT_MS`(`a_application.c`): PLC(UART4) 통신 두절 시 DIO fail-safe 0 처리 watchdog timeout.
- 수정: 둘 다 `1000U` → `5000U`로 변경.
- 관련 문서: `../documents/06-design-decisions.md` DEC-021, `../documents/05-protocol.md` PROTO-009, `../documents/08-implementation-notes.md` IMP-017.
- 검증: STM32CubeIDE 헤드리스 clean Debug 빌드 0 errors/0 warnings 확인. 실 보드 검증은 미수행.

---

## 변경 이력

- 2026-06-28: 최초 작성. 2026-06-28 세션 대화 내용 기록.
- 2026-06-28: TX 수신 타이밍 이슈 수정, timeout 200ms 변경, 슬레이브별 상태 레지스터 구현 내용 추가.
- 2026-06-28: Modbus master TX/RX 큐 분리(DEC-018), DIO slave 3 제거, timeout 30ms 변경 내용 추가.
- 2026-06-28: SharedMap Discovery-based registration 재설계(DEC-019), Heating/Gas 슬롯 3개 확정, pool 644개 구현 내용 추가.
- 2026-06-30: SharedMap pool 660개 현재 코드 기준 정정과 RAM2 section 이동 작업 기록 추가.
- 2026-06-30: UART4 PLC timeout 시 DIO 출력 fail-safe 0 처리 작업 기록 추가.
- 2026-08-03: HR199 comm status bit를 슬레이브별 AND 조합으로 재계산하는 변경 작업 기록 추가(IMP-016).
- 2026-08-03: Heating EEPROM Save/ALARM Reset timeout, UART4 watchdog timeout 1초→5초 변경 작업 기록 추가(IMP-017).
