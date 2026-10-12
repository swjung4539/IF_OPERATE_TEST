# 파일 선택 종료 현상 수정 보고

## 현상

- 검사 준비 화면에서 `파일 선택`을 누르면 프로그램이 멈춘 뒤 종료됨.

## 원인

- WinForms의 `OpenFileDialog`와 `FolderBrowserDialog`는 OLE를 사용하는 STA 스레드에서 실행되어야 한다.
- 프로그램 진입점이 `async Task Main` 형태로 변경되어 파일 대화상자를 실행하는 UI 스레드의 STA 보장이 약해졌고, 대화상자 예외가 클릭 이벤트 밖으로 전파되면서 프로세스가 종료될 수 있었다.

## 수정 내용

- `[STAThread] private static void Main()` 진입점으로 복구해 UI 스레드의 STA 상태를 보장했다.
- 기준서 파일 선택, 결과 폴더 선택, 중단 검사 파일 선택에 복구 가능한 예외 처리를 추가했다.
- UI 예외 전역 처리와 로컬 오류 로그를 추가했다.
- 오류 로그는 `%LOCALAPPDATA%\OperateTest\Logs\crash-YYYYMMDD.log`에 기록된다.
- Debug 식별 버전을 `1.0.0-stage5.3-debug`로 변경했다.
- Windows 응답 없음 보고서에서 파일창에 회사 보안/드라이브 Shell 확장이 로드된 것을 확인했다.
- 최신 Shell 파일창 대신 호환성 모드를 사용하고, 시작 위치를 접근 가능한 로컬 폴더로 제한했다.

## 검증 결과

- Release 빌드: 성공
- 자동 흐름 검증: 201개 전부 통과
- 추가 검증: STA 진입점, 비동기 진입점 미사용, UI 예외 처리 등록, STA UI 검증 스레드

## 현장 확인 항목

- 새 배포 EXE 실행 후 COMM/DIO 검사 준비 화면에서 `파일 선택` 창이 정상적으로 열리고 취소/선택되는지 확인한다.
- 현장 PC의 보안 프로그램 또는 Windows Shell 확장 문제로 파일 대화상자 자체가 실패하는 경우, 프로그램은 종료되지 않고 오류 메시지와 로그 경로를 표시해야 한다.

## 실행 파일 운용 원칙

- 배포 확정 전 수정 및 확인은 `artifacts\Debug\OperateTest.exe`에서 수행한다.
- 사용자가 배포를 확정하기 전에는 `artifacts\Deploy\OperateTest.exe`를 다시 게시하지 않는다.
