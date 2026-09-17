# AT25SF041B 기반 Slave Desired-State 복구 보고서

## 목적

이 문서는 PLC 쓰기 명령 이후 IF 모듈이 watchdog reset되어 하위 Modbus Slave 명령이 유실되는 문제를 보완하기 위해 적용한 외장 Flash 복구 기능의 동작, 저장 데이터, 시퀀스, Flash 수명 및 검증 항목을 보고하기 위한 문서다.

## 결론 요약

| 항목 | 결과 |
|---|---|
| 대상 Flash | AT25SF041B, 4 Mbit / 512 KiB SPI NOR Flash |
| 통신 | STM32L452 SPI3, Mode 0, PA15 `MEM_CS3` software chip-select |
| 저장 대상 | 실제 하위 Slave의 상태형 write: DIO HR129, Heating HR2000~2020 중 HR2001/HR2002 제외 |
| 저장 제외 | EEPROM Save(HR2001), Alarm Reset(HR2002), GST/global/config write, trigger성 명령 |
| 저장 시점 | 실제 Slave port가 결정된 일반 write 직후, 최대 100 ms 이내 Flash 기록 |
| 복구 시점 | reset 후 Flash를 읽고, 해당 Slave port 준비 뒤 기존 Modbus queue로 재전송 |
| PLC 수정 | 불필요 |
| IF 보드 하드웨어 수정 | 불필요 |
| Debug build | 성공 |
| 최악 조건 Flash 수명 | 약 **5.76년** — 현재 구현만으로 10년 보장 불가 |

> 중요: 100 ms마다 값이 항상 변경되는 최악 조건에서는 현재 record 형식이 약 5.76년으로 계산된다. 따라서 10년이 절대 요구조건이면 variable-length delta record 최적화 또는 저장 주기/기록 정책 변경이 추가로 필요하다.

## 문제 정의

### 기존 시퀀스

```text
PLC                    IF 모듈                    하위 Slave
 | Write 요청             |                           |
 |----------------------->|                           |
 |                        | PLC 데이터 맵 해석         |
 |<-----------------------| PLC ACK                    |
 |                        |                           |
 |                        | Modbus write queue 입력    |
 |                        |-------------------------->|
 |                        |                           |
```

PLC ACK와 실제 하위 Slave 처리 사이에 IF 모듈이 watchdog reset되면, PLC는 성공으로 판단하지만 하위 Slave는 명령을 받지 못할 수 있다.

### 적용 후 시퀀스

```text
PLC                    IF 모듈                       AT25 Flash              하위 Slave
 | Write 요청             |                               |                       |
 |----------------------->|                               |                       |
 |                        | PLC 맵을 실제 Slave write로 변환                     |
 |<-----------------------| PLC ACK                        |                       |
 |                        | 최신 목표 상태를 RAM dirty로 표시                    |
 |                        |------------------------------>| 100 ms delta 저장    |
 |                        |                               |                       |
 |                        | Modbus write queue 입력                              |
 |                        |----------------------------------------------------->|
```

watchdog reset 후에는 다음 순서로 복구한다.

```text
IF 재부팅
  -> SPI3 Flash 초기화
  -> 유효한 Flash sector 탐색
  -> 가장 최신 checkpoint + delta record 복원
  -> 하위 Modbus port 초기화 완료 대기
  -> 기존 Modbus write queue에 복구 write 투입
  -> 하위 Slave에 최종 상태 재전송
```

## 적용 코드 및 책임

| 파일 | 책임 |
|---|---|
| `User/Application/Src/a_at25sf041b.c` | AT25SF041B SPI3 read/program/erase driver |
| `User/Application/Src/a_desired_state_mirror.c` | desired state RAM mirror, CRC/commit, wear leveling, 복구 전송 |
| `User/Application/Src/a_modbus_master.c` | 일반 write 추적과 복구 write의 queue 전달 분리 |
| `User/Application/Src/a_application.c` | 초기화, 100 ms 저장 service, port ready 통지 |
| `CMakeLists.txt` | 신규 source build 등록 |

### Modbus write 처리 정책

일반 PLC write는 `ModbusMaster_ForwardWrite()`를 사용한다.

1. 실제 Modbus Slave port를 찾는다.
2. 허용된 상태형 주소이면 `DesiredStateMirror_TrackWrite()`로 RAM 최신값을 갱신한다.
3. 기존 하위 Slave transmit queue에 넣는다.

watchdog 복구 write는 `ModbusMaster_ForwardRecoveredWrite()`를 사용한다.

1. 기존 transmit queue에 넣는다.
2. Flash mirror에는 다시 기록하지 않는다.
3. 따라서 복구 반복으로 Flash 수명이 소모되지 않는다.

## 저장 대상

### 저장하는 데이터

Flash에는 PLC 통합 주소가 아닌 변환이 끝난 실제 Slave 명령을 저장한다.

| 필드 | 설명 |
|---|---|
| `port` | LPUART1 / USART2 / USART3 중 실제 하위 통신 port |
| `slave_id` | 실제 Modbus Slave ID |
| `table` | Modbus table. 현재 Holding Register만 허용 |
| `address` | 실제 Slave register 주소 |
| `value` | 마지막 목표 값 |
| `valid` | 유효 slot 여부 |

### 현재 허용 주소

| 장치 | 저장 주소 | 이유 |
|---|---:|---|
| DIO | HR129 | 출력 상태이며 마지막 상태 복구가 가능 |
| Heating | HR2000, HR2003~HR2020 | 상태/설정값으로 마지막 값 복구 가능 |

### 의도적으로 제외한 주소 및 명령

| 대상 | 제외 이유 |
|---|---|
| Heating HR2001 EEPROM Save | reset 뒤 재실행하면 불필요한 Flash/Slave EEPROM 저장이 발생할 수 있음 |
| Heating HR2002 Alarm Reset | reset 뒤 재실행하면 alarm 상태를 의도치 않게 해제할 수 있음 |
| GST/global write | 일반 Modbus Slave write 경로가 아니므로 1차 범위에서 제외 |
| runtime config write | IF 내부 설정이며 Slave desired state와 성격이 다름 |
| trigger/start/toggle/pulse 명령 | 마지막 값만 재적용하는 방식으로는 안전하게 복구할 수 없음 |

## Flash 저장 구조

### Flash 물리 구성

| 항목 | 값 |
|---|---:|
| Flash 전체 크기 | 512 KiB = 524,288 bytes |
| erase sector 크기 | 4 KiB = 4,096 bytes |
| sector 수 | 128 |
| page program 크기 | 최대 256 bytes |
| SPI mode | Mode 0 (CPOL Low, CPHA 1 Edge) |

### Wear Leveling 구조

Flash 전체 128개 sector를 순서대로 순환 사용하는 ring 구조다.

```text
Sector 0 -> Sector 1 -> ... -> Sector 127 -> Sector 0
```

현재 sector가 가득 차거나 새 Slave address가 처음 등록되면 다음 sector를 erase한 뒤 checkpoint를 기록한다.

### Sector Header

Sector 시작에는 전체 최신 상태를 저장하는 checkpoint header가 있다.

| 구성 | 크기 |
|---|---:|
| magic | 4 bytes |
| format version | 2 bytes |
| header size | 2 bytes |
| generation | 4 bytes |
| 64개 slot × 8 bytes | 512 bytes |
| CRC32 | 4 bytes |
| commit | 1 byte |
| 합계 | **529 bytes** |

`generation`이 가장 큰 유효 sector가 최신 sector다.

### Delta Record

100 ms 동안 변경된 상태를 저장한다.

| 구성 | 크기 |
|---|---:|
| magic | 2 bytes |
| value count | 1 byte |
| reserved | 1 byte |
| 64-slot changed bit mask | 8 bytes |
| 최대 4개 value | 8 bytes |
| CRC32 | 4 bytes |
| commit | 1 byte |
| 합계 | **25 bytes** |

현재 구현은 최대 4개의 변경값을 하나의 record에 묶는다. 1개만 변경되어도 고정 record 형식상 25 bytes를 사용한다.

### 무결성 보장

Flash write 중 watchdog reset 또는 전원 차단이 발생할 수 있으므로 다음 순서로 기록한다.

```text
1. record 본문 기록
2. CRC32가 포함된 record 기록
3. 마지막으로 commit byte를 0x00으로 기록
```

부팅 시 다음 조건을 모두 만족할 때만 유효 record로 인정한다.

```text
magic 일치
format version 일치
commit byte = 0x00
CRC32 일치
```

불완전하거나 CRC가 틀린 record는 무시한다.

## 저장 및 복구 시퀀스

### 일반 PLC 쓰기

```text
1. PLC가 UART4로 write 요청 전송
2. IF 모듈이 PLC 그룹 맵을 실제 Slave write로 변환
3. IF 모듈이 PLC에 기존 정책대로 즉시 ACK
4. ModbusMaster_ForwardWrite()가 실제 Slave port를 선택
5. 허용 주소이면 DesiredStateMirror_TrackWrite()가 RAM slot의 최신값과 dirty bit 갱신
6. 기존 Modbus transmit queue에 write request 입력
7. 하위 Slave task가 기존 방식으로 Modbus frame 전송
8. UART4 task의 100 ms service가 dirty 값 최대 4개를 Flash delta record로 기록
```

### 새 Slave 주소 최초 등록

```text
1. 기존 64개 RAM slot에서 port/slave/table/address가 같은 slot을 찾음
2. 없으면 빈 slot에 실제 Slave 식별자와 값 등록
3. 다음 100 ms service에서 다음 Flash sector를 erase
4. 전체 RAM slot을 포함하는 새 checkpoint header 기록
5. commit byte 기록 후 새 sector를 활성 sector로 변경
```

새 slot의 주소 정보는 delta record에 포함되지 않으므로 checkpoint에 기록한다.

### watchdog reset 후 복구

```text
1. Application_Init()에서 SPI3 Flash 초기화
2. 128개 sector header를 읽음
3. CRC/commit이 유효한 header 중 generation이 가장 큰 sector 선택
4. 해당 checkpoint를 RAM slot에 복원
5. 해당 sector의 유효 delta record를 순서대로 적용
6. 유효 slot을 restore pending으로 표시
7. DIO/Heating/Gas task가 Slave 등록을 마치면 해당 port ready 표시
8. DesiredStateMirror_RestoreService()가 한 번에 하나의 복구 request를 기존 Modbus queue에 입력
9. queue 입력 성공한 slot만 restore pending을 해제
10. Slave task가 기존 Modbus 처리 방식으로 Slave에 재전송
```

## Flash 수명 계산

### 사용한 전제

| 항목 | 값 |
|---|---:|
| Flash erase/program endurance 가정 | 100,000 cycles / sector |
| 사용 sector 수 | 128 |
| sector당 delta 저장 가능 영역 | 4,096 - 529 = 3,567 bytes |
| delta record 크기 | 25 bytes |
| sector당 delta record 수 | floor(3,567 / 25) = **142 records** |
| worst-case 기록 주기 | 100 ms = 초당 10 records |

### 최악 조건 수명

```text
총 기록 가능 record 수
= 128 sectors × 142 records/sector × 100,000 erase cycles
= 1,817,600,000 records

100 ms마다 1 record 기록 시
= 1,817,600,000 / 10 records/s
= 181,760,000 seconds

연수 환산
= 181,760,000 / 31,557,600
= 약 5.76 years
```

### 수명 결론

| 실제 기록 빈도 | 이론상 수명 |
|---|---:|
| 100 ms마다 항상 기록 | 약 5.76년 |
| 200 ms마다 기록 | 약 11.52년 |
| 500 ms마다 기록 | 약 28.8년 |
| 1초마다 기록 | 약 57.6년 |

현재 구현은 같은 값은 기록하지 않으므로, 실제 값 변경이 매 100 ms보다 적으면 수명은 위 비율로 늘어난다.

### 10년 요구조건 판단

현재 구현의 고정 25-byte delta record는 100 ms worst-case 조건에서 10년을 만족하지 못한다.

10년을 만족하기 위한 최대 평균 record 크기는 다음과 같다.

```text
필요 record 수 = 10 years × 31,557,600 s/year × 10 records/s
               = 3,155,760,000 records

sector당 필요 record 수
= 3,155,760,000 / (128 × 100,000)
= 약 247 records/sector

허용 record 크기
= 3,567 / 247
= 약 14.4 bytes 이하
```

따라서 10년을 worst-case로 보장하려면 아래 중 하나가 필요하다.

1. `value_count`에 맞춰 길이가 달라지는 variable-length delta record로 변경한다.
2. 1개 값 변경 record를 약 14 bytes 이하로 줄인다.
3. Flash 저장 주기를 200 ms 이상으로 늘린다.
4. 실제 변경이 없는 반복 PLC write를 계속 제외한다.
5. Flash에 기록할 주소 수/정책을 더 제한한다.

현재 구현은 **기능 복구는 가능하지만, 100 ms worst-case 10년 보장은 미달**이다.

## 실제 운용에서 수명에 영향을 주는 요소

| 요소 | 수명 영향 |
|---|---|
| 동일값 반복 PLC write | 기록하지 않으므로 수명 증가 |
| 100 ms 안에 1~4값 변경 | 하나의 record로 묶이므로 수명 증가 |
| 새 Slave 주소 최초 등록 | checkpoint sector 전환 1회 발생 |
| Flash write 실패 | 손상 가능 record 위치를 버리고 새 checkpoint sector 사용 |
| 값이 계속 바뀌는 setpoint | Flash 수명 감소 |
| 펄스 명령 제외 | 불필요한 write 및 위험한 replay 방지 |

## 안전 및 실패 처리

| 상황 | 처리 |
|---|---|
| Flash read/write/erase 실패 | mirror 기능이 동작하지 않을 수 있으나 기존 PLC/Slave 통신은 유지 |
| Flash write 중 reset | commit/CRC가 불완전한 record를 부팅 시 무시 |
| delta record 저장 실패 | 현재 sector의 손상 가능 위치를 재사용하지 않고 다음 sector checkpoint 사용 시도 |
| Modbus queue full | 복구 slot을 유지하고 다음 service cycle에 재시도 |
| Slave port 미준비 | 해당 port의 복구 전송 보류 |
| pulse command | Flash 저장 및 replay 제외 |

## 현장 검증 항목

### SPI Flash 기본 검증

| 번호 | 확인 내용 | 합격 기준 |
|---|---|---|
| 1 | JEDEC ID read | AT25SF041B의 정상 ID 수신 |
| 2 | 4 KiB erase | test sector가 모두 `0xFF` |
| 3 | page program/read-back | 기록 데이터와 read-back 데이터 일치 |
| 4 | status WIP | write/erase 뒤 WIP가 정상 해제 |

### 기능 검증

| 번호 | 시나리오 | 합격 기준 |
|---|---|---|
| 1 | DIO HR129 write | Flash dirty/delta 기록 및 Slave write queue 입력 |
| 2 | Heating HR2000 write | Flash delta 기록 및 Slave write queue 입력 |
| 3 | HR2001 EEPROM Save | Flash 저장 및 reboot replay 없음 |
| 4 | HR2002 Alarm Reset | Flash 저장 및 reboot replay 없음 |
| 5 | write 직후 watchdog reset | reboot 뒤 마지막 값이 Slave에 재전송 |
| 6 | 동일값 반복 write | 새 Flash delta record 없음 |
| 7 | queue full | 복구 요청이 다음 cycle에 재시도 |
| 8 | DIO fail-safe 0 write | reboot 후 과거 ON 값이 복구되지 않는지 확인 |

## 남은 권장 개선

### 우선순위 1: 10년 수명 요구 충족

현재 25-byte 고정 delta record를 variable-length record로 변경한다.

예상 compact record 예시:

| 구성 | 1개 값 변경 | 4개 값 변경 |
|---|---:|---:|
| magic + count | 3 bytes | 3 bytes |
| changed mask | 8 bytes | 8 bytes |
| value | 2 bytes | 8 bytes |
| CRC16 또는 축소 CRC 정책 | 2 bytes | 2 bytes |
| commit | 1 byte | 1 byte |
| 합계 | 16 bytes | 22 bytes |

10년 worst-case를 보장하려면 bit mask 크기, CRC 정책, address slot 수까지 포함해 평균 14 bytes 이하가 되도록 별도 재설계와 수명 재계산이 필요하다.

### 우선순위 2: Flash diagnostic

다음 진단 값을 남기는 것을 권장한다.

```text
JEDEC ID
현재 active sector
generation
Flash write/erase failure count
복구 전송 성공/실패 count
마지막 CRC error count
```

### 우선순위 3: 실제 Slave 응답 검증 강화

Slave write 응답은 다음을 확인해야 한다.

```text
Slave ID
Function code
Exception response
Write address
Write quantity/value echo
CRC
```

이 기능은 Flash 복구와 별개지만, 실제 Slave write 성공 여부를 더 정확하게 판단하기 위해 필요하다.

## 참고 자료

- [Renesas AT25SF041B 제품 페이지](https://www.renesas.com/en/products/at25sf041b)
- [AT25SF041B Datasheet](https://www.renesas.com/en/document/dst/at25sf041b-datasheet)

## 변경 이력

- 2026-09-01: AT25SF041B desired-state 복구 기능 구현 기준으로 최초 작성.
