# Communication Checklist

AI용 압축 체크리스트. 근거 문서: `../documents/03-interfaces.md`(IF-001~011), `../documents/05-protocol.md`(PROTO-001~012).

## 1. UART 물리 채널 (PROTO-001)

- [ ] UART4: RS485, 115200 bps, 8N1 (PLC Modbus slave)
- [ ] LPUART1: RS485, 38400 bps, 8N1 (DIO master)
- [ ] USART2: RS485, 19200 bps, 8N1 (Heating Jacket master)
- [ ] USART3: RS485, 9600 bps, even parity, 1 stop bit (Gas Detector master)
- [ ] USART1: RS422, 115200 bps, 8N1 (GST)
- [ ] 공통: no flow control, LSB first
- [ ] 전 채널 DMA circular + IDLE callback 수신 동작
- [ ] DE/RE GPIO 방향 제어 정상 동작 (IF-008)

## 2. UART4 PLC Modbus RTU Slave (IF-003, PROTO-002)

- [ ] Slave ID `0~247` 허용, `248~255` 미허용, ID `0`은 broadcast 아님(일반 ID 응답)
- [ ] global 영역(설정 110~125 / 슬레이브상태 131~145 / GST 199~322) 판정이 FC03(Holding Register)에만 적용
- [ ] global 영역 이외 function code로 global 주소 요청 시 응답 없음
- [ ] active_id_list에 없는 ID는 global 영역 제외하고 응답 없음
- [ ] 지원 FC: 01/02/03/04/05/06/0F/10 정상 처리
- [ ] Read: 즉시 map 값 반환(하위 즉시 polling 아님)
- [ ] Write: SharedMap 즉시 commit 후 즉시 ACK, 하위 전달은 best-effort(ACK 되돌리지 않음)
- [ ] CRC 오류/frame 길이 오류/미정의 entry → 응답 없음(Modbus exception 미사용)

## 3. 하위 Modbus RTU Master (IF-005, PROTO-003, PROTO-008)

- [ ] 포트별 active ID 기본값: LPUART1 `{1,2}`, USART2 `{20,21,22}`, USART3 `{30,40,50}`
- [ ] polling period 기본값 3포트 모두 50 ms
- [ ] response timeout: LPUART1 50 ms / USART2 200 ms / USART3 100 ms
- [ ] retry count 기본 3회 (read poll에만 적용)
- [ ] tx_queue(write, depth4) 우선순위가 poll(read)보다 높은지
- [ ] write forward는 재시도 없음(best-effort), 응답 대기 중 동일 포트 다음 항목 전송 금지(버스 충돌 방지, `pending_is_write` 상태, 2026-07-16 반영분)
- [ ] 순차 poll: active ID 오름차순 → ID 내 poll item 순서 → 마지막 ID 이후 rollover
- [ ] 연속 3회 timeout/fail → 통신상태 bit 0, 1회 성공 → 1 복구
- [ ] SharedMap_RegisterSlave(config 기준, probe 없음) 정책 준수, Gas Type `0`(동작 안 함) 슬롯 polling 제외
- [ ] DIO/Heating/Gas 각 poll·write item 표(PROTO-008)와 실제 코드 주소/FC 일치

## 4. GST 프로토콜 - USART1 (IF-006, PROTO-004)

- [ ] 프레임: `[Header 3][FC 1][Addr Hi][Addr Lo][Len 1][Data N][CRC16 2][Tail 2]`
- [ ] Header `0x47 0x53 0x54`, Tail `0x53 0x49`, CRC Modbus CRC16 Lo/Hi
- [ ] FC `0x01`=Read, `0x02`=Write만 처리
- [ ] 3-task 분리: RxTask(수신 큐잉)/ProtocolTask(해석+map 반영)/TxTask(송신) 역할 분리
- [ ] write 영역 `201~241`, read 영역 `242~282`, 예약 `283~322`(기본값 0) 매핑
- [ ] period 기본 50 ms, 연속 3회 실패 시 `199` bit3 = 0, 1회 성공 시 복구
- [ ] UART4가 GST 영역(199~322) 요청 시 forwarding 없이 map만 사용
- [ ] PLC write 시 분기: `201~241`→실제 GST frame 전송, `283~322`→로컬 commit만

## 5. 통합 메모리 맵 (IF-004, PROTO-005)

- [ ] entry key `id + table_type + address` 일치
- [ ] SharedMap_Init()은 global entry(131~145, 199~322)만 생성, DIO/Heating/Gas는 미생성
- [ ] pool 크기 660 entries(DIO4+Heating171+Gas330+Global155) 유지
- [ ] 미등록 key 요청 시 자동 생성 없이 실패 반환
- [ ] 다중 task 접근 시 critical section 보호
- [ ] Gas 엔트리 H2+TFT 주소 합집합 등록 여부

## 6. UART4 PLC 4-그룹 통합 Read/Write 맵 (IF-011, PROTO-012)

- [ ] Read Slave ID: 1=DIO, 2=Heating(병합), 3=Gas(STD+TFT 병합), 4=Backup / addr `1~164`
- [ ] Write Slave ID: 5=DIO, 6=Heating, 7=Backup, 8=Slave ID 설정 / addr `201~317`
- [ ] Gas Status 8bit 패킹 규칙(bit0~7) 코드 일치
- [ ] Gas Type에 따라 STD/TFT 자리만 채우고 나머지 0 유지
- [ ] 제외 항목(FW버전 레지스터, Gas 설정값, TFT Lower word, TFT 코일)이 실제로 미노출
- [ ] 이 계층이 SharedMap/IF-003~006 기존 동작을 변경하지 않는지(번역 계층으로만 동작)

## 7. 오류·실패 판정 공통 정책 (PROTO-006)

- [ ] 응답 없음 조건(CRC 오류/frame 오류/ID 248~255/미등록 active ID/미등록 map 주소) 전 채널 일관 적용
- [ ] Modbus exception 응답 대신 무응답 정책 준수
- [ ] 연속 3회 실패=비정상, 1회 성공=정상 기준이 전 포트 동일하게 구현

## 8. RAM 통신 설정 / active ID (IF-007)

- [ ] 각 period/timeout/retry 필드 기본값과 RAM 반영 여부
- [ ] dio_ids/heating_ids/gas_ids/gas_types 슬롯 개수·기본값 일치
- [ ] revision 카운터가 설정 변경 성공시마다 1 증가
- [ ] Flash 저장 없음(전원 재인가 시 compile-time 기본값 복귀)

## 9. 런타임 Config Apply (IF-009, PROTO-010)

- [ ] HR `110~120` 입력, `121` trigger(=Write group addr `317`과 동일 레지스터), `122` 결과코드 매핑
- [ ] `ServiceConfigApply()`가 매 tick 폴링되고 HR121=1일 때만 적용 시도
- [ ] 검증 규칙: ID `0` 또는 `1~247`, 그룹 내 중복 불허, gas profile `0~2`
- [ ] 실패 시 기존 active list 미변경, 결과 코드만 기록
- [ ] 성공 시 revision 증가 → 하위 task가 감지해 `ReinitXxxContext()+RegisterConfiguredSlaves()` 재실행

## 10. UART4 PLC Watchdog Fail-safe (PROTO-009)

- [ ] `APP_UART4_WATCHDOG_TIMEOUT_MS` 기본 1000, `0`이면 비활성
- [ ] 유효 Modbus 요청 처리 시각 기준 timeout 판정
- [ ] timeout 시 DIO active ID 전체에 HR129=0 write 1회만 enqueue(latch)
- [ ] 통신 재개 시 latch 해제되어 재발동 가능

## 11. USB 펌웨어 업데이트 프로토콜 (IF-010, PROTO-011) — 실 보드 검증 전 상태 주의

- [ ] 프레임 `[0xAA][0x55][CMD][LEN:2][PAYLOAD][CRC16:2]`, CRC16이 SOF 제외 구간 대상
- [ ] 앱 측 `ENTER_UPDATE`만 인식, CRC 불일치/다른 CMD는 무응답
- [ ] 부트로더 PING/ERASE_SLOT/WRITE_CHUNK/FINALIZE/REBOOT 각 요청·응답 payload 형식
- [ ] WRITE_CHUNK offset 8배수 검증, FINALIZE는 리드백 CRC32 대조 후에만 메타데이터 갱신
- [ ] PC GUI 재시도(청크당 최대 3회) 및 실패 시 중단 로직

## 12. 코드-문서 정합성 (전체 공통)

- [ ] `a_modbus_master.c`의 TxStep/RxStep/ProcessStep 3단계 구조가 IF-005 기술과 일치
- [ ] `a_uart.c` 공통 수신 API(queue full 시 최신 frame 유지 정책) 일치
- [ ] `a_plc_group_map.c/h`가 PROTO-012/IF-011 표와 주소 매핑 일치
- [ ] `05-protocol.md` 변경이력 2026-07-16 항목(write forward 순차 대기)이 `a_modbus_master.c` 최근 커밋과 대조 반영됨
