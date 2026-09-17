# 4단계 기록 - MAIN HEX 펌웨어 업데이트

완료일: 2026-09-17.

## 구현 범위

- Intel HEX 레코드 파싱 및 체크섬 검증
- MAIN Slot A/B 주소 범위 검증
- 부트로더 USB CDC PING 탐색
- 필요 시 앱의 ENTER_UPDATE 진입 후 부트로더 재탐색
- 활성 슬롯 확인 후 반대 슬롯에 기록
- ERASE_SLOT, WRITE_CHUNK, FINALIZE CRC 검증, REBOOT 수행
- READ_FLASH로 현재 활성 슬롯과 선택 HEX가 같으면 기록 생략
- 동일한 경우 `선택한 펌웨어와 동일합니다.` 표시
- 기록 및 검증 성공 시 `펌웨어 업데이트 성공` 표시
- DIO 업데이트는 아직 비활성화

## 파일

- `PC/TestMode.App/BootProtocol.cs`
- `PC/TestMode.App/IntelHex.cs`
- `PC/TestMode.App/MainFirmwareUpdateService.cs`
- `PC/TestMode.App/FirmwareView.cs`
- `PC/TestMode.App/MainForm.cs`

## 안전 처리

- 부트로더 영역 또는 슬롯 밖 주소가 포함된 HEX 거부
- Intel HEX 체크섬 오류 거부
- 슬롯 크기 224KB 초과 거부
- WRITE_CHUNK는 기존 부트로더 USB 패킷 제약에 맞춰 48바이트 사용
- 부트로더 응답 타임아웃은 성공으로 처리하지 않음
- REBOOT 응답은 보드가 재부팅하며 끊길 수 있으므로 결과를 강제하지 않음

## 확인하지 못한 부분

- 실제 MAIN 보드 연결과 물리 펌웨어 기록
- 현재 배포 HEX의 실제 주소 구성
- 실제 A/B 슬롯 전환 및 재부팅
- 현행 배포 규칙의 버전 값을 HEX에서 추출하는 방법
- FINALIZE에 전달하는 버전은 현재 `0.0.0` 기본값임. 배포 버전 규칙 확정 후 별도 반영해야 함.

## 검증

- PC Debug 빌드: 경고 0, 오류 0
- Stage 4 UI 회귀 검증: 통과
- UI 검증은 실제 장치에 접근하지 않음
- ARM 펌웨어 바이너리 빌드는 ARM 컴파일러 부재로 수행하지 않음

## 다음 단계

5단계 DIO USB 부트로더 및 ID 조회.
