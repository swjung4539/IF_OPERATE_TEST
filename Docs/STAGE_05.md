# 5단계 진행 기록 - DIO ID 조회 준비

진행일: 2026-09-17.

## 완료한 단위

- DIO 복사본에 회로도 기준 ID 읽기 함수 추가
- `ID_SET1=PA0`를 상위 비트, `ID_SET2=PC15`를 하위 비트로 처리
- ID 매핑:
  - LOW/LOW = ID1
  - LOW/HIGH = ID2
  - HIGH/LOW = ID3
  - HIGH/HIGH = ID4
- 기존 Modbus Slave의 `SlaveIDCheck()`도 같은 함수 사용
- PC 앱이 `DEVICE=DIO` 식별과 `GET_ID` 응답을 처리하도록 준비
- DIO 화면에서 ID와 두 DIP 상태를 USB 수신값으로 표시할 수 있도록 연결

## 추가 파일

- `Firmware/DIO/User/Application/a_test_id.h`
- `Firmware/DIO/User/Application/a_test_id.c`
- `PC/TestMode.App/UsbTestClient.cs`
- `PC/TestMode.App/MainForm.cs`
- `PC/TestMode.App/SwitchView.cs`

## 아직 남은 작업

- STM32G0B1용 USB Device FS 설정
- USB CDC 소스 및 디스크립터 생성
- DIO USB 부트로더 프로젝트와 플래시 영역 분리
- DIO USB TEST 명령과 PC ID 조회 연결
- 최초 부트로더 ST-LINK 설치 절차

현재 DIO `.ioc`에는 USB IP와 `USB_DEVICE`가 없고, 워크스페이스에도 STM32G0용 CDC 생성 코드가 없으므로 USB 부트로더는 생성 기반을 확보한 뒤 진행해야 합니다. MAIN 부트로더를 MCU가 다른 DIO에 그대로 복사하지 않았습니다.

## 검증 한계

- ARM 컴파일러가 현재 환경에 없어 DIO 펌웨어 빌드는 수행하지 않음
- 실물 DIP 스위치와 ID 값은 아직 확인하지 않음
- USB 조회는 아직 연결하지 않음

## 검증 추가

- PC Debug 빌드: 경고 0, 오류 0
- Stage 5 UI 회귀 검증: 통과
- 실제 장치 접근: 없음
