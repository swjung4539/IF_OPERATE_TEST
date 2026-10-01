# 3단계 완료 보고

작성일: 2026-10-01

## 구현 완료

### COMM 로터리 스위치

- STM32 Programmer CLI와 ST-LINK SWD Hot Plug 방식 사용
- MAIN STM32L452의 `GPIOC_IDR 0x48000810` 읽기
- 대상 핀: PC0, PC1, PC2, PC3
- MAIN GPIO 설정이 Pull-up이고 로터리 접점이 LOW로 들어오므로 `position = (~GPIOC_IDR) & 0x0F`로 위치 계산
- 현재 위치를 `0~F`로 크게 표시
- PC3~PC0 원시 HIGH 상태 표시
- 관측한 위치를 16개 타일에 누적 표시
- 16개 위치를 모두 관측하면 완료 표시
- 확인 이력 초기화 기능 추가
- MAIN을 리셋하거나 정지하지 않고 읽기만 수행

### COMM FAULT 접점 검사

고정 검사 대상은 DIO ID2입니다. 검사자가 Fault 대상을 선택하는 메뉴는 만들지 않았습니다.

정상 통신 준비:

- UART4에서 MAIN HR131을 주기적으로 읽어 PLC 통신 유지
- DIO ID1, ID2 정상 응답
- Heating ID20, ID21, ID22 정상 응답
- Gas ID30, ID40, ID50 정상 응답
- Backup GST 정상 응답
- 위 10개 대상이 모두 응답하고 2초 이상 정상 상태가 유지돼야 준비 완료

`OPEN (FAULT)`:

- DIO ID2 요청만 무응답 처리
- DIO ID1과 Heating, Gas, GST, UART4는 계속 정상 통신
- DIO ID2 요청이 실제로 관측된 뒤 MAIN debounce 2초 이상 대기
- 검사자에게 TB18-1 `EXT_COM` / TB18-2 `EXT_FLT` 개방 측정 안내
- 검사자가 `개방 확인` 버튼으로 결과 입력

`CLOSE (NORMAL)`:

- DIO ID2 정상 응답 재개
- ID2 응답 복구를 실제로 확인
- 전체 정상 통신을 2초 이상 유지
- 검사자에게 TB18 도통 측정 안내
- 검사자가 `도통 확인` 버튼으로 결과 입력

안전 처리:

- FAULT 검사 종료 시 DIO ID2 응답을 정상으로 복구
- FAULT 상태에서 COMM 화면을 닫아도 정상 응답 복구를 먼저 시도
- 복구 후 통신 worker와 COM 포트 종료
- 검사 도중 UART4와 나머지 활성 Slave 통신 유지
- 전체 로그, Fault 대상 선택, 장시간 통계 화면 없음

## 구현 파일

- `Services/StLink/StLinkDioIdSwitchReader.cs`: 공통 ST-LINK 메모리, DIO ID, COMM 로터리 판독
- `Services/CommInspection/CommEmulationHost.cs`: 전체 정상 통신 유지와 DIO ID2 차단·복구
- `Services/ModbusSlave/ModbusSlaveEmulatorService.cs`: 지정 Slave ID 무응답 처리
- `Forms/CommInspectionForm.cs`: 로터리 및 5단계 FAULT 검사 UI

## 검증 결과

- `dotnet build OperateTest.sln --configuration Debug`
- 결과: 경고 0개, 오류 0개
- 기존 Fault Emulator Git 작업 트리 변경 없음
- 기존 DIO Git 작업 트리 변경 없음
- MAIN·DIO 펌웨어 파일 수정 없음
- MAIN 원본에서 GPIOC PC0~PC3 Pull-up, FAULT PE2, 기본 DIO ID1·2, debounce 2000ms 재확인
- 정상일 때 NO relay 여자, fault·전원 차단 시 접점 개방 구조 확인

## 실물 검증 필요

현재 ST-LINK와 COMM 보드가 연결되지 않아 다음 항목은 실물에서 확인해야 합니다.

- 로터리 위치 0~F와 화면 값 일치
- 로터리 bit 순서와 Active Low 최종 확인
- 정상 통신 준비 완료
- DIO ID2 차단 후 약 2초 뒤 TB18 개방
- DIO ID2 복구 후 약 2초 뒤 TB18 도통
- 각 RS-485/RS-422 변환기 방향과 배선
- 프로그램 종료 중 정상 복구

실물 검증에서 로터리 방향이나 접점 논리가 다르면 OP_test의 변환식과 안내 문구만 보정하며 MAIN 펌웨어는 수정하지 않습니다.
