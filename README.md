# OperateTest

COMM/DIO 보드 수입검사용 독립 Windows Forms 프로그램입니다.

## COMM 보드 검사

- UART4 MAIN Modbus 응답 검사
- USART2 Heating ID20·21·22 응답 검사
- USART3 Gas ID30·40·50 STD/TFT 응답 검사
- USART1 Backup GST 프레임 왕복 검사
- ST-LINK로 로터리 PC0~PC3 위치 0~F 실시간 표시
- 0~F 전 위치 관측 이력
- Heating ID20 응답 차단을 이용한 FAULT OPEN 검사
- Heating ID20 응답 복구를 이용한 NORMAL CLOSE 검사
- TB18 개방·도통 검사자 확인

## DIO 보드 검사

- ST-LINK로 ID_SET1(PA0), ID_SET2(PC15)의 ID 0~3 전 위치 확인
- MAIN UART4를 통해 ID 0 보드의 첫 번째 DIO 경로 검사
- MAIN 그룹 맵 HR1 DI 읽기 및 HR201 DO 쓰기
- DO1~16 → DI1~16 walking-one 루프백
- 전체 ON/OFF 및 baseline 복귀 검사
- 취소·오류·종료 시 전체 OFF 시도

## 빌드 및 EXE 위치

### 디버깅용

```powershell
.\build-debug.cmd
```

- 실행 파일: `artifacts\Debug\OperateTest.exe`
- 개발 PC에서 디버깅할 때 사용하는 .NET 의존형 빌드입니다.

### 배포용

```powershell
.\publish-deploy.cmd
```

- 현재 작업 공간의 배포 파일: `artifacts\Deploy\OperateTest.exe`와 같은 폴더의 DLL·`runtimes` 디렉터리
- 현재 패키지는 .NET 8 Desktop Runtime이 설치된 PC에서 폴더째 실행합니다.
- `publish-deploy.cmd`는 필요한 런타임 패키지를 내려받을 수 있는 환경에서 독립 실행형 단일 파일을 다시 생성합니다.

## 실행

```powershell
dotnet run --project .\OperateTest.App\OperateTest.App.csproj
```

MAIN/DIO 운용 펌웨어와 기존 Fault Emulator는 이 솔루션에서 참조하거나 수정하지 않습니다.


## 화면 구성

Fault Emulator와 같은 흐름으로 초기 선택 화면, 좌측 메뉴형 보드 대시보드, 카드형 검사 화면을 사용합니다.

- 대시보드
- 통신 설정
- 통신 검사
- FAULT 및 스위치 검사

로터리 스위치, FAULT 접점, DIO ID DIP 검사는 각 항목의 열기 버튼으로 독립 검사창을 실행합니다.

## 연속 검사 결과 파일 검증

```powershell
.\verify-workbook-flow.cmd
```

다음 흐름을 임시 Excel 문서로 자동 검증합니다.

- 동일 문서 10개 및 최대 30개 보드 누적
- 중복 시리얼과 31번째 기록 차단
- 중단 검사 체크포인트 복구
- 새 문서 `_02` 생성
- COMM/DIO 결과 문서 분리
- Excel 잠금 중 저장 실패와 기존 파일 무손상

## 개발·배포 실행 파일 운용

- 기능 수정과 현장 확인은 `artifacts\Debug\OperateTest.exe`에서 수행합니다.
- Debug 파일은 `dotnet build OperateTest.sln -c Debug` 또는 `build-debug.cmd`로 갱신합니다.
- 사용자가 배포를 확정하기 전에는 `artifacts\Deploy\OperateTest.exe`를 다시 게시하지 않습니다.
- 최종 승인 후에만 `publish-deploy.cmd`를 실행하고 배포 파일의 버전과 SHA-256을 기록합니다.
