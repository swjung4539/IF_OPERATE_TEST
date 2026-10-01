# UI 재구성 결과

Fault Emulator의 화면 구조와 색상 체계를 기준으로 OP_test UI를 재구성했습니다.

## 공통 구조

- 초기 화면: COMM 보드 검사 / DIO 보드 검사 선택 카드
- 보드 화면: 좌측 메뉴와 우측 작업 화면
- 메뉴:
  1. 대시보드
  2. 통신 설정
  3. 통신 검사
  4. FAULT 및 스위치 검사
- 흰색 카드, 옅은 회색 배경, 파란 선택 메뉴, 상태 색상 체계를 적용
- 펌웨어 업데이트, 로그·리포트 메뉴는 포함하지 않음

## COMM

- 대시보드: 포트 연결 수, 검사 경로 수, 로터리·FAULT 검사 구성 표시
- 통신 설정: UART4, DIO, Heating, Gas, Backup 포트 설정
- 통신 검사: 5개 경로의 요청·응답 결과만 간단히 표시
- FAULT 및 스위치 검사:
  - 로터리 스위치 검사 열기
  - FAULT 접점 검사 열기
- 로터리와 FAULT는 각각 독립 창에서 실행

## DIO

- 대시보드: RS-485, 16채널 루프백, ID 1~4 검사 구성 표시
- 통신 설정: DIO RS-485 포트 설정
- 통신 검사: DO1~16 출력과 DI1~16 루프백 결과 표시
- FAULT 및 스위치 검사:
  - ID DIP 스위치 검사 열기
- ID DIP는 독립 창에서 ST-LINK로 실시간 표시
- DIO에는 FAULT 제어 항목을 만들지 않음

## 검증

- Debug 빌드: 경고 0, 오류 0
- 실행 스모크 테스트: 정상 시작 확인
- 기존 통신, ST-LINK, FAULT, DIO 루프백 서비스 로직 유지
- MAIN/DIO 원본 펌웨어와 기존 Fault Emulator는 수정하지 않음


## Fault Emulator 공통 디자인 적용

- Fault Emulator의 디자인 토큰과 벡터 아이콘을 직접 사용
- AppPanel, AppCard, AppButton, AppToggleSwitch, AppComboBox, StatusBadge, SideMenuButton, AppTitleBar 공통 컨트롤 적용
- COMM 통신 검사:
  - 경로별 ON/OFF
  - 반복 횟수
  - 계속 반복
  - START / STOP / RESET
  - TOTAL / PASS / FAIL / ERROR 누적 카드
  - 경로별 T / P / F / E와 마지막 판정
  - 최근 검사 결과
- DIO 루프백 검사:
  - 검사 ON/OFF
  - 반복 횟수 및 계속 반복
  - START / STOP / RESET
  - 16채널 실시간 판정
- 로터리, FAULT, DIO ID 전용 창도 동일한 상태 카드와 검사 패널 디자인 적용
