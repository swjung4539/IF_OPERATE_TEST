# 7단계 기록 - DIO USB CDC 및 자동 실행 기반

완료일: 2026-09-17.

## 완료한 작업

- DIO `.ioc`에 USB CDC 설정 생성
- `PA11=USB_DM`, `PA12=USB_DP`
- STM32G0 USB Device FS와 CDC 생성 코드 추가
- USB 48MHz/HSI48/CRS 초기화 코드 생성
- G0 PCD 및 USB Device Library 포함
- DIO 운용 펌웨어에 읽기 전용 USB 명령 추가
- PC DIO 프로토콜과 응답 형식 연결
- USB watcher가 `IDENTIFY` 응답을 기다리지 않고 새 CDC 포트 연결 시 초기 화면 표시

## DIO USB 명령

- `IDENTIFY` -> `DEVICE=DIO`
- `GET_SWITCH` -> `SW1=0;SW2=1`
- `GET_FIRMWARE_INFO` -> `DEVICE=DIO;VERSION=1.0.0;SIZE=131072;CRC=XXXXXXXX`

## 운용 코드 영향 범위

기존 Modbus, UART2, DI/DO, 출력 제어 로직은 수정하지 않았다. USB 수신 링버퍼와 정보 조회 함수만 추가했다. 원본 `sub` 프로젝트와 `TestMode/Firmware/DIO` 복사본 양쪽에 동일한 USB 생성 및 명령 모듈이 존재한다.

## 빌드 및 UI 검증

- 원본 DIO USB 펌웨어 빌드 성공
- 원본 결과: `interface_sub.elf`, text 68020 bytes, data 408 bytes, bss 26520 bytes
- TestMode DIO 복사본 CubeIDE headless 빌드 성공
- PC Debug 빌드: 경고 0, 오류 0
- Stage 8 UI 회귀 검증: 통과
- 실제 USB 장치 인식과 물리 스위치 검증은 아직 진행하지 않음

## 다음 작업

실물 MAIN/DIO를 연결해 USB 인식, CDC 응답, ID 4개 조합, 펌웨어 CRC, 기존 Modbus/DI/DO 영향을 확인한다. 이후 배포용 패키지와 Windows 로그인 자동 실행 등록을 정리한다.
