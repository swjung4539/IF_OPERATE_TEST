# 1단계 완료 보고

작성일: 2026-10-01

## 완료 범위

- `OperateTest.sln` 독립 Windows Forms 프로젝트 유지
- 초기 `COMM 보드 검사` / `DIO 보드 검사` 선택 화면 유지
- 기존 Fault Emulator에서 다음 통신 계층만 OP_test로 선별 이관
  - Serial 비동기 송수신, 연결 해제, 포트 이탈 감지
  - Modbus RTU Master 및 CRC/응답 검증
  - 다중 Modbus Slave Emulator
  - GST Peer 및 프레임 Codec
- 검사 화면의 COM 연결 UI를 새 비동기 Serial 계층에 연결
- 통신 취소 토큰, 응답 시간 초과, 케이블 분리 및 포트 오류 처리 기반 구성
- 채널별 프로토콜 서비스 공급자 `InspectionProtocolServices` 추가
- COMM 5개 채널과 DIO 1개 채널을 서로 독립된 Serial transport로 구성
- 동일 COM 포트의 중복 점유 차단
- 화면 종료 및 프로그램 종료 시 연결 해제 처리

## 채널 구성

| 검사 | 채널 | 기본 설정 |
|---|---|---|
| COMM | UART4 메인 통신 | RS-485, 115200, 8-N-1 |
| COMM | DIO Slave 통신 | RS-485, 38400, 8-N-1 |
| COMM | Heating Jacket 통신 | RS-485, 19200, 8-N-1 |
| COMM | Gas Detector 통신 | RS-485, 9600, 8-E-1 |
| COMM | Backup GST 통신 | RS-422, 115200, 8-N-1 |
| DIO | DIO 보드 통신 | RS-485, 38400, 8-N-1 |

## 검증 결과

- `dotnet build OperateTest.sln --configuration Debug`
- 결과: 경고 0개, 오류 0개
- 생성 파일: `OperateTest.App/bin/Debug/net8.0-windows/OperateTest.exe`
- 실행 스모크 테스트: 시작 후 프로세스 정상 유지 확인
- 기존 Fault Emulator Git 작업 트리 변경 없음
- 기존 DIO Git 작업 트리 변경 없음
- MAIN 펌웨어 파일 수정 없음

실제 COM 장치 송수신은 보드와 변환기가 필요한 실물 검사이므로 아직 수행하지 않았습니다.

## 원본 보호

이번 단계에서 수정한 범위는 `C:\Users\LAB\Desktop\MSC_TEST\OP_test`뿐입니다. MAIN, DIO 운용 펌웨어와 기존 Fault Emulator 소스는 복사 원본으로만 사용했고 수정하지 않았습니다.

## 다음 단계

2단계에서는 현재 통신 기반 위에 다음 검사 로직을 연결합니다.

- COMM 5개 경로의 간단한 통신 성공/실패 판정
- DIO DO1~16 명령과 DI1~16 루프백 자동 판정
- 오류, 취소, 화면 종료 시 DIO DO 전체 OFF 처리
- DIO ID DIP는 ST-LINK에서 PA0/PC15를 직접 읽어 화면에 ID 1~4로 표시
