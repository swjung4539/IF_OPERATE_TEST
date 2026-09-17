# 3단계 기록 - MAIN 로터리 및 FAULT 제어

완료일: 2026-09-17.

## 구현 범위

- MAIN 애플리케이션 복사본에 TEST USB 링크 추가
- 기존 부트로더 진입용 바이너리 경로는 유지
- TEST 기능은 내부 USB CDC 명령으로 처리
- PC 화면에 COM 포트나 통신 프레임을 노출하지 않음
- MAIN 로터리 입력 조회
- `OPEN (FAULT)` / `CLOSE (NORMAL)` 제어
- FAULT 도통 여부는 자동 판정하지 않음

## 내부 TEST 명령

- `IDENTIFY` -> `DEVICE=MAIN`
- `GET_ROTARY` -> `ROTARY=0F` 형식
- `FAULT OPEN` -> `FAULT=OPEN`
- `FAULT CLOSE` -> `FAULT=CLOSE`

이는 사용자에게 제공하는 통신 검사 기능이 아니라 USB 연결을 위한 내부 제어입니다.

## MAIN 동작

- 로터리 입력: PC0~PC3, `CAN_ID4~CAN_ID7`
- 로터리 화면 갱신 목표: 100ms
- FAULT 출력: PE2
- `OPEN (FAULT)`: PE2 Low
- `CLOSE (NORMAL)`: PE2 High
- TEST FAULT 명령은 1.5초 동안 운용 자동 FAULT 출력을 임시로 덮어씀
- PC가 계속 상태 명령을 보내는 동안 수동 상태 유지
- 명령이 끊기면 운용 펌웨어의 자동 FAULT 제어로 복귀

## 변경 파일

- `PC/TestMode.App/UsbTestClient.cs`
- `PC/TestMode.App/MainForm.cs`
- `PC/TestMode.App/SwitchView.cs`
- `PC/TestMode.App/FaultView.cs`
- `PC/TestMode.App/UiCheck.cs`
- `PC/TestMode.App/TestMode.App.csproj`
- `Firmware/Main/User/Application/Inc/a_test_link.h`
- `Firmware/Main/User/Application/Src/a_test_link.c`
- `Firmware/Main/User/Application/Src/a_application.c`
- `Firmware/Main/Core/Src/main.c`
- `Firmware/Main/USB_DEVICE/App/usbd_cdc_if.c`
- `Firmware/Main/CMakeLists.txt`

## 검증

- PC Debug 빌드: 경고 0, 오류 0
- Stage 3 UI 검증: 통과
- UI 검증은 실제 USB 장치에 접근하지 않음
- ARM 컴파일러가 현재 환경에 없어 MAIN 펌웨어 바이너리 빌드는 수행하지 못함
- 실물 보드의 USB 인식, 로터리 값, 릴레이 방향, TB18 도통은 아직 검증하지 않음

## 다음 단계

4단계 MAIN 펌웨어 업데이트와 HEX 비교·기록 검증 기능.
