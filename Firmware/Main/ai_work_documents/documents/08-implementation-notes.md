# Implementation Notes

## 목적

구현 중 확인한 기준점, 단계별 결정, 검증 결과를 `IMP-*` 기준으로 기록한다.

## 구현 목록

| ID | 제목 | 상태 | 완료일 | 관련 결정 |
|---|---|---|---|---|
| IMP-000 | 통신 task 구조 기준점 | Done | 2026-06-27 | DEC-016 |
| IMP-001 | 기존 통신 코드 기준점 확인 | Done | 2026-06-27 | DEC-002 |
| IMP-002 | 통신 설정 모듈 추가 | Done | 2026-06-28 | DEC-010, DEC-011, DEC-012 |
| IMP-003 | 통합 map 모듈 추가 | Done | 2026-06-28 | DEC-006, DEC-007, DEC-013, DEC-019 |
| IMP-004 | poll/write item table 추가 | Done | 2026-06-28 | DEC-012 |
| IMP-005 / IMP-006 | UART4 Modbus slave read/write path 전환 | Done | 2026-06-28 | DEC-003, DEC-008, DEC-017 |
| IMP-007 | 하위 Modbus master task 구현 | Done | 2026-06-28 | DEC-004, DEC-018 |
| IMP-008 | USART1 GST 3-task map 연동 | Done | 2026-06-28 | DEC-005, DEC-013 |
| IMP-009 | 기존 bridge hub 경로 제거/비활성화 | Done | 2026-06-28 | DEC-002 |
| IMP-010 | 빌드 및 단계별 검증 | Done | 2026-06-28 | - |
| IMP-011 | SharedMap RAM2 배치 | Done | 2026-06-30 | DEC-020 |
| IMP-012 | UART4 PLC watchdog fail-safe | Done | 2026-06-30 | DEC-021 |
| IMP-013 | 런타임 Config Apply (HR110~122) | Done (소급 반영) | 2026-07-06 | DEC-022 |
| IMP-014 | USB 펌웨어 업데이트 (부트로더 A/B 이중 슬롯 + C# GUI) | In Progress (실 보드 검증 전) | 2026-07-06 | DEC-023 |

## 상세 구현

## IMP-000 - 통신 task 구조 기준점

- 갱신 일자: 2026-06-27
- 문서 기준 task 구성:
  - `UART4_Task`: PLC 요청을 수신하고 통합 메모리 맵 기준으로 read 응답 또는 write pending을 생성한다.
  - `LPUART1_Task`: LPUART1 하위 장치와 송수신하며 통합 메모리 맵을 동기화한다.
  - `USART2_Task`: USART2 하위 장치와 송수신하며 통합 메모리 맵을 동기화한다.
  - `USART3_Task`: USART3 하위 장치와 송수신하며 통합 메모리 맵을 동기화한다.
  - `USART1_RxTask`: USART1 수신 frame을 GST 수신 queue에 넣는다.
  - `USART1_ProtocolTask`: GST packet 해석, GST read/write 영역 map 동기화, GST 송신 queue 생성을 담당한다.
  - `USART1_TxTask`: GST 송신 queue 요청을 USART1 GST write frame으로 전송한다.
- USART1 정책:
  - USART1은 Modbus master/slave가 아니라 GST 전용 protocol endpoint다.
  - GST 공용 영역 `199~322`의 값 동기화 주체는 `USART1_ProtocolTask`다.
  - `UART4_Task`는 PLC가 GST 공용 map 영역에 접근할 수 있는 Modbus slave 인터페이스만 제공하며, USART1 GST frame 송수신을 수행하지 않는다.
- 코드 반영 상태:
  - 현재 코드는 `Core/Src/main.c`에서 `Application_Init()` 후 `Application_CreateTasks()`를 호출하고, `User/Application/Src/a_application.c`가 `UART4_Task`, `LPUART1_Task`, `USART2_Task`, `USART3_Task`, `USART1_RxTask`, `USART1_ProtocolTask`, `USART1_TxTask`를 생성한다.
  - `Application_RunOnce()` 단일 호출 구조와 `defaultTask` 연결은 현 코드에서 사용하지 않는다.
  - 2026-06-28 CMake Debug 빌드가 7-task 구조 기준으로 성공했다.
- 남은 검증:
  - 실제 UART4, LPUART1, USART2, USART3, USART1 장비 연결 검증은 별도로 필요하다.

## IMP-001 - 기존 통신 코드 기준점 확인

- 확인 일자: 2026-06-27
- 확인 결과:
  - `User/Application/*` 사용자 통신 계층은 현재 repository에 없다.
  - `a_uart.c/h`, `m_modbus.c/h`, `a_modbus_slave.c`, `a_modbus_master.c/h`, `a_gst_protocol.c/h`는 현재 repository에 없다.
  - transparent bridge hub 구현 파일이나 함수는 현재 repository에 없다.
- 재사용 대상:
  - CubeMX 생성 UART handle과 초기화 설정.
  - 5개 UART의 RX DMA circular 설정.
  - UART/DMA interrupt handler와 HAL 연결.
  - RS485/RS422 DE/RE GPIO define과 초기 출력 설정.
  - CMSIS-RTOS2/FreeRTOS 기본 골격.
- 신규 작성 대상:
  - `a_uart.c/h`
  - `m_modbus.c/h`
  - `a_comm_config.c/h`
  - `a_shared_map.c/h`
  - `a_poll_table.c/h`
  - `a_modbus_slave.c/h`
  - `a_modbus_master.c/h`
  - `a_gst_protocol.c/h`
  - `a_application.c/h`
- 폐기/주의 사항:
  - UART4 request를 하위 UART로 원본 forwarding하는 bridge 경로를 새로 만들지 않는다.
  - UART4는 통합 map 기반 Modbus RTU slave로만 응답한다.

## IMP-002 - 통신 설정 모듈 추가

- 구현 파일:
  - `User/Application/Inc/a_comm_config.h`
  - `User/Application/Src/a_comm_config.c`
- 구현 내용:
  - compile-time 기본값을 RAM `CommConfig_t`로 복사하는 `CommConfig_InitDefaults()` 추가.
  - 설정 const getter와 active ID list getter 추가.
  - active ID 포함 여부 검사 API 추가.
  - Gas Detector profile 판별 API 추가.
- 기본값:
  - LPUART1/USART2/USART3/USART1 period: 50 ms.
  - TX timeout: 50 ms.
  - LPUART1 response timeout: 50 ms.
  - USART2 response timeout: 200 ms.
  - USART3 response timeout: 100 ms.
  - retry count: 3.
  - DIO active ID: `{1, 2}`.
  - Heating active ID: `{20, 21, 22}`.
  - Gas active ID: `{31, 40, 30}`.
- 리뷰:
  - 별도 review subagent 정적 검토 통과.
- 검증:
  - 2026-06-28 Debug clean-first 빌드에 포함되어 빌드 확인됨.
  - 장비 검증은 별도 필요.

## IMP-003 - 통합 map 모듈 추가

- 구현 파일:
  - `User/Application/Inc/a_shared_map.h`
  - `User/Application/Src/a_shared_map.c`
- 구현 내용:
  - 일반 Modbus 장치는 `id + table + address` key로 조회한다.
  - USART1/GST `199~322`는 ID 없는 global Holding Register entry로 생성한다.
  - `SharedMap_Init()`은 global entry(config `110~122`, firmware version `123~125`, slave status `131~145`, GST `199~322`)만 생성한다.
  - DIO/Heating/Gas entry는 Discovery 후 `SharedMap_RegisterSlave(actual_id, group)` 호출로 생성한다.
  - pending write set/commit/discard API를 제공한다.
  - UART4 write 경로는 `pending_value`를 즉시 commit하고 PLC에 즉시 ACK한다.
  - comm status `199` bit는 DIO/Heating/Gas/GST 순서로 관리한다.
  - 연속 timeout/fail 3회 시 상태 bit clear, 성공 1회 시 set한다.
  - `SharedMap_SetCurrentTimeMs()`로 받은 시간을 성공 갱신 시 `last_update_ms`에 기록한다.
- Entry capacity:
  - DIO: `2 * 2 = 4`
  - Heating: `3 * 57 = 171`
  - Gas: `3 * 110 = 330`
  - Global: config `13` + firmware version `3` + slave status `15` + GST `124` = `155`
  - Total: `660`
- 기준 선택:
  - Gas H2 상세 범위는 `05-protocol.md`의 `PROTO-008` poll/write item 기준을 따른다.
- 리뷰:
  - 별도 review subagent 최종 정적 검토 통과.
- 검증:
  - 2026-06-28 Debug clean-first 빌드에 포함되어 빌드 확인됨.
  - 장비 검증은 별도 필요.

## IMP-004 - poll/write item table 추가

- 구현 파일:
  - `User/Application/Inc/a_poll_table.h`
  - `User/Application/Src/a_poll_table.c`
- 구현 내용:
  - DIO, Heating Jacket, H2 Detector, H2 Detector(TFT) profile별 poll/write item table을 추가했다.
  - `05-protocol.md`의 `PROTO-008` 순서대로 function code, table, address, quantity를 정의했다.
  - active ID와 gas profile 기준으로 poll profile을 선택하는 API를 제공한다.
  - pending write 처리를 위한 write item 조회 API를 제공한다.
- 리뷰:
  - 별도 review subagent 정적 검토 통과.
- 검증:
  - 2026-06-28 Debug clean-first 빌드에 포함되어 빌드 확인됨.
  - 장비 검증은 별도 필요.

## IMP-005 / IMP-006 - UART4 Modbus slave read/write path 전환

- 구현 파일:
  - `User/Application/Inc/m_modbus.h`
  - `User/Application/Src/m_modbus.c`
  - `User/Application/Inc/a_modbus_slave.h`
  - `User/Application/Src/a_modbus_slave.c`
- 구현 내용:
  - Modbus RTU CRC, endian helper, read response, write ACK builder를 추가했다.
  - FC01/FC02/FC03/FC04 read는 shared map 값을 기준으로 응답한다.
  - FC05/FC06/FC0F/FC10 write는 pending write를 만들고 `ModbusSlave_ProcessWriteRequest()`에서 SharedMap commit 후 ACK를 생성한다.
  - `UART4_Task`는 ACK를 즉시 전송하고, 하위 Modbus/GST 전달은 이후 best-effort로 처리한다.
  - GST global Holding `199~322`는 slave ID와 무관하게 global map으로 처리한다.
  - write heartbeat는 최종 ACK 성공 경로에서 증가한다.
- 리뷰:
  - 별도 review subagent 정적 검토 통과.
- 검증:
  - 2026-06-28 Debug clean-first 빌드에 포함되어 빌드 확인됨.
  - 장비 검증은 별도 필요.

## IMP-007 - 하위 Modbus master task 구현

- 구현 파일:
  - `User/Application/Inc/a_modbus_master.h`
  - `User/Application/Src/a_modbus_master.c`
  - `User/Application/Inc/a_application.h`
  - `User/Application/Src/a_application.c`
  - `Core/Src/main.c`
  - `CMakeLists.txt`
- 구현 내용:
  - LPUART1/USART2/USART3를 context 기반 Modbus master port로 처리하는 API를 추가했다.
  - tx_queue의 pending write를 read polling보다 우선 처리하도록 구성했다.
  - active ID와 poll table 기준으로 ID/poll item을 순차 처리하고 rollover한다.
  - FC01/FC02/FC03/FC04 read request와 FC05/FC06/FC0F/FC10 write request를 생성한다.
  - read response를 shared map에 반영하고 coil/discrete 값은 bit unpack 후 `0/1`로 저장한다.
  - 통신 성공/실패를 `SharedMap_SetCommStatus()`로 반영한다.
- task 반영:
  - `Application_CreateTasks()`에서 `LPUART1_Task`, `USART2_Task`, `USART3_Task`를 별도 FreeRTOS task로 생성한다.
  - 각 task는 `ModbusMaster_TxStep()`, `ModbusMaster_RxStep()`, `ModbusMaster_ProcessStep()`으로 하위 장치와 주기적으로 송수신하며 하위 장치 데이터와 통합 메모리 맵을 동기화한다.
  - `UART4_Task`는 PLC에 즉시 ACK한 뒤 `ModbusMaster_ForwardWrite()`로 pending write를 tx_queue에 제출한다.
- 리뷰:
  - 별도 review subagent 검토 결과 최초 수정 필요.
  - Modbus master pending write 처리 중 응답 상태가 유실될 수 있는 경로를 수정했다.
  - UART4 slave pending을 tx_queue에 제출하고, 실제 port context에서 best-effort로 처리하는 경로를 추가했다.
  - 수정 후 별도 review subagent 정적 검토 통과.
- 검증:
  - 2026-06-28 Debug clean-first 빌드에 포함되어 빌드 확인됨.
  - 장비 검증은 별도 필요.

## IMP-008 - USART1 GST 3-task map 연동

- 구현 파일:
  - `User/Application/Inc/a_gst_protocol.h`
  - `User/Application/Src/a_gst_protocol.c`
  - `User/Application/Src/a_application.c`
  - `CMakeLists.txt`
- 구현 내용:
  - GST frame 형식 `[Header 3][FC 1][AddrHi 1][AddrLo 1][Len 1][Data N][CRC16 2][Tail 2]` build/parse를 추가했다.
  - Header `0x47 0x53 0x54`, Tail `0x53 0x49`, Modbus CRC16 Lo/Hi 순서를 적용했다.
  - 주기 송신은 global Holding `201~241` 값을 GST write frame으로 구성한다.
  - 수신 read frame은 `242~282` 범위만 global map에 반영한다.
  - 수신 map 반영은 `FC=0x01` read frame만 허용한다.
  - `COMM_DEVICE_GROUP_GST` 통신 상태를 `199` bit 3에 반영한다.
  - PLC write로 생성된 GST global write 중 `201~241`은 UART4에서 즉시 ACK/commit 후 GST 송신 queue를 통해 best-effort로 전달한다.
  - `283~322` 예약 global 영역은 local read/write 가능 영역으로 즉시 ACK/commit 처리한다.
- task 반영:
  - USART1은 `USART1_RxTask`, `USART1_ProtocolTask`, `USART1_TxTask` 3-task 구조로 구성했다.
  - `USART1_RxTask`는 수신 frame을 GST 수신 queue에 넣고 queue full 시 최신 frame 기준으로 재동기화한다.
  - `USART1_ProtocolTask`는 GST 수신 queue를 소비해 map `242~282`를 갱신하고, map `201~241`을 GST 송신 queue로 생산한다.
  - `USART1_TxTask`는 GST 송신 queue를 소비해 GST write frame을 전송하고 실패 시 짧은 delay 후 재시도한다.
- 리뷰:
  - 최초 review subagent가 수신 FC 검증 누락을 지적했다.
  - `ApplyReadFrame()`에 `GST_FC_READ` 검증을 추가했다.
  - 재리뷰 결과 발견 사항 없음.
- 검증:
  - 2026-06-28 Debug clean-first 빌드에 포함되어 빌드 확인됨.
  - 장비 검증은 별도 필요.

## IMP-009 - 기존 bridge hub 경로 제거/비활성화

- 확인 파일:
  - `Core/Src/main.c`
  - `User/Application/*`
  - `CMakeLists.txt`
- 확인 내용:
  - repository 내 기존 transparent bridge/hub forwarding 구현 파일 또는 함수가 발견되지 않았다.
  - 현재 `main.c`는 `Application_RunOnce()` 경로와 `defaultTask`를 사용하지 않고 `Application_CreateTasks()`를 통해 7개 통신 task를 생성한다.
  - `a_application.c/h`는 `UART4_Task`, `LPUART1_Task`, `USART2_Task`, `USART3_Task`, `USART1_RxTask`, `USART1_ProtocolTask`, `USART1_TxTask` 진입점을 제공한다.
  - UART4는 통합 map 기반 Modbus slave로 처리한다.
  - USART1 GST 공용 영역의 실제 송수신과 map 동기화는 USART1 GST task가 담당한다.
- 리뷰:
  - IMP-007/IMP-008 리뷰 중 application 연결 경로와 CMake 등록을 확인했다.
- 검증:
  - 2026-06-28 Debug clean-first 빌드에 포함되어 빌드 확인됨.
  - 장비 검증은 별도 필요.

## IMP-010 - 빌드 및 단계별 검증

- 빌드 명령:
  - Configure: `cmake --preset Debug`
  - Build: `cmake --build --preset Debug --clean-first`
  - 사용 도구: STM32CubeIDE 번들 CMake/Ninja/GNU Arm toolchain.
- 빌드 결과:
  - Debug clean-first 빌드 성공.
  - RAM: `122,568 B / 128 KB` (`93.51%`).
  - RAM2: `0 B / 32 KB`.
  - FLASH: `61,832 B / 512 KB` (`11.79%`).
- 주의:
  - 이 빌드 결과는 7개 통신 task 구성 기준이다.
  - 장비 검증은 아직 수행하지 않았다.
- 검증 중 수정:
  - `SharedMapEntry_t` 필드 순서와 flag bitfield를 조정해 shared map RAM 사용량을 줄였다.
  - `configTOTAL_HEAP_SIZE`를 `80000`에서 `60000`으로 조정했다.
  - 7개 통신 task stack 크기를 빌드 가능한 수준으로 조정했다.
- 리뷰:
  - RAM/link 수정 범위에 대해 별도 review subagent 정적 검토 통과.
- 남은 장비 검증:
  - 실제 통신 장비 연결 후 UART4, LPUART1, USART2, USART3, USART1 송수신을 확인해야 한다.
  - 장비에서 stack high-water mark와 heap free/minimum free 값을 확인해야 한다.

## IMP-011 - SharedMap RAM2 배치

- 목적: SRAM1 `.bss`에 있던 SharedMap 대형 정적 데이터를 RAM2로 이동해 SRAM1 여유를 확보한다.
- 현재 확인:
  - 실제 Debug 빌드는 `STM32L452VETX_FLASH.ld`를 사용한다.
  - RAM2는 `ORIGIN = 0x10000000`, `LENGTH = 32K`로 정의되어 있다.
  - 기존 map 기준 `s_entries`는 `0x2940` bytes이며 RAM2 32 KB 안에 배치 가능하다.
- 구현 방식:
  - `a_shared_map.c`에 RAM2 section attribute macro를 정의한다.
  - SharedMap 정적 변수에 `.sharedmap_ram2` section attribute를 부여한다.
  - 링커 스크립트에 `.sharedmap_ram2 (NOLOAD)` section을 추가하고 `>RAM2`로 배치한다.
- 검증 기준:
  - 빌드 성공.
  - map file에서 `.sharedmap_ram2`가 `0x10000000` 영역에 배치되는지 확인.
  - SRAM1 `.bss` 사용량이 SharedMap 이동분만큼 감소하는지 확인.
- 검증 결과:
  - 2026-06-30 `make -C Debug -j8 all` 빌드 성공.
  - `Debug/IF_main.map` 기준 `.sharedmap_ram2`는 `0x10000000~0x10002950` (`0x2950` bytes)에 배치됨.
  - SRAM1 `.bss`는 `0x20000fbc~0x2000ec48` (`0xdc8c` bytes), `_user_heap_stack`은 `0x2000ec48~0x2000f248` (`0x600` bytes)에 배치됨.

## IMP-012 - UART4 PLC watchdog fail-safe

- 목적: PLC 통신이 끊겼을 때 DIO 출력이 마지막 상태로 유지되지 않도록 active DIO 전체에 출력 0을 전달한다.
- 설정:
  - `a_application.c`의 `APP_UART4_WATCHDOG_TIMEOUT_MS` define으로 조정한다.
  - 기본값은 `1000U` ms.
  - `0U`이면 watchdog 비활성.
  - SharedMap/PLC 설정 register는 추가하지 않는다.
- 동작:
  - UART4에서 유효한 Modbus RTU 요청을 처리하면 마지막 수신 tick을 갱신하고 timeout latch를 해제한다.
  - 마지막 수신 tick 이후 timeout이 지나면 DIO active ID 전체에 `HR129=0` write를 enqueue한다.
  - timeout 유지 중에는 같은 fail-safe write를 반복 enqueue하지 않는다.
- 검증:
  - 2026-06-30 `make -C Debug -j8 all` 빌드 성공.

## IMP-013 - 런타임 Config Apply (HR110~122)

- 목적: PLC가 재컴파일/재부팅 없이 DIO/Heating/Gas active ID와 Gas profile 힌트를 런타임에 변경할 수 있게 한다.
- 기록 경위: 2026-07-06 시퀀스 다이어그램 작성을 위한 코드 감사 중 `a_application.c`의 `ServiceConfigApply()`와 `a_comm_config.c`의 `CommConfig_ApplyDeviceSettings()`가 이미 구현되어 있음을 발견했다. `01-requirements.md`(REQ-014), `03-interfaces.md`(IF-007), `04-data-model.md`(DATA-005)에는 이 기능이 "초기 구현 범위에서 제외"로 반대로 기록되어 있어 정정과 함께 이 항목을 소급 추가한다.
- 구현 파일:
  - `User/Application/Src/a_application.c` (`ServiceConfigApply()`, `CONFIG_*_START_ADDR`/`CONFIG_APPLY_*_ADDR` define)
  - `User/Application/Src/a_comm_config.c` (`CommConfig_ApplyDeviceSettings()`)
- 레지스터 배치: `110~111` DIO ID, `112~114` Heating ID, `115~117` Gas ID, `118~120` Gas profile 힌트, `121` apply trigger, `122` 결과 코드. 상세는 `04-data-model.md`의 `DATA-013`, `05-protocol.md`의 `PROTO-010` 참고.
- 동작:
  - `UART4_Task` 루프에서 매 tick `ServiceConfigApply()`가 HR121을 폴링한다.
  - `1`이면 HR110~120 값을 읽어 `CommConfig_ApplyDeviceSettings()`를 호출한다.
  - 검증(ID `0` 또는 `1~247`, 그룹 내 `0`이 아닌 ID 중복 불허, Gas profile `0~2`) 통과 시 active list를 갱신하고 `revision`을 증가시킨다. 실패 시 기존 값 유지.
  - 결과 코드를 HR122에 쓰고 HR121을 `0`으로 clear한다.
- 후속 동작: `revision` 변경을 감지한 하위 포트 task가 `ReinitXxxContext()` + `ModbusMaster_DiscoverSlaves()`로 재-Discovery한다(코드 위치 확인, 별도 IMP 항목으로 분리 기록하지 않음).
- 검증: 코드 감사(정적 확인)로 존재를 확인함. 이 기능 자체에 대한 별도 빌드/장비 검증 기록은 남아 있지 않아 필요 시 추가 검증이 필요하다.

## IMP-014 - USB 펌웨어 업데이트 (부트로더 A/B 이중 슬롯 + C# GUI)

- 목적: 현장 배포 후 재컴파일/재플래시 없이 USB로 펌웨어를 갱신할 수 있게 한다. 업데이트 실패(정전, USB 단절, 잘못된 이미지)가 나도 기존 펌웨어가 계속 정상 동작하도록 A/B 이중 슬롯으로 설계했다.
- 구현 파일/프로젝트:
  - `Shared/bootloader_shared.h/.c`: 부트로더/앱 공용 - Flash 레이아웃 상수, `BlBootMetadata_t`, CRC32/CRC16.
  - `Bootloader/Bootloader/`(신규 STM32CubeIDE 프로젝트, FreeRTOS 없는 bare-metal): `boot_flag.c`(noinit 플래그), `boot_meta.c`(메타데이터 ping-pong 로드/저장/롤백), `boot_jump.c`(VTOR 재설정 후 슬롯 점프), `boot_flash.c`(슬롯 소거/쓰기), `boot_rxbuf.c`(USB ISR→메인루프 링버퍼), `boot_protocol.c`(PING/ERASE_SLOT/WRITE_CHUNK/FINALIZE/REBOOT 파서+처리), `main.c`(부팅 판단 로직), `USB_DEVICE/App/usbd_cdc_if.c`(CDC 수신을 링버퍼에 적재).
  - `IF_main`: `User/Application/Src/a_boot_link.c`(앱 상주 `ENTER_UPDATE` 트리거 인식 + noinit 플래그 세팅 + 리셋), `a_bootloader_link.c`(`Bootloader_ConfirmBootOk()`), `USB_DEVICE/App/usbd_cdc_if.c`(CDC 수신을 `a_boot_link`로 전달), `Core/Src/main.c`(`StartDefaultTask`에서 20ms 주기 폴링), `STM32L452VETX_FLASH.ld`/`STM32L452VETX_SLOTA.ld`/`STM32L452VETX_SLOTB.ld`(`.noinit_boot_flag` 섹션 + 슬롯별 FLASH origin), `.cproject`의 `Release_SlotA`/`Release_SlotB` 빌드 구성, `stm32l4xx_hal_conf.h`(IWDG 활성화), `a_application.c`(`Application_Init()` 끝에서 `Bootloader_ConfirmBootOk()` 호출, `UART4_Task`에서 매 tick `HAL_IWDG_Refresh()`).
  - `Tools/FirmwareUpdater/`(C#/.NET 8 WinForms): `BootProtocol.cs`(프레임/CRC 구현), `UpdateService.cs`(PING→ERASE_SLOT→WRITE_CHUNK 반복→FINALIZE→REBOOT 시퀀스), `MainForm.cs`(UI).
- Flash 레이아웃: Bootloader 48KB(0x08000000) / Slot A 224KB(0x0800C000) / Slot B 224KB(0x08044000) / 메타데이터 4KB(0x0807C000) / 예비 12KB. 최초 32KB로 시작했으나 USB Device 스택만으로 초과해 48KB로 확장(`DEC-023` 참고).
- 진행 경위 중 발견/수정한 사항:
  - Bootloader 프로젝트를 32KB로 시작했으나 USB Device 스택 포함 시 5.4KB 초과해 48KB로 확장.
  - `stm32l4xx_hal_iwdg.c/.h`가 프로젝트에 없어 CubeMX에서 IWDG 페리페럴을 활성화하고 코드를 재생성해 확보(Prescaler 64, Reload 1499, Window 4095 = 약 3초 타임아웃).
  - 최초 설계는 "부트로더가 매 부팅마다 짧은 USB 대기창을 두고 트리거를 기다리는" 방식이었으나, 앱에 이미 USB CDC(`USB_DEVICE/App/*`, `MX_USB_DEVICE_Init()`가 `StartDefaultTask`에서 상시 호출됨)가 구현되어 있었다는 사실을 뒤늦게 코드 확인으로 발견해 "앱 상주 트리거" 방식으로 재설계(`DEC-023` 참고).
- 실 보드 검증 중 발견/수정한 버그 (2026-07-06, 시간순):
  1. **부트로더 롤백 로직이 `retry_count`를 전혀 안 씀**: `state==PENDING_TEST`이면 무조건 반대 슬롯으로 롤백해서, 방금 올린 새 펌웨어를 한 번도 실행해보지 못하고 되돌아갔다. `retry_count==0`(첫 시도)이면 일단 부팅시켜보고, 그래도 `Bootloader_ConfirmBootOk()`가 안 불린 채 다시 부팅되면(2번째, `retry_count!=0`) 그때 롤백하도록 `main.c` 수정.
  2. **부트로더가 슬롯이 비어있을 때 `Error_Handler()`로 멈춰버림**: 공장 출하 상태(Slot A/B 둘 다 비어있음)에서 `Bootloader_JumpToSlot()`이 무효 이미지로 호출되면 USB도 안 켜고 무한루프. `Bootloader_SlotHasValidImage()`로 사전 확인 후 무효면 USB 대기 모드로 전환하도록 `main.c`/`boot_jump.c` 수정.
  3. **WRITE_CHUNK payload가 `BL_FRAME_MAX_PAYLOAD`(256)를 초과**: offset(4)+data(256)=260 > 256이라 부트로더 파서가 "있을 수 없는 길이"로 조용히 버림(응답 없음, GUI는 타임아웃). `BL_FRAME_MAX_PAYLOAD`를 `BL_FRAME_CHUNK_SIZE+8`로 수정.
  4. **부트로더가 여러 USB 패킷(64바이트 단위)으로 나뉘어 오는 WRITE_CHUNK 요청에 응답 안 함**: 원인 미해결로, GUI 쪽 청크 크기를 48바이트(프레임 전체가 패킷 1개 안에 들어가도록)로 낮춰 우회(`Tools/FirmwareUpdater/UpdateService.cs`).
  5. **점프 직전 NVIC 인터럽트 정리 누락**: `Bootloader_JumpToSlot()`이 TIM6만 끄고 USB 등 다른 인터럽트는 켜둔 채 `__enable_irq()`를 불러서, 앱이 자기 페리페럴을 초기화하기 전에 인터럽트가 발생해 간헐적 HardFault가 났다. NVIC의 모든 인터럽트(ICER/ICPR)를 점프 전에 정리하도록 `boot_jump.c` 수정.
  6. **`BlBootMetadata_t` 메타데이터 쓰기 시 CRC32 필드 자체가 Flash에 기록 안 됨**: ARM GCC가 임베디드 빌드에서 `enum`을 기본적으로 1바이트로 압축(`-fshort-enums`, ARM EABI 표준)하는 바람에 구조체 실제 크기가 56바이트가 아니라 52바이트였는데, `sizeof(meta)/8` 정수 나눗셈이 52/8=6으로 잘려서 마지막 4바이트(CRC32 필드)가 안 써짐 → CRC 검증이 항상 실패 → `BootMeta_Load()`가 매번 "메타데이터 없음" 기본값을 반환. `boot_meta.c`/`a_bootloader_link.c`의 쓰기 루프를 `boot_flash.c`처럼 나머지 바이트 패딩 방식으로 수정.
  7. **(가짜 단서) Flash 캐시 스테일 의심**: 위 6번 수정 전, 증상이 비슷해 STM32L4의 I/D 캐시(`FLASH_ACR`)를 의심하고 `BootFlash_FlushCaches()`(disable→reset→enable)를 `boot_flash.c`/`boot_meta.c`/`boot_jump.c`에 추가했다. 근본 원인은 아니었지만 안전장치로는 유효해 그대로 유지.
  8. **★근본 원인★ - `Shared/bootloader_shared.h`가 두 프로젝트에 "링크"가 아니라 "복사본"으로 들어가 있었음**: `Bootloader/Bootloader/Core/Inc/bootloader_shared.h`가 초기 임포트 시점(2026-07-06 12:53)의 스냅샷에 고정된 채, 이후 `BL_BOOTLOADER_SIZE`(32→48KB), `BL_SLOT_A_BASE`(0x08008000→0x0800C000), `BL_SLOT_B_BASE`(0x08040000→0x08044000) 등 핵심 주소 변경사항을 전혀 반영하지 못하고 있었다. 그 결과 **부트로더는 SlotB를 0x08040000로 알고 소거/쓰기/점프**했지만 **IF_main의 `Release_SlotB.ld`는 0x08044000으로 빌드**되어, 실행 시 16KB(0x4000) 주소가 어긋나 벡터테이블/데이터 참조가 전부 틀어졌다. 이게 지금까지의 모든 간헐적·재현 어려운 크래시(HardFault, INVSTATE, "이상한 메모리 값")의 진짜 원인이었다. `cp`로 두 프로젝트의 복사본을 `Shared/` 원본과 동기화해 해결. **향후 `Shared/bootloader_shared.h` 수정 시 반드시 두 프로젝트의 복사본에도 동일하게 반영해야 한다**(IDE 링크를 신뢰하지 말 것).
  9. 진단용으로 `BL_CMD_READ_FLASH`(0x06) 명령과 GUI의 "Slot B 검증(읽기 비교)" 버튼을 추가(WRITE_CHUNK로 실제 기록된 바이트를 파일과 직접 대조). 8번 root cause 확정 후에는 필수는 아니나, 향후 유사 문제 진단에 유용해 유지 여부는 추가 논의 필요.
- 검증 상태: 부트로더/앱 개별 빌드(Debug, Release_SlotA, Release_SlotB) 성공 확인. 8번 수정 후 실 보드에서 GUI로 SlotA→SlotB 업데이트 시 **초록 LED 정상 점등(앱 정상 부팅) 확인** - 근본 원인 수정이 유효함을 실측으로 확인했다. **다만 Modbus/PLC 통신 응답 확인과, 재연결 시 메타데이터가 `CONFIRMED`로 전환됐는지 확인은 사용자가 다음 세션(익일)에 이어서 진행하기로 함.** A/B 왕복(SlotB→SlotA) 재업데이트, 정전/USB 단절 시나리오, 새 펌웨어 hang 유발 후 IWDG 자동 롤백 동작도 아직 미검증 - 다음 작업으로 남겨둔다.

## IMP-015 - PLC Write Config 그룹에 Apply Trigger 주소(317) 추가

- 목적: 외부통신프로토콜 사양서(`MXC-HIF_통합_개발_사양서_Rev00.xlsx`, "2. 통신 프로토콜 정의" 시트)에 PLC Write Slave ID `8`(Slave ID 설정) 그룹 addr `317`="Apply Triger" 항목이 추가되었으나, 코드(`WRITE_CONFIG_END=316`)와 문서(`05-protocol.md` 등)에는 반영되어 있지 않아 동기화했다.
- 구현 파일: `User/Application/Src/a_plc_group_map.c`(`WRITE_CONFIG_END` 316→317, `ApplyConfigWrite()`), `User/Application/Inc/a_plc_group_map.h`(주석).
- 동작 변경:
  - PLC Write addr `306~317`(12개, 기존 306~316/11개에서 확장)이 global HR `110~121`로 그대로 연속 매핑된다(`CONFIG_HOLDING_BASE=110` 기준 offset 계산이라 `317`은 자동으로 `121`=apply trigger에 매핑됨, 별도 case 분기 불필요).
  - 기존에는 PLC가 `306~316` 아무 값이나 write하면 `ApplyConfigWrite()`가 매번 무조건 HR121(apply trigger)에 `1`을 자동 세팅했다. 이 자동 세팅 코드를 제거하고, PLC가 `317`을 명시적으로 write해야만(즉 값이 그대로 HR121에 반영되어야만) `ServiceConfigApply()`가 적용을 시도하도록 변경했다.
- 문서 갱신: `05-protocol.md`(PROTO-010, Write 그룹 상세 addr 306~317), `04-data-model.md`(DATA-013, HR121 설명), `03-interfaces.md`(IF-009, 입력 항목).
- 검증: 코드 수정만 진행, 빌드/실 보드 검증은 아직 수행하지 않음.

## IMP-016 - HR199 comm status bit를 슬레이브별 AND 조합으로 재계산

- 목적: DIO(2개)/Heating(3개)/Gas(3개)처럼 그룹에 슬레이브가 여러 개 달린 경우, 슬레이브 하나가 빠져도 나머지가 정상 응답하면 그룹 comm bit(`199`)가 다시 1로 켜지던 기존 동작을 사용자가 문제로 지적. "구성된(configured) 슬레이브가 전부 온라인이어야 그룹 bit=1, 하나라도 오프라인이면 계속 0"으로 의미를 바꾸기로 확인 후 진행.
- 구현 파일: `User/Application/Src/a_shared_map.c`, `User/Application/Src/a_modbus_master.c`.
- 동작 변경:
  - `a_shared_map.c`에 `RecomputeGroupCommBit()` 추가: `CommConfig_GetActiveIds(group)`로 얻은 구성된 슬레이브 개수만큼 DATA-012 슬레이브 상태 레지스터(131~145)를 순회해 전부 온라인(`0x0100` bit)인지 검사하고, 결과를 `199`의 해당 그룹 bit에 그대로 반영(threshold 없이 즉시 set/clear). 구성 안 된 슬롯은 `active_ids->count`에 안 잡혀 자동 제외된다.
  - `SharedMap_SetSlaveStatus()` 마지막에 `RecomputeGroupCommBit()`을 호출해, 슬레이브 상태가 갱신될 때마다 그룹 bit가 자동 재계산되도록 연결.
  - `a_modbus_master.c`의 폴링 성공/실패 경로(`RetryOrGivePoll()`, 폴링 응답 처리)에서 중복 호출이던 `SharedMap_SetCommStatus(group, ...)`를 제거(이제 `SetSlaveStatus`가 트리거).
  - PLC 포워딩 write 성공/실패 경로의 `SharedMap_SetCommStatus()` 호출도 제거 - write는 best-effort 1회 시도라 노이즈가 크고, 어차피 다음 폴링 사이클에서 슬레이브 상태가 정확히 반영되므로 그룹 bit에는 반영하지 않기로 사용자와 합의.
  - USART1 GST는 슬레이브 인덱스 개념이 없는 단일 디바이스라 `SharedMap_SetCommStatus()`(연속 3회 실패 threshold 방식) 그대로 유지, 변경 없음.
- 문서 갱신: `04-data-model.md`(DATA-006).
- 검증: STM32CubeIDE 헤드리스 빌드(`Debug` 구성) 0 errors, 0 warnings 확인. 실 보드 검증은 아직 수행하지 않음.

## IMP-017 - Heating EEPROM Save/ALARM Reset 자동 클리어 timeout, UART4 watchdog timeout 1초 → 5초 변경

- 목적: 사용자가 두 timeout 값이 1초로 설정된 게 맞는지 확인 요청 → 코드 확인 결과 둘 다 1000ms 확인 → 5000ms로 변경 요청.
- 대상:
  - `HEATING_EEPROM_ALARM_TIMEOUT_MS`(`a_plc_group_map.c`): PLC가 Heating EEPROM Save/ALARM Reset을 `1`로 write한 뒤 재write 없이 이 시간이 지나면 PLC 명령이 끝난 것으로 보고 `PlcGroupMap_Poll()`이 슬레이브에 `0`을 대신 write하는 자동 클리어 타이밍(rising edge 트리거 방식, 798155a/3cf389c 커밋에서 구현, 문서 미반영 상태였음). `1000U` → `5000U`.
  - `APP_UART4_WATCHDOG_TIMEOUT_MS`(`a_application.c`): PLC(UART4) 통신 두절 시 DIO fail-safe 0 처리를 트리거하는 watchdog timeout(DEC-021, PROTO-009). `1000U` → `5000U`.
- 문서 갱신: `06-design-decisions.md`(DEC-021), `05-protocol.md`(PROTO-009).
- 검증: STM32CubeIDE 헤드리스 clean 빌드(`Debug` 구성) 0 errors, 0 warnings 확인. 실 보드 검증은 아직 수행하지 않음.

## 변경 이력

- 2026-06-27: IMP-001 기준점 확인 결과 기록.
- 2026-06-27: IMP-007 구현 내용과 리뷰 대기 상태 기록.
- 2026-06-27: IMP-007 수정 완료, application 호출부 연결, 리뷰 통과 상태 기록.
- 2026-06-27: IMP-008 GST map 연동 구현, 리뷰 수정, 재리뷰 통과 상태 기록.
- 2026-06-27: IMP-009 bridge/hub 경로 부재 확인 및 application 경로 연결 상태 기록.
- 2026-06-27: IMP-010 Debug clean-first 빌드 성공 및 메모리 사용량 기록.
- 2026-06-27: 통신 task 문서 기준을 `UART4_Task`, 하위 3포트 task, USART1 GST 3-task 구조로 갱신하고 후속 재구현/재검증 필요 사항 기록.
- 2026-06-27: USART1/GST 공용 영역의 통합 map 동기화 주체를 USART1 GST task로 명확화.
- 2026-06-28: 7-task 구조 기준 구현/빌드 상태와 `Application_CreateTasks()` 생성 구조로 IMP-007~IMP-010 기록 갱신.
- 2026-06-30: 현재 코드 기준으로 CommConfig 기본값, SharedMap 660-entry 구조, 즉시 ACK write 경로, ModbusMaster 3-step/queue 구조를 갱신.
- 2026-06-30: SharedMap RAM2 배치 계획과 검증 기준 추가.
- 2026-06-30: SharedMap RAM2 배치 구현 후 Debug 빌드와 map section 주소를 검증.
- 2026-06-30: UART4 PLC watchdog fail-safe 구현 기준 추가 및 Debug 빌드 확인.
- 2026-07-06: IMP-013(런타임 Config Apply, HR110~122) 소급 반영. 코드 감사로 기존 문서의 "PLC register 설정 변경 미포함" 서술이 현재 코드와 다름을 확인하고 정정.
- 2026-07-06: IMP-014(USB 펌웨어 업데이트, 부트로더 A/B 이중 슬롯 + C# GUI) 진행 기록 추가. 개별 빌드 확인 완료, 실 보드 검증 전.
- 2026-07-06: 실 보드 테스트 중 발견한 버그 9건 기록(retry_count 롤백 미사용, 빈 슬롯 Error_Handler 정지, WRITE_CHUNK payload 크기 초과, 멀티패킷 USB 응답 없음, 점프 전 NVIC 미정리, enum 1바이트 압축으로 인한 메타데이터 CRC 미기록, Flash 캐시 플러시 추가, ★근본 원인: Shared/bootloader_shared.h가 두 프로젝트에 링크 아닌 복사본으로 들어가 SlotB 주소가 16KB 어긋남★, 진단용 READ_FLASH 명령 추가). 근본 원인 수정 후 실 보드에서 SlotB 정상 부팅(초록 LED) 확인. Modbus 통신 확인과 CONFIRMED 전환 확인은 다음 세션으로 이월.
- 2026-08-03: IMP-016(HR199 comm status bit 재계산 방식 변경) 추가. DIO/Heating/Gas 그룹 bit를 슬레이브별 상태 AND 조합으로 재계산하도록 변경, GST는 변경 없음. Debug 빌드 0 errors/0 warnings 확인.
- 2026-08-03: IMP-017(Heating EEPROM Save/ALARM Reset 자동 클리어 timeout, UART4 watchdog timeout 1초→5초 변경) 추가. DEC-021, PROTO-009 갱신. Debug 빌드 0 errors/0 warnings 확인.
- 2026-07-16: IMP-015(PLC Write Config 그룹 addr 317=Apply Trigger 추가) 반영. 사양서 xlsx에 추가된 317을 코드/문서에 동기화하고, 306~316 write 시 자동 trigger 세팅 동작을 제거해 317 명시적 write만 trigger로 인정하도록 변경. 빌드/실 보드 검증은 미수행.
