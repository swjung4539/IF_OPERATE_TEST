# 2단계 완료 보고

작성일: 2026-10-01

## 구현 완료

### COMM 통신 검사

- COMM 화면에 5개 경로 검사 목록과 전체 검사·취소 기능 추가
- 각 경로를 `미실행`, `검사 중`, `정상`, `실패`로 표시
- UART4
  - PC가 Modbus Master로 MAIN의 Global HR131을 읽음
  - 정상 응답과 CRC·주소·Function 검증을 통과해야 정상 판정
- LPUART1 DIO
  - PC가 ID 1, 2의 다중 Modbus Slave로 동작
  - MAIN 요청을 받고 정상 응답을 전송한 경우 정상 판정
- USART2 Heating
  - PC가 ID 20, 21, 22의 다중 Modbus Slave로 동작
  - 실제 Heating 폴링 주소에 정상 응답
- USART3 Gas
  - ID 30, 40은 STD, ID 50은 TFT 프로파일로 구성
  - MAIN 원본의 현재 wire address와 FC01/02/03/04 폴링 범위를 반영
  - 등록된 어느 활성 ID든 정상 요청·응답이 완료되면 경로 정상 판정
- USART1 Backup GST
  - MAIN GST 프레임을 수신·검증하고 41개 상태 레지스터 응답 전송
- 응답 없음은 최대 3회 확인 후 실패 처리
- COM 포트가 연결되지 않았거나 실제 요청·응답이 없으면 정상으로 표시하지 않음

### DIO ID DIP 표시

- 설치된 STM32 Programmer CLI 자동 검색
- ST-LINK SWD Hot Plug 방식 사용
- STM32G0B1 GPIO IDR 직접 읽기
  - `ID_SET1 = PA0`: `GPIOA_IDR 0x50000010` bit 0
  - `ID_SET2 = PC15`: `GPIOC_IDR 0x50000810` bit 15
- 화면에 ID 1~4와 각 핀의 HIGH/LOW 상태 표시
- ID 계산
  - LOW / LOW = ID 1
  - HIGH / LOW = ID 2
  - LOW / HIGH = ID 3
  - HIGH / HIGH = ID 4
- DIO 운용 펌웨어의 통신 ID와 별개로 물리 DIP 상태를 실시간 표시

### DIO DO/DI 루프백

- RS-485에서 현재 통신 가능한 Slave ID 1~4 자동 탐색
- DO 명령: Holding Register 129, FC06
- DI 확인: Input Register 130, FC04
- DO 전체 OFF 후 안정된 DI baseline 저장
- DO1~16 walking-one 검사
- 각 채널에서 `현재 DI XOR baseline`이 예상 비트 하나와 같은지 판정
- 채널마다 OFF 후 baseline 복귀 확인
- 전체 ON `0xFFFF` 및 전체 OFF `0x0000` 확인
- 채널별 정상/실패, baseline, 현재 DI, 예상·실제 변화 비트 표시
- 검사 취소, 통신 오류, 예외, 화면 종료 시 DO 전체 OFF 시도
- 별도 전체 OFF 버튼 제공
- LED 점등은 검사자가 직접 확인하도록 안내

## 검증 결과

- `dotnet build OperateTest.sln --configuration Debug`
- 결과: 경고 0개, 오류 0개
- GUI 실행 스모크 테스트 정상
- STM32 Programmer CLI 명령 형식 확인
- 현재 PC 결과: `No debug probe detected`
- 기존 Fault Emulator Git 작업 트리 변경 없음
- 기존 DIO Git 작업 트리 변경 없음
- MAIN·DIO 펌웨어 파일 수정 없음

## 실물 검증 필요

현재 ST-LINK 디버그 프로브와 실제 COMM/DIO 보드가 연결되지 않아 다음 항목은 코드와 명령 구성까지만 검증했습니다.

- ST-LINK에서 실제 DIP 4개 조합 읽기
- COMM 5개 물리 포트 요청·응답
- DIO 16채널 루프백 지그
- 출력 LED 육안 확인
- 실제 Active High/Low와 지그 전원·공통선 배선

## 다음 단계

3단계에서는 COMM 로터리 스위치 0~F 표시, 실제 통신 장애를 이용한 FAULT OPEN/CLOSE 검사, TB18 검사자 확인 및 실물 통합 검증을 진행합니다.
