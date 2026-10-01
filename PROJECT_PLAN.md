# OP_test COMM/DIO 보드 검사 에뮬레이터 구현 계획

작성일: 2026-09-23  
대상 폴더: `C:\Users\LAB\Desktop\MSC_TEST\OP_test`

## 1. 문서 목적

이 문서는 사용자와의 대화 및 현재 워크스페이스 분석 결과를 기준으로 `OP_test`에 새로 구현할 보드 검사 에뮬레이터의 최종 범위와 구현 순서를 정의한다.

이 문서 이후 구현에서는 현재 유효한 범위를 우선하며, 이전에 논의했던 부트로더·펌웨어 업데이트·버전/CRC 검사 계획은 적용하지 않는다.

## 2. 최종 목표

기존 Fault Emulator의 통신 계층과 UI 구성요소를 필요한 만큼 재사용하여 다음 두 보드를 검사하는 독립 실행형 Windows 프로그램을 만든다.

초기 화면:

1. `COMM 보드 검사`
2. `DIO 보드 검사`

검사 범위:

- COMM 보드
  - UART4 PLC 통신
  - LPUART1 DIO 통신
  - USART2 Heating Jacket 통신
  - USART3 Gas Detector 통신
  - USART1 Backup GST 통신
  - 로터리 스위치 0~F 검사
  - FAULT 릴레이 OPEN/CLOSE 검사
- DIO 보드
  - ID DIP 스위치 조작에 따른 현재 ID 1~4 실시간 표시
  - PC의 DO1~16 출력 명령
  - DO→DI 1:1 루프백 자동 검사
  - DO LED 검사자 육안 확인

## 3. 최종 제외 범위

다음 기능은 구현하지 않는다.

- 부트로더 화면
- 펌웨어 업데이트
- HEX 파일 선택 및 기록
- 펌웨어 버전 확인
- CRC 비교
- 최신 버전 판정
- 범용 Modbus 수동 패킷 화면
- 기존 IFN-01~50 전체 검사 화면
- 장시간 반복 및 장애 카탈로그 UI
- Fault 대상 선택 화면
- 전체 로그·리포트·세션 기록 화면
- 결과 내보내기 및 통계 화면

DIO ID DIP는 통신 주소 탐색에 사용하지 않고 ST-LINK로 PA0/PC15 GPIO를 직접 읽어 화면에 ID 1~4로 표시한다.

## 4. 원본 코드 보호 원칙

현재 워크스페이스의 펌웨어는 다음 Git 기준으로 깨끗한 상태다.

- MAIN 저장소: `MXC-HIFx/main/IF_Controller`
  - 확인 커밋: `5293673 fix: drain Modbus transactions before config apply`
- SUB 저장소: `MXC-HIFx/sub/mxc-hif1-dio`
  - 확인 커밋: `9777987 ID 설정 값 변경`
- Fault Emulator 저장소
  - 확인 커밋: `d4c5690 fix: stabilize fault tests and run summaries`

원칙:

- MAIN/SUB 운용 펌웨어를 수정하지 않는다.
- 기존 Fault Emulator를 직접 수정하지 않는다.
- 모든 신규 PC 코드는 `OP_test` 내부에서만 작성한다.
- 필요한 기존 C# 코드는 `OP_test`로 선별 복사하여 독립 프로젝트로 관리한다.
- `Fault Emulator/.work`, `backup_original`, 과거 TestMode 작업물은 구현 기준으로 사용하지 않는다.
- MAIN의 `Drivers`, `Middlewares`, `m_*.c`, `m_*.h`는 수정하지 않는다.

## 5. 분석한 기준 코드

### 5.1 기존 Fault Emulator

경로:

`C:\Users\LAB\Desktop\MSC_TEST\Fault Emulator\IF-emulator\IF_Fault_Emulator`

재사용 대상:

- `TestEmulator/Controls`
- `TestEmulator/Theme`
- `TestEmulator/Services/Serial`
- `TestEmulator/Services/Modbus`
- `TestEmulator/Services/ModbusSlave`
- `TestEmulator/Services/Gst`

재사용 가능한 주요 기능:

- COM 포트 검색, 연결, 해제
- 채널별 독립 COM 포트 관리
- Modbus RTU FC03/FC04/FC06/FC10
- CRC, Slave ID, Function Code, 응답 길이 검증
- Timeout 처리와 트랜잭션 기록
- 다중 Modbus Slave Emulator
- GST 프레임 생성, 파싱, Peer 통신
- WinForms 카드 UI, 버튼, 색상, 폰트

현재 Fault Emulator에는 연속 정상 응답을 통합 관리하는 서비스가 없으므로 `OP_test`에서 별도의 `CommEmulationHost`를 구현한다.

### 5.2 MAIN 펌웨어

경로:

`C:\Users\LAB\Desktop\MSC_TEST\main\mxc-hif1\IF_Controller\IF_main`

확인 내용:

- MCU: STM32L452 계열
- 로터리 입력: PC0, PC1, PC2, PC3
- FAULT 출력: PE2
- FAULT debounce: 2000ms
- UART4: 115200, RS-485
- LPUART1: 38400, RS-485, DIO
- USART2: 19200, RS-485, Heating Jacket
- USART3: 9600, Even parity, RS-485, Gas Detector
- USART1: 115200, RS-422, GST

기본 내부 장치 ID:

- DIO: 1, 2
- Heating Jacket: 20, 21, 22
- Gas Detector: 30, 40, 50
- Backup GST: 1대

MAIN은 모든 활성 내부 장치와 UART4가 최근 2초 안에 정상 통신했는지 확인하여 FAULT 출력을 제어한다.

### 5.3 DIO 펌웨어

경로:

`C:\Users\LAB\Desktop\MSC_TEST\sub_real\mxc-hif1-dio\interface_sub`

확인 내용:

- UART2 RS-485
- 38400, 8-N-1
- Slave ID 1~4
- DO 16점
- DI 16점
- DO 명령: Holding Register 129
- DI 읽기: Input Register 130
- DI 보조 읽기: Holding Register 130
- bit 0 = 채널 1
- bit 15 = 채널 16
- FC03, FC04, FC06, FC16 지원

DIO 검사는 USB나 별도 테스트 펌웨어 없이 기존 RS-485 운용 통신으로 수행한다.

## 6. 새 프로젝트 구조

```text
OP_test/
├─ OperateTest.sln
├─ OperateTest.App/
│  ├─ Program.cs
│  ├─ Forms/
│  │  ├─ BoardSelectionForm.cs
│  │  ├─ CommInspectionForm.cs
│  │  ├─ DioInspectionForm.cs
│  │  └─ ConnectionSettingsForm.cs
│  ├─ Controls/
│  ├─ Theme/
│  └─ Services/
│     ├─ Serial/
│     ├─ Modbus/
│     ├─ ModbusSlave/
│     ├─ Gst/
│     ├─ CommInspection/
│     ├─ DioInspection/
│     └─ StLink/
├─ OperateTest.Tests/
└─ Docs/
   ├─ PROJECT_PLAN.md
   ├─ COMM_TEST_SPEC.md
   ├─ DIO_LOOPBACK_SPEC.md
   └─ HARDWARE_CONNECTION.md
```

프로젝트 기준:

- .NET 8
- Windows Forms
- `System.IO.Ports`
- 기존 Fault Emulator와 별도 실행 파일
- 기존 프로젝트에 대한 빌드 참조 없이 자체 코드로 구성

## 7. 초기 화면

프로그램을 실행하면 다음 두 카드만 표시한다.

- `COMM 보드 검사`
- `DIO 보드 검사`

부트로더, 펌웨어, 버전, CRC 관련 진입 항목은 표시하지 않는다.

## 8. COMM 보드 검사

COMM 화면은 다음 세 영역으로 구성한다.

1. 통신 검사
2. 로터리 스위치 검사
3. FAULT 검사

### 8.1 COMM 통신 검사

PC 역할:

| 경로 | PC 역할 | 기본 장치 |
|---|---|---|
| UART4 | PLC Modbus Master | COMM 보드 |
| LPUART1 | Modbus Slave Emulator | DIO ID 1, 2 |
| USART2 | Modbus Slave Emulator | Heating ID 20, 21, 22 |
| USART3 | Modbus Slave Emulator | Gas ID 30, 40, 50 |
| USART1 | GST Peer Emulator | Backup 1대 |

화면 구성:

- 첫 번째 참고 화면과 같은 단순 검사 항목 목록 사용
- 각 행에는 검사 대상, 실행 상태, 최종 판정만 표시
- 대상별 상태는 `미실행`, `검사 중`, `정상`, `실패`로 표시
- `전체 통신 검사` 버튼으로 모든 경로를 순서대로 검사
- 전체 로그, T/P/F/E 누적 통계, 상세 패킷 정보는 표시하지 않음

필요 서비스:

- `CommEmulationHost`
  - MAIN이 내부 통신 채널로 보내는 요청 수신
  - 정상 요청인지 확인한 뒤 응답 송신
- `CommInspectionService`
  - 대상별 정상 통신 1회 확인
  - 타임아웃 시 최대 3회 재시도
  - PASS/FAIL 결과 표시

검사 흐름:

1. 검사 대상을 선택한다.
2. 대상 포트를 연다.
3. 정상 요청과 정상 응답이 한 번 왕복하는지 확인한다.
4. 성공하면 PASS, 최대 3회 안에 응답하지 않으면 FAIL로 표시한다.

MAIN 내부 통신 채널은 PC 에뮬레이터가 Slave 역할로 정상 응답을 돌려주고, UART4 및 외부 Slave 검사는 PC가 요청을 보내 정상 응답을 확인한다.

화면에는 복잡한 통계 없이 대상별 `검사 중`, `정상`, `실패`만 표시한다.

판정 원칙:

- 대상과 유효한 요청·응답이 한 번 이상 왕복하면 PASS
- 응답 없음이나 잘못된 응답은 FAIL
- 응답시간, 성공률 및 반복 성능 통계는 표시하지 않음

### 8.2 로터리 스위치 검사

대상:

- PC0~PC3
- 위치 0~F

검사 방식:

1. ST-LINK 연결 확인
2. MCU를 리셋하거나 정지하지 않는 Hot Plug 방식으로 GPIO 입력 읽기
3. PC0~PC3을 4비트 값으로 조합
4. 현재 위치 0~F 표시
5. 사용자가 확인한 위치를 누적
6. 16개 위치 전부 관측하면 완료

필요 서비스:

- `StLinkConnectionService`
- `RotarySwitchMonitor`

설치된 STM32 Programmer CLI를 자동 탐색하고 SWD Hot Plug로 GPIOC_IDR(0x48000810)을 읽는다. PC0~PC3은 Pull-up 입력이므로 LOW 접점을 반전해 0~F로 표시한다.

### 8.3 FAULT 검사

별도 USB 테스트 명령이나 펌웨어 수정 없이 실제 통신 상태를 이용한다.

`OPEN (FAULT)` 흐름:

1. UART4 통신은 계속 유지
2. 고정 검사 경로인 DIO ID2의 정상 응답만 자동 중단
3. 마지막 정상 통신 이후 2초 이상 대기
4. MAIN의 FAULT 상태 전환 확인
5. 검사자가 TB18-1 `EXT_COM`과 TB18-2 `EXT_FLT`의 개방 확인
6. 프로그램에서 `개방 확인` 입력

`CLOSE (NORMAL)` 흐름:

1. 프로그램이 중단했던 고정 검사 경로의 응답을 자동 재개
2. 모든 활성 장치 정상 통신 확인
3. MAIN 복구 debounce 2초 이상 대기
4. 검사자가 TB18 도통 확인
5. 프로그램에서 `도통 확인` 입력

주의:

- FAULT 대상은 검사자가 선택하지 않으며 프로그램 내부의 고정 시나리오로 실행한다.
- FAULT 유도 중에도 UART4 요청을 계속 보내서 내부 통신 장애만 검사한다.
- FAULT 결과는 통신 상태만으로 자동 합격시키지 않는다.
- TB18 물리 접점 확인까지 완료해야 합격이다.

### 8.4 COMM 최종 합격 조건

- UART4 통신 PASS
- DIO 내부 통신 PASS
- Heating 내부 통신 PASS
- Gas 내부 통신 PASS
- Backup GST 통신 PASS
- 로터리 0~F 전 위치 확인
- FAULT OPEN 물리 확인
- FAULT CLOSE 물리 확인

## 9. DIO 보드 검사

### 9.1 연결

- RS-485 COM 포트 선택
- 38400, 8-N-1 고정
- 루프백 검사 시작 시 통신 가능한 Slave ID 1~4 자동 탐색
- FC04, Input Register 130, Quantity 1 응답으로 통신 ID 확인
- ID DIP 화면 표시는 통신 주소가 아니라 ST-LINK GPIO 직접 읽기 사용

### 9.2 ID DIP 스위치 실시간 표시

사용자가 DIO의 `ID_SET1`, `ID_SET2` DIP 스위치를 물리적으로 조작하면 화면 중앙의 큰 ID 표시가 현재 설정에 맞춰 `ID 1`부터 `ID 4`까지 실시간으로 바뀌게 한다.

프로그램은 ST-LINK Hot Plug 방식으로 PA0와 PC15의 GPIO 입력을 직접 읽는다. 사용자는 통신 주소나 프레임을 볼 필요 없이 화면에 표시된 ID가 실제 스위치 조작과 일치하는지만 눈으로 확인한다.

현재 코드 기준 ID 조합은 다음과 같다.

| ID_SET1 | ID_SET2 | 화면 표시 |
|---|---|---|
| LOW | LOW | ID 1 |
| HIGH | LOW | ID 2 |
| LOW | HIGH | ID 3 |
| HIGH | HIGH | ID 4 |

화면 동작:

- 감지 성공: 큰 숫자로 현재 ID 표시
- DIP 변경 중: `ID 확인 중...` 표시
- ST-LINK 응답 없음: `ST-LINK 연결을 확인하세요` 표시

DIO 운용 펌웨어의 통신 Slave ID는 시작 시 확정되지만, 검사 화면은 ST-LINK로 PA0/PC15 GPIO를 직접 읽으므로 DIP 조작 상태를 재부팅 없이 표시한다. 실제 DIP의 ON 표기가 전기적으로 HIGH인지 여부는 실물 보드에서 한 번 확인한 뒤 화면 그림과 안내 문구에 반영한다.

### 9.3 루프백 지그

기본 연결:

- DO1 → DI1
- DO2 → DI2
- DO3 → DI3
- DO4 → DI4
- DO5 → DI5
- DO6 → DI6
- DO7 → DI7
- DO8 → DI8
- DO9 → DI9
- DO10 → DI10
- DO11 → DI11
- DO12 → DI12
- DO13 → DI13
- DO14 → DI14
- DO15 → DI15
- DO16 → DI16

실제 지그에는 회로에 맞는 +24V, 0V, `EXT_COM1~16` 전류 경로가 포함되어야 한다. 신호 단자끼리 연결하기 전에 커넥터 핀맵과 출력 회로 형식을 확정한다.

### 9.4 자동 검사 순서

1. DO Register 129에 `0x0000` 기록
2. DI Register 130을 반복해서 읽어 안정된 `baseline` 저장
3. DO1만 ON
4. 안정화 시간 대기
5. DI를 반복해서 읽어 연속 동일값 확인
6. `current_di XOR baseline_di` 계산
7. 변화 비트가 DI1 하나인지 판정
8. DO 전체 OFF
9. DI가 baseline으로 복귀하는지 확인
10. DO2~DO16 반복
11. DO 전체 ON `0xFFFF` 검사
12. DO 전체 OFF `0x0000` 복귀

판정식:

```text
changed_bits = current_di XOR baseline_di
expected_bit = 1 << channel_index
PASS if changed_bits == expected_bit
```

이 방식은 입력 논리가 Active High 또는 Active Low여도 채널 변화 여부를 확인할 수 있다.

### 9.5 실패 분류

| 결과 | 표시 내용 |
|---|---|
| DI 변화 없음 | DO/DI 불량, 전원·공통선 누락, 지그 단선 가능 |
| 다른 DI 한 점 변화 | 채널 매핑 또는 지그 오배선 |
| 여러 DI 동시 변화 | 단락 또는 지그 배선 오류 |
| OFF 후 baseline 미복귀 | 출력 또는 입력 고착 |
| 응답 없음 | 전원, RS-485, Slave ID, 펌웨어 확인 |
| 프로토콜 오류 | 통신 설정, 노이즈, 프레임 불일치 확인 |

### 9.6 DIO 화면 표시

- 연결 상태
- DIP 조작에 따라 실시간으로 바뀌는 현재 ID 1~4
- 현재 검사 채널 `DO n → DI n`
- DO 명령값
- baseline DI
- 현재 DI
- 예상 변화 비트
- 실제 변화 비트
- 채널별 PASS/FAIL
- 전체 진행률
- 전체 OFF 버튼
- 최종 PASS/FAIL

walking-one 검사 중 DO LED는 검사자가 물리적으로 확인한다. LED 확인은 자동 DI 판정과 분리한다.

### 9.7 DIO 출력 안전 처리

다음 상황에서 DO Register 129에 `0x0000` 기록을 시도한다.

- 검사 시작 전
- 각 채널 검사 사이
- 검사 정상 종료
- 검사 취소
- 통신 오류
- 예외 발생
- DIO 화면 종료
- 프로그램 종료

통신이 끊겨 OFF 명령을 전송하지 못하면 전원 또는 출력을 수동 확인하라는 안내를 표시한다.

## 10. 구현 단계

### 1단계: 프로젝트 골격 및 통신 기반

- `OperateTest.sln`과 WinForms 프로젝트 생성
- 기존 Controls/Theme 선별 이관
- Serial/Modbus/ModbusSlave/GST 서비스 선별 이관
- 검사 전용 `AppServices` 구성
- 초기 COMM/DIO 선택 화면
- 연결 설정 화면
- COM 포트 검색·연결·해제
- 기존 세 프로젝트가 수정되지 않았는지 확인

1단계 완료 기준:

- `OP_test` 단독 빌드 성공
- 초기 화면에서 COMM/DIO 화면 진입
- 각 화면에서 필요한 COM 포트 선택 가능
- 연결 실패와 실제 응답 없음이 구분됨

### 2단계: 핵심 자동 검사

DIO:

- ST-LINK PA0/PC15 직접 읽기 및 ID DIP 실시간 표시
- DO 129 기록
- DI 130 읽기
- baseline 안정화
- 16채널 walking-one
- 전체 ON/OFF
- 실패 채널 및 실제 변화 채널 표시
- 모든 종료 경로 전체 OFF

COMM:

- 첫 번째 참고 화면 형태의 단순 통신 검사 목록 구현
- `CommEmulationHost` 구현
- DIO/HJ/Gas/GST 정상 응답 worker 동시 실행
- UART4 probe와 그룹 Read
- 내부 요청·응답 관측
- 5개 통신 경로 개별 판정
- 전체 통신 결과 표시

2단계 완료 기준:

- DIO 루프백 자동 검사 완료
- COMM 5개 통신 경로 PASS/FAIL 판정
- 응답 없는 장치를 정상으로 표시하지 않음

### 3단계: 로터리·FAULT·실물 통합

- ST-LINK 연결 및 Hot Plug GPIO 읽기
- 로터리 0~F 누적 검사
- 고정된 FAULT 검사 경로의 응답 자동 중단·재개
- FAULT debounce 진행 표시
- TB18 OPEN/CLOSE 수동 확인
- COMM/DIO 최종 결과 통합
- 실물 지그 검증
- 프로그램 종료 및 오류 상황 검증

3단계 완료 기준:

- 로터리 16위치 확인
- 실제 통신 장애에 따른 FAULT OPEN 확인
- 정상 통신 복구에 따른 FAULT CLOSE 확인
- COMM/DIO 전체 실물 검사 완료

## 11. 구현 전 하드웨어 확정 항목

COMM:

- UART4용 RS-485 변환기
- LPUART1용 RS-485 변환기
- USART2용 RS-485 변환기
- USART3용 RS-485 변환기
- USART1용 RS-422 변환기
- ST-LINK/SWD 연결
- TB18 접점 측정 방법

DIO:

- UART2용 RS-485 변환기
- DO1~16과 DI1~16 커넥터 핀맵
- +24V 및 0V 배선
- `EXT_COM1~16` 연결 방식
- 보드 정격 전원
- 오배선 방지 루프백 하네스

## 12. 완료 판정

### COMM

- 5개 통신 경로가 독립적으로 판정된다.
- 실제 요청과 정상 응답이 모두 확인된다.
- 로터리 0~F가 ST-LINK로 확인된다.
- 통신 장애로 FAULT OPEN이 발생한다.
- 정상 복구로 FAULT CLOSE가 발생한다.
- TB18 물리 접점 확인이 최종 결과에 반영된다.

### DIO

- DIP 스위치 네 조합에서 화면의 ID 1~4 표시가 실제 조작과 일치한다.
- PC가 DO1~16을 순차적으로 제어한다.
- 각 DO에 대응하는 DI 한 점만 변하는지 확인한다.
- 다른 DI가 변하면 예상/실제 채널을 표시한다.
- 전체 ON/OFF가 수행된다.
- 실패·취소·종료 시 전체 OFF를 시도한다.
- LED 순차 점등을 검사자가 확인할 수 있다.

## 13. 다음 작업 시작 지시문

다음 구현 요청은 아래 문장으로 시작할 수 있다.

> OP_test/PROJECT_PLAN.md와 STAGE3_REPORT.md를 기준으로 실물 보드 검증을 진행해줘. MAIN/SUB/Fault Emulator는 수정하지 말고 OP_test 설정만 보정해줘.

## 14. 현재 상태

### 1단계 완료 — 2026-10-01

- `OperateTest.sln`과 독립 WinForms 프로젝트 생성 완료
- 초기 COMM/DIO 선택 화면 구현 완료
- Serial/Modbus/ModbusSlave/GST 통신 계층 선별 이관 완료
- COMM 5개 경로와 DIO COM 포트의 비동기 검색·연결·해제 구현 완료
- 동일 COM 포트 중복 점유 방지 구현 완료
- 후속 검사 화면 골격 구성 완료
- 로그·리포트·Fault 대상 선택 메뉴 제외 확인
- Debug 빌드 경고 0개, 오류 0개
- 실행 파일 시작 확인 완료
- MAIN/SUB/Fault Emulator 원본 변경 없음

### 2단계 완료 — 2026-10-01

- COMM 5개 물리 경로별 요청·응답 판정 구현 완료
- DIO ST-LINK PA0/PC15 ID 1~4 실시간 표시 구현 완료
- DIO HR129/IR130 기반 16채널 루프백 구현 완료
- 전체 ON/OFF와 모든 종료 경로 OFF 처리 구현 완료
- Debug 빌드 경고 0개, 오류 0개
- GUI 실행 확인 완료
- 실물 보드 검증은 장비 연결 후 필요

### 3단계 완료 — 2026-10-01

- ST-LINK GPIOC PC0~PC3 로터리 0~F 실시간 표시 완료
- 로터리 16개 위치 누적 확인 완료
- 전체 정상 통신 유지용 CommEmulationHost 구현 완료
- 고정 DIO ID2 응답 차단·복구 구현 완료
- FAULT OPEN / NORMAL CLOSE와 TB18 검사자 확인 UI 완료
- FAULT 상태에서 종료 시 정상 응답 복구 처리 완료
- Debug 빌드 경고 0개, 오류 0개
- 실물 ST-LINK·보드·지그 검증 필요

3단계 구현을 완료했으며 다음 작업은 실물 보드·지그 통합 검증이다.



## 통신 구조 최종 정정 — 2026-10-01

실제 MAIN 펌웨어와 보드 커넥터 구성을 기준으로 다음과 같이 적용한다.

- PC 직접 연결 포트는 네 개다.
  - MAIN UART4: 115200, 8-N-1
  - Heating Jacket: 19200, 8-N-1
  - Gas Detector: 9600, 8-E-1
  - Backup GST: 115200, 8-N-1
- DIO는 별도 PC COM 포트가 없다.
- DIO 통신 상태는 MAIN UART4에서 Slave ID 4, HR163 bit0으로 판정한다.
- Heating, Gas, Backup은 각 COM 포트에서 MAIN의 실제 요청을 받아 정상 응답을 보내 검사한다.
- 통신 검사 화면은 MAIN, DIO, Heating, Gas, Backup 다섯 경로를 표시한다.
- FAULT 검사는 전체 정상 통신을 유지한 뒤 Heating ID20 응답 하나만 차단한다.
- MAIN의 2초 통신 FAULT 판정 후 TB18 개방을 검사자가 확인한다.
- CLOSE 단계에서는 Heating ID20 응답을 복구하고 TB18 도통을 확인한다.
- DIO 루프백은 공유 MAIN UART4 PLC 그룹 맵을 사용한다.
  - DI 읽기: Slave ID 1, HR1~2
  - DO 쓰기: Slave ID 5, HR201~202, FC10
- 원본 MAIN/DIO 펌웨어와 Fault Emulator는 수정하지 않는다.