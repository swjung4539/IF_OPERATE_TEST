# IF Controller 통신 구조 및 명령 흐름

## 1. 전체 버스 구조

```
PLC --[UART4 115200bps RS-485]-- IF Controller (STM32L452)
                                         |
                         +---------------+---------------+
                         |               |               |
                   [LPUART1]        [USART2]         [USART3]
                   38400bps         19200bps           9600bps
                   RS-485           RS-485             RS-485
                         |               |               |
                   DIO 슬레이브   Heating 슬레이브   Gas 슬레이브
                   ID: 1, 2       ID: 20,21,22      ID: 31,40,30
                                         |
                                   [USART1 115200bps]
                                         |
                                    GST 장치
```

각 버스는 물리적으로 독립 — DE/RE 핀이 분리되어 있어 동시 통신 가능.

---

## 2. SharedMap (중앙 캐시)

```
+--------------------------------------------------------------+
|                      SharedMap (RAM)                          |
|  - 전체 통신 데이터의 중간 저장소                               |
|  - 슬레이브 polling 결과 저장                                  |
|  - PLC Read 요청 즉시 응답용 캐시                              |
+--------------------------------------------------------------+
        ^  polling으로 채워짐          ^  PLC write로 갱신됨
        |                              |
  LPUART1/USART2/USART3           UART4_Task
  (50ms 주기 자동)                (PLC 요청 시만)
```

---

## 3. PLC Write 명령 전달 흐름

```
PLC
 |  FC06/FC16 Write Request (115200bps)
 v
UART4_Task (1ms 루프)
 |
 +-- ModbusSlave_HandleRequest()
 |       |
 |       +-- [global 주소: 110~125]
 |       |    SharedMap 즉시 커밋
 |       |    PLC에 즉시 ACK  <---------- PLC 관점에서 완료
 |       |    GST 범위면 USART1로도 전달
 |       |
 |       +-- [per-slave 주소: 129 등]
 |            SharedMap 즉시 커밋
 |            PLC에 즉시 ACK  <---------- PLC 관점에서 완료
 |            write 큐(깊이 4)에 삽입
 |                    |
 |                    v (비동기)
 |            LPUART1_Task / USART2_Task / USART3_Task
 |                    |
 |                    +-- waiting_response == 1? --> 대기 (최대 50ms)
 |                    +-- waiting_response == 0? --> 슬레이브에 전송
 |                                                   (TransmitOnly, 응답 안 기다림)
 v
슬레이브 장치 (실제 반영)
```

PLC는 슬레이브 완료 전에 즉시 ACK 수신 — best-effort 구조.

---

## 4. PLC Read 데이터 읽기 흐름

```
PLC
 |  FC03/FC04 Read Request (115200bps)
 v
UART4_Task
 |
 +-- ModbusSlave_HandleRequest()
         |
         +-- SharedMap에서 즉시 읽기 (캐시)
                    |
                    v
             PLC에 응답 전송  <---- 슬레이브 통신 없이 즉시 응답
```

SharedMap은 polling으로 주기적으로 갱신 — 최대 50ms 지연 가능.

---

## 5. 슬레이브 Polling 자동 읽기 흐름

```
LPUART1_Task (1ms 루프, 50ms 주기)
 |
 +-- Discovery 완료 여부 확인
 +-- write 큐 확인 -> 쓸 것 있으면 먼저 처리 (우선순위)
 +-- poll 타이머 50ms 경과?
         |
         +-- FC03 Read 전송 -> waiting_response = 1
                    |
                    +-- 응답 수신 -> SharedMap 갱신 -> waiting_response = 0
                    +-- 50ms 타임아웃 -> retry (최대 3회) -> give up
```

---

## 6. 버스별 쓰기 병목

```
LPUART1_Task 타임라인 예시:

t=0ms   : DIO 슬레이브 poll 시작 (waiting_response=1)
t=0ms   : PLC write 요청 도착 -> write 큐에 삽입 (UART4 즉시 ACK)
t=4ms   : DIO 응답 수신 (waiting_response=0)
t=5ms   : write 큐 처리 -> 슬레이브에 전달
t=50ms  : 다음 poll 시작
```

- PLC → IF Controller ACK: **즉시 (~1ms)**
- IF Controller → 슬레이브 반영: **최대 50ms** (poll 완료 후)
- write 큐 오버플로 시: **조용히 드롭** (PLC 미통보)

---

## 7. 타이밍 요약

| 경로 | 속도 | 1 프레임 (8 byte) |
|------|------|------------------|
| PLC → IF (UART4) | 115200 bps | ~0.69 ms |
| IF → DIO (LPUART1) | 38400 bps | ~2.08 ms |
| IF → Heating (USART2) | 19200 bps | ~4.17 ms |
| IF → Gas (USART3) | 9600 bps | ~8.33 ms |
| IF → GST (USART1) | 115200 bps | ~0.69 ms |

| 동작 | 지연 |
|------|------|
| PLC write → IF ACK | ~1~3 ms |
| IF → 슬레이브 write 반영 | 최대 50 ms |
| 슬레이브 → SharedMap 갱신 주기 | 50 ms |
| Discovery (슬레이브 미응답 시) | 최대 500 ms |
