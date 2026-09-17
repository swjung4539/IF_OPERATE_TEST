# Full Code Review - 2026-06-30

## Review Scope

- Project-owned firmware code under `User/Application`, `Core/Src`, `Core/Inc`, linker/config files, and project documents needed for context.
- Third-party STM32 HAL, CMSIS, and FreeRTOS source files were treated as external dependencies and not reviewed line-by-line.

## Verification

- Build command: `make -C Debug -j8 all`
- Result: success
- Size: `text=70936`, `data=4028`, `bss=68580`, `dec=143544`
- Note: an earlier nested PowerShell build command failed because the command was composed incorrectly; the corrected build command above succeeded.

## Findings

### 1. Major

- 코드 위치: `User/Application/Src/a_comm_config.c:33`, `User/Application/Src/a_comm_config.c:45`, `User/Application/Src/a_comm_config.c:136`, `User/Application/Src/a_comm_config.c:184`, `User/Application/Src/a_comm_config.c:194`, `User/Application/Src/a_application.c:491`, `User/Application/Src/a_application.c:515`, `User/Application/Src/a_application.c:539`
- 문제 설명: `s_comm_config`가 UART4 task에서 갱신되고, LPUART1/USART2/USART3/GST 관련 task에서 동시에 읽힌다. 하지만 `CommConfig_ApplyDeviceSettings()`, `CommConfig_GetActiveIds()`, `CommConfig_GetRevision()`, `CommConfig_GetGasTypeByIndex()`에 mutex/critical section/복사본 스냅샷 보호가 없다.
- 왜 문제가 되는지: 설정 적용 중 `dio_ids`, `heating_ids`, `gas_ids`, `gas_types`, `revision` 일부만 갱신된 순간 다른 task가 읽으면 잘못된 slave ID로 discovery/poll/write를 수행할 수 있다. 특히 `revision`은 마지막에 증가하므로, 다른 task가 중간 상태를 읽어도 재초기화 조건을 놓칠 수 있다.
- 수정 제안: `s_comm_config` 접근을 mutex 또는 critical section으로 보호한다. 더 안전한 방식은 새 `CommConfig_t next_config`를 지역에서 완성한 뒤 짧은 critical section에서 구조체 전체를 한 번에 교체하고 `revision`을 증가시키는 것이다. `CommConfig_GetActiveIds()`처럼 내부 포인터를 반환하는 API는 외부 task가 오래 들고 있을 수 있으므로, caller 제공 버퍼에 복사하는 snapshot API로 바꾸는 것이 좋다.
- 추가 테스트 케이스: UART4로 설정 적용을 반복하면서 각 master task가 동시에 polling 중일 때 ID 목록이 찢겨 읽히지 않는지 테스트. 설정 변경 직후 discovery 대상 ID, gas type, revision이 항상 같은 세대의 값인지 검증.

### 2. Major

- 코드 위치: `User/Application/Src/a_shared_map.c:66`, `User/Application/Src/a_shared_map.c:107`, `User/Application/Src/a_shared_map.c:127`, `User/Application/Src/a_shared_map.c:306`, `User/Application/Src/a_application.c:498`, `User/Application/Src/a_application.c:523`, `User/Application/Src/a_application.c:547`
- 문제 설명: 설정 변경 후 discovery가 다시 돌 때 `SharedMap_RegisterSlave()`는 새 slave entry를 추가만 하고, 비활성화된 이전 ID entry를 invalid 처리하거나 slave 영역을 재구성하지 않는다. `AddEntry()`는 capacity 초과 시 조용히 return 한다.
- 왜 문제가 되는지: 사용자가 ID 설정을 여러 번 변경하면 이전 ID의 map entry가 계속 남고, 새 ID가 누적된다. 충분히 반복하면 `SHARED_MAP_ENTRY_CAPACITY`에 도달해 새 slave register가 등록되지 않을 수 있다. 이 경우 이후 `SharedMap_Find()`가 실패하고, 쓰기/읽기/timeout fail-safe가 동작하지 않거나 과거 ID 데이터가 계속 노출될 수 있다.
- 수정 제안: 설정 적용 시 slave별 동적 entry 영역을 재빌드하는 API를 추가한다. 예: `SharedMap_RebuildSlaveEntries()`에서 static/global entry는 유지하고 active ID 기준으로 DIO/Heating/Gas entry를 재생성한다. 최소한 `SharedMap_RegisterSlave()` 실패 여부를 반환하고, capacity 초과를 로그/상태값으로 노출해야 한다.
- 추가 테스트 케이스: 110~120 설정 register를 여러 조합으로 20회 이상 변경하고 discovery 재수행 후 active ID만 조회되는지 확인. capacity 경계에서 새 ID 등록 실패가 감지되는지 확인.

### 3. Major

- 코드 위치: `User/Application/Src/a_application.c:231`, `User/Application/Src/a_application.c:247`, `User/Application/Src/a_application.c:267`, `User/Application/Src/a_application.c:276`
- 문제 설명: UART4 watchdog timeout 시 DIO 출력 0 요청을 enqueue하지만, `SharedMap_SetPendingWrite()`, `SharedMap_CommitWrite()`, `ModbusMaster_ForwardWrite()` 중 하나라도 실패해도 `s_uart4_watchdog_latched`가 true로 고정된다.
- 왜 문제가 되는지: DIO slave가 아직 등록되지 않았거나 master write queue가 full이면 실제 DIO sub로 출력 0이 전달되지 않는다. 그런데 latch가 걸려 다음 UART4 정상 프레임이 오기 전까지 재시도하지 않으므로 fail-safe 요구사항을 만족하지 못할 수 있다.
- 수정 제안: `EnqueueDioOutputZero()`와 `EnqueueDioFailSafeOutputs()`가 성공/실패를 반환하도록 바꾸고, 모든 active DIO에 대해 queue 성공 또는 명시적으로 더 이상 보낼 대상이 없는 경우에만 latch한다. 실패 시에는 다음 loop에서 재시도하거나 제한된 retry/backoff를 둔다.
- 추가 테스트 케이스: DIO entry 미등록 상태에서 UART4 timeout 발생, master tx queue full 상태에서 timeout 발생, 이후 queue 여유가 생겼을 때 출력 0이 재시도되는지 확인.

### 4. Major

- 코드 위치: `User/Application/Src/a_modbus_master.c:450`, `User/Application/Src/a_modbus_master.c:463`, `User/Application/Src/a_modbus_master.c:948`, `User/Application/Src/a_modbus_master.c:950`, `User/Application/Src/a_application.c:224`, `User/Application/Src/a_application.c:228`
- 문제 설명: PLC write 요청에 대해 SharedMap을 먼저 commit하고 PLC에는 ACK를 보낸 뒤, 실제 sub-device write는 `ModbusMaster_ForwardWrite()`와 `ProcessPendingAttempt()`에서 best-effort 송신만 한다. write 응답 수신/CRC 검증/timeout retry가 없다.
- 왜 문제가 되는지: sub-device가 write를 거부하거나 응답하지 않아도 PLC는 성공 ACK를 받고 SharedMap은 성공 값으로 보인다. 실제 장비 출력/설정과 SharedMap/PLC 상태가 불일치할 수 있다. DIO 출력, heater 설정, gas coil/register write 같은 제어성 write에서 안정성 리스크가 크다.
- 수정 제안: write forwarding도 read poll처럼 `waiting_response`, expected ack length, timeout, retry, parse 검증 상태를 갖도록 한다. 정책상 PLC 즉시 ACK가 필요하다면, 별도 상태 register에 "forward pending/failed"를 기록하고 readback으로 실제 반영 여부를 확인해야 한다.
- 추가 테스트 케이스: sub-device write ack 정상/CRC 오류/무응답/exception response를 주입하고 PLC ACK, SharedMap 값, 통신 상태 register가 기대대로 변하는지 확인.

### 5. Major

- 코드 위치: `User/Application/Src/a_gst_protocol.c:185`, `User/Application/Src/a_gst_protocol.c:203`, `User/Application/Src/a_gst_protocol.c:228`, `User/Application/Src/a_gst_protocol.c:241`, `User/Application/Src/a_gst_protocol.c:252`
- 문제 설명: GST 수신 프레임 검증에서 function이 READ 또는 WRITE인지만 확인하고, frame address가 `GST_READ_START_ADDR`인지 검증하지 않는다. `ApplyReadFrame()`은 프레임의 address 필드를 무시하고 항상 242번지부터 SharedMap에 쓴다.
- 왜 문제가 되는지: CRC가 맞지만 address가 다른 GST frame 또는 WRITE frame이 들어와도 242~282 read 영역에 값이 반영될 수 있다. 외부 장치의 프레임 종류/주소가 틀어진 경우 PLC가 잘못된 GST 상태를 읽게 된다.
- 수정 제안: `ValidateFrame()` 또는 `ApplyReadFrame()`에서 `frame[3] == GST_FC_READ` 및 `Modbus_GetU16Be(&frame[4]) == GST_READ_START_ADDR`를 확인한다. 길이도 `GST_DATA_LENGTH` 또는 허용된 축약 길이 정책을 명확히 문서화한다.
- 추가 테스트 케이스: 정상 READ frame, function WRITE frame, address 201 frame, address 242 but odd data length, CRC 오류 frame을 각각 넣고 SharedMap 242~282 변경 여부를 검증.

### 6. Major

- 코드 위치: `User/Application/Src/a_uart.c:125`, `User/Application/Src/a_uart.c:138`, `User/Application/Src/a_uart.c:139`, `User/Application/Src/a_uart.c:245`, `User/Application/Src/a_uart.c:247`
- 문제 설명: `HAL_UART_ErrorCallback()`에서 바로 `Uart_StartReceive()`를 호출한다. 이 함수는 `HAL_UART_DMAStop()`, DMA buffer memset, `HAL_UARTEx_ReceiveToIdle_DMA()`를 수행한다.
- 왜 문제가 되는지: HAL UART error callback은 interrupt context에서 호출될 수 있다. ISR 안에서 HAL DMA stop/start와 buffer 초기화를 수행하면 HAL 내부 lock/state와 충돌하거나 ISR latency가 커질 수 있다. 동시에 task가 `Uart_GetData()` 또는 `Uart_StopReceive()`를 수행 중이면 context 상태도 꼬일 수 있다.
- 수정 제안: error callback에서는 per-UART error flag 또는 task notification만 설정하고, 실제 DMA stop/start는 UART owner task에서 처리한다. ISR/task 공유 상태는 `FromISR` API 또는 짧은 critical section으로 보호한다.
- 추가 테스트 케이스: UART overrun/framing error를 연속 발생시키면서 DMA receive 재시작이 안정적으로 되는지 확인. error callback 중 task가 `Uart_GetData()`를 호출하는 상황에서 frame queue 손상 여부를 확인.

### 7. Minor

- 코드 위치: `User/Application/Src/a_modbus_slave.c:16`, `User/Application/Src/a_modbus_slave.c:18`, `User/Application/Src/a_modbus_slave.c:532`
- 문제 설명: `IsValidSlaveId()`가 slave id 0을 유효한 요청으로 허용한다. 이후 read/write handler는 id 0 frame도 CRC가 맞으면 처리할 수 있다.
- 왜 문제가 되는지: Modbus RTU에서 address 0은 일반적으로 broadcast이며 slave는 응답하지 않아야 한다. 현재 구조에서는 global holding range 접근에 id 0을 사용할 의도가 섞여 있을 수 있지만, 외부 PLC가 broadcast semantics를 기대하면 응답 충돌 또는 예기치 않은 설정 변경이 발생할 수 있다.
- 수정 제안: 이 장치의 UART4 PLC-facing protocol에서 id 0을 global access로 쓰는지 명확히 결정한다. Modbus 표준 broadcast를 따를 경우 read는 무응답, write는 ACK 없이 처리 또는 무시하도록 분기한다. global map 접근은 별도 PLC slave id 또는 명시 주소 정책으로 분리한다.
- 추가 테스트 케이스: id 0 read/write, id 1~247 정상, id 248 이상 요청을 넣고 응답 여부와 SharedMap 변경 여부를 검증.

### 8. Suggestion

- 코드 위치: `User/Application/Src/m_modbus.c:1392`, `User/Application/Src/m_modbus.c:1400`, `User/Application/Src/m_modbus.c:1425`, `User/Application/Src/m_modbus.c:1474`, `User/Application/Src/m_modbus.c:1494`
- 문제 설명: 신규 RTU utility 함수들은 NULL 인자 방어가 일부 부족하다. 예를 들어 `Modbus_GetU16Be()`와 `Modbus_PutU16Be()`는 caller가 항상 유효 포인터를 준다는 전제다.
- 왜 문제가 되는지: 현재 주요 호출부는 길이 검증 뒤 사용하고 있어 즉시 crash 가능성은 낮다. 다만 공용 utility로 노출된 함수라 향후 다른 호출부에서 NULL 또는 짧은 buffer를 넘기면 hard fault로 이어질 수 있다.
- 수정 제안: 포인터 검증이 필요한 API와 내부 전용 fast-path API를 구분한다. 공개 헤더에 노출된 함수는 NULL 방어 또는 사전조건을 주석으로 명확히 남긴다.
- 추가 테스트 케이스: utility 함수 단위 테스트에서 NULL, 최소 길이 미만 buffer, 정상 big-endian 변환을 검증.

## Test Gaps

- UART4 timeout fail-safe: queue full, DIO 미등록, timeout 후 재시도, 정상 통신 복귀 시 latch 해제 테스트가 필요하다.
- Config apply: 0 ID, 중복 ID, invalid ID, gas type invalid, 설정 변경 중 master polling 동시성 테스트가 필요하다.
- SharedMap dynamic registration: 반복 설정 변경, capacity 경계, inactive ID 제거 여부 테스트가 필요하다.
- Modbus slave: id 0/broadcast 정책, CRC 오류, 잘못된 byte count, boundary quantity, register range crossing 테스트가 필요하다.
- Modbus master write forwarding: write ack 정상/무응답/exception/CRC 오류/timeout retry 테스트가 필요하다.
- GST protocol: function/address/data length/CRC/tail 오류 프레임과 partial frame reassembly timeout 테스트가 필요하다.

## Notes

- 리뷰 시점에 기존 작업 트리에는 Eclipse `.metadata` 변경과 문서 정리 변경이 남아 있었다. 이 리뷰에서는 해당 변경을 되돌리거나 커밋하지 않았다.
- `ai_work_documents/agent-guides/06-code-review.md`와 `ai_work_documents/checklists/04-firmware-coding-checklist.md`는 현재 일부 환경에서 한글이 깨져 보인다. 리뷰 기준은 직전 반영 내용과 `14-firmware-coding-standard.md` 기준을 적용했다.
