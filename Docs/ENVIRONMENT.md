# 개발 환경 확인

확인일: 2026-09-17. 시스템 PATH와 원본 IDE workspace는 변경하지 않았다.

## PC

- .NET SDK: 8.0.425 (`C:/Program Files/dotnet/dotnet.exe`)
- WindowsDesktop runtime / reference pack: 8.0.31
- 대상: net8.0-windows / WinForms
- `global.json`은 설치된 .NET 8 SDK를 지정한다.
- `PC/TestMode.App/TestMode.App.csproj` Debug 복원·빌드 성공: 경고 0, 오류 0.
- 빌드 로그: `Baseline/pc-build.log`. 빈 창 실행 및 화면 검증은 아직 하지 않았다.
- SDK 최초 실행 설정은 `TestMode/.dotnet-home`을 사용했다. 도구 PATH 자동 등록 안내가 있었으나 빌드에는 영향이 없었고 시스템 PATH를 변경하지 않았다.
- 참고 업데이터는 기존 net8.0-windows와 System.IO.Ports 8.0.0 의존성을 그대로 유지한다. 이번 단계에서는 참고 업데이터를 빌드하지 않았다.
- 설치 SDK가 8이므로 참고 폴더의 `.slnx` 대신 `.csproj`를 직접 사용한다.

## 펌웨어 도구

설치 루트: `C:/ST/STM32CubeIDE_2.2.0/STM32CubeIDE/`

| 도구 | 확인 결과 |
|---|---|
| STM32CubeIDE | 2.2.0 설치 경로 및 headless-build.bat 확인, IDE 실행은 하지 않음 |
| ARM GCC | --version 실행 성공, 14.3.1 |
| CMake | --version 실행 성공, 4.3.1 |
| Ninja | --version 실행 성공, 1.13.2 |
| Make | CubeIDE plugin 내 make.exe 존재 확인 |
| STM32CubeProgrammer CLI | CubeIDE plugin 내 실행 파일 존재 확인, 장치 접속하지 않음 |

도구 실행 파일은 위 루트의 `plugins/com.st.stm32cube.ide.mcu.externaltools.*` 아래 `tools/bin`에 있다. GCC·CMake·Ninja는 기본 PATH에는 없으므로 CubeIDE 환경 또는 해당 실행 파일의 절대 경로를 사용한다.

## 펌웨어 프로젝트 격리

별도 workspace: `TestMode/.ide-workspace`.

| 폴더 | 기존 프로젝트 이름 | 빌드 구성 |
|---|---|---|
| Firmware/Main | IF_main | Debug, Release, Release_SlotA, Release_SlotB |
| Firmware/MainBootloader | Bootloader | Debug, Release |
| Firmware/DIO | interface_sub | Debug, Release |

- 각 `.project`, `.cproject` XML 파싱 성공. linked resource는 세 프로젝트 모두 0개다.
- 원본 프로젝트 이름을 유지하므로 기존 workspace에 원본과 복사본을 함께 import하지 않는다.
- MAIN 복사본 `.cproject`의 Slot A/B 링커 경로 2개가 과거 `C:/ST/kgjworkspace/github/IF_Controller/IF_main`에 고정되어 있었다. 복사본의 `${workspace_loc:/${ProjName}/...}` 경로로 변경했다.
- 세 프로젝트의 `.project`, `.cproject`를 재검사하여 `C:` 절대 경로와 외부 locationURI 참조가 남지 않았음을 확인했다.
- CMake의 `../../` 소스 경로는 `Firmware/Main/cmake/stm32cubemx` 기준으로 MAIN 복사본 내부를 가리킨다.
- MAIN CMake 기본 설정은 `STM32L452VETX_FLASH.ld`를 사용한다. 이후 A/B용 업데이트 이미지 생성에는 해당 슬롯 링커 설정을 명시적으로 사용해야 한다.
- 이번 단계의 펌웨어 확인은 도구 실행과 프로젝트 설정 점검까지다. 펌웨어 컴파일·링크, IDE import, 보드 실행은 아직 검증하지 않았다.

## 알려진 후속 작업

- DIO USB 부트로더 신규 개발과 메모리 배치: 5단계.
- MAIN/DIO 스위치 원시 입력과 표시값 매핑: 3·5단계.
- 기존 업데이터 BIN 처리에서 HEX 해석 및 동일 이미지 확인으로 확장: 4·6단계.
- MAIN 수동 FAULT 제어와 기존 자동 제어의 충돌 방지: 3단계.
