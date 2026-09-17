# TestMode

MAIN / DIO 보드용 별도 개발 프로젝트. 현재 7단계: MAIN/DIO USB 검사 기능과 자동 실행 기반 연결 완료. 실제 보드 검증은 아직 진행하지 않았다.

## 원본 보존

- `../main/`, `../sub/`의 기존 프로젝트는 수정·빌드·정리·플래싱 대상으로 사용하지 않는다.
- 펌웨어 변경은 `Firmware/`의 복사본에서만 한다.
- PC 참고 코드도 `PC/FirmwareUpdaterReference/`에 복사했으며 원본에 프로젝트 참조를 걸지 않는다.
- `Docs/Baseline/original-files.json`은 시작 시 main/sub 파일의 SHA-256 기준이다. `.git` 내부는 제외했다.
- `Docs/Baseline/copied-files.json`은 복사한 파일과 출처, 해시를 기록한다.
- 기존 빌드 결과와 캐시는 복사하지 않았다. 목록은 `excluded-files.json`에 있다.

## 구조

| 경로 | 용도 |
|---|---|
| PC/TestMode.App | .NET 8 WinForms 선택 화면, MAIN/DIO TEST 화면, 공통 펌웨어 화면 |
| PC/FirmwareUpdaterReference | 기존 업데이터의 수정하지 않은 참고용 복사본 |
| Firmware/Main | IF_main 개발용 복사본 |
| Firmware/MainBootloader | 기존 MAIN 부트로더 복사본 |
| Firmware/DIO | 기존 interface_sub 복사본. USB 부트로더는 아직 없음 |
| Firmware/Shared | 기존 부트로더 공유 정의 복사본 |
| Docs | 확정 범위, 환경, 단계 결과, 원본 기준 |
| Tools | 원본 및 복사본 기준 검증 도구 |
| Release | 이후 검증된 배포물 전용 |

## 시작

PC 빌드: `dotnet build PC/TestMode.App/TestMode.App.csproj -c Debug`

PC 실행: `PC/TestMode.App/bin/Debug/net8.0-windows/TestMode.App.exe`

7단계에서는 MAIN/DIO USB 검사, MAIN 부트로더 업데이트, DIO USB CDC 정보 조회, USB 연결 시 초기 화면 표시 기반이 연결되어 있다. 실제 장치가 없으면 스위치 값은 알 수 없음(`—`)으로 표시한다.

화면 검증 실행(TestMode 폴더 기준): `PC/TestMode.App/bin/Debug/net8.0-windows/TestMode.App.exe --ui-check=Docs/Verification/Stage02`

검증 실행만 명시적인 예시 데이터를 사용하고 화면에 표시한다. 실제 장치 연결이나 파일 업로드를 수행하지 않는다. 결과와 화면 캡처는 `Docs/Verification/Stage02`에 저장된다.

복사본 기준 검증: `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Verify-Baseline.ps1 -VerifyCopies`

후속 개발로 복사본을 변경한 뒤 원본만 검증: `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Verify-Baseline.ps1`

위 실행 정책 옵션은 해당 PowerShell 프로세스에만 적용하며 시스템 설정을 변경하지 않는다. 복사본 MAIN `.cproject`의 과거 절대 링커 경로 2개만 프로젝트 내부 경로로 수정했다. `copy-adjustments.json`에 변경 전후 해시를 기록했다. 펌웨어 소스는 수정하지 않았다.

펌웨어는 별도 CubeIDE workspace `TestMode/.ide-workspace`에 `Firmware/Main`, `Firmware/MainBootloader`, `Firmware/DIO`만 import한다. 기존 workspace를 재사용하지 않는다. 프로젝트 이름은 원본과 동일하게 유지했으며 별도 workspace로 충돌을 방지한다. 복사된 launch 설정은 이번 단계에서 실행하지 않았다.

다음 단계는 [진행 계획](Docs/PLAN.md), 도구와 제약은 [개발 환경](Docs/ENVIRONMENT.md)을 참고한다.

완료 기록: [1단계](Docs/STAGE_01.md), [2단계](Docs/STAGE_02.md).
