# 14 Firmware Coding Standard

AI용 압축 규칙. 상세 설명은 `README.md`와 `../references/02-펌웨어개발표준및관리지침.md`를 따른다.

## Priority

1. 안정성
2. 명확성
3. 이식성
4. 유지보수성
5. 팀 규칙

## Before Coding

- 확인: MCU, 주변장치, 계층(Driver/Module/Application), 실행 위치(main/ISR/task/callback), 입력/출력, 예외 처리.
- 제약: blocking 금지, 동적 메모리 금지, 동시성, 함수 길이, 인터페이스(UART/SPI/I2C/CAN/ADC/GPIO).
- 정보 부족 시 추측 구현 금지. 사용자에게 확인한다.

## C Rules

- ANSI C 기반.
- 정수형은 `stdint.h` 고정폭 자료형 우선. `int` 중심 사용 지양.
- `#include`에 절대/상대 디렉터리 경로를 직접 쓰지 않는다.
- `if`, `for`, `while`, `switch`는 단일 문장이어도 중괄호 사용.
- 이중 대입 금지.
- 전역 변수는 필요한 경우로 제한.
- ISR/task/callback/timer handler는 재진입성, 공유 자원 보호, blocking 여부 확인.
- ISR 내부 긴 처리, blocking 함수, 동적 메모리 할당 금지.

## File Names

- 소문자 시작, `snake_case`.
- 형식: `파일종류_기능명.c`, `파일종류_기능명.h`.
- 접두사: Application `a_`, Driver `d_`, Module `m_`.
- 파일명 중복과 `_` 외 특수문자 금지.

## File Layout

1. 파일 헤딩 주석
2. 헤더 인클루딩
3. 매크로 정의
4. 자료형 정의
5. 함수 선언
6. 전역 변수 및 외부 전역 변수
7. 정적 지역 변수 및 리터럴
8. 함수 정의
9. 지역 함수
10. 전역 함수

기존 파일 수정 시 불필요한 대규모 재정렬 금지.

## Header

- include guard 필수.
- guard 이름은 파일명을 대문자로 변환.
- include 경로 직접 표기 금지.

## Comments

- "무엇"보다 "왜"를 설명.
- 하드웨어/타이밍/예외 처리 이유는 주석으로 남긴다.
- 단순 동작 설명 주석 최소화.
- 복잡하면 주석보다 함수 분리.
- 여러 줄 비활성화: `#if 0` / `#endif`.

## Types

- 우선: `uint8_t`, `int8_t`, `uint16_t`, `int16_t`, `uint32_t`, `int32_t`, `uint64_t`, `int64_t`.
- `float32_t`, `float64_t`는 프로젝트/플랫폼 정의 확인 후 사용.
- struct/union/enum은 `typedef`.
- 태그 suffix: struct `_struct`, union `_union`, enum `_enum`.
- 타입 suffix: struct `_st`, union `_ut`, enum `_et`.

## Operators

- 이중 대입 금지.
- 단항 연산자와 피연산자 사이 공백 없음.
- 이항 연산자 앞뒤 공백 1개.
- 삼항 항목은 소괄호로 명확화. 복잡하면 `if`/`else`.
- 비트 연산은 괄호로 우선순위 명확화.

## Functions

- 지역 함수: `snake_case`.
- 전역 함수: `CamelCase`.
- 이름 형식: `모듈이름_오브젝트_동작` 또는 `모듈이름_동작`.
- 파라미터 최대 5개. 6개 이상은 구조체 고려.
- 큰 구조체/배열은 포인터 전달 우선.
- 함수는 공백 포함 200줄 미만 목표.
- ISR/task/callback은 thread-safe 여부 확인.

## Syntax

- 들여쓰기 TAB, 크기 4.
- 여는 괄호 뒤/닫는 괄호 앞 공백.
- 중괄호는 독립 줄.
- 제어문 단일 문장도 중괄호.
- `for` 세미콜론 뒤 공백.

## Review

- 파일명, 함수명, 들여쓰기, 괄호, 주석.
- ANSI C, 고정폭 자료형.
- boundary, 예외 처리, NULL, overflow, race.
- ISR/task/callback blocking, 재진입성, 공유 자원 보호.
- STM32 빌드 가능 여부와 검증 증빙.

## History

- 2026-06-30: 회사 펌웨어 코딩 표준 프로젝트 적용본.
