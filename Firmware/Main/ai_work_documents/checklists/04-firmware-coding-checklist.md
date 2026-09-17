# Firmware Coding Checklist

AI용 압축 체크리스트. 상세 기준은 `../agent-guides/14-firmware-coding-standard.md`.

## Request

- [ ] MCU/주변장치/실행 위치 확인
- [ ] Driver/Module/Application 계층 확인
- [ ] 입력/출력/예외/timeout/retry 확인
- [ ] ISR/task/callback 및 동시성 확인
- [ ] 파일명/출력 형식 확인

## File And Include

- [ ] `a_`, `d_`, `m_` 접두사와 `snake_case`
- [ ] `.c`/`.h` 이름 일관
- [ ] include guard
- [ ] include 경로 하드코딩 없음
- [ ] 소스 구역 순서 확인

## Types And Declarations

- [ ] `stdint.h` 고정폭 자료형
- [ ] `float32_t`/`float64_t` 정의 확인
- [ ] typedef/suffix 규칙 확인
- [ ] 전역 변수 최소화

## Functions

- [ ] 지역 `snake_case`, 전역 `CamelCase`
- [ ] 파라미터 5개 이하 또는 구조체 사용
- [ ] 함수 200줄 미만 목표
- [ ] 복잡한 처리 함수 분리
- [ ] ISR/task/callback 재진입성/thread-safe 확인

## Syntax And Operators

- [ ] 제어문 중괄호 항상 사용
- [ ] 중괄호 독립 줄
- [ ] TAB 들여쓰기
- [ ] 괄호 공백 규칙
- [ ] 이중 대입 없음
- [ ] 비트 연산 괄호
- [ ] 복잡한 삼항 회피

## Comments

- [ ] 이유/제약/예외 중심
- [ ] 의미 없는 주석 없음
- [ ] 하드웨어/타이밍 제약 기록
- [ ] 여러 줄 비활성화는 `#if 0` / `#endif`

## Firmware Safety

- [ ] ISR 긴 처리/blocking/동적 할당 없음
- [ ] task 공유 자원 보호
- [ ] NULL/overflow/buffer length/timeout/retry boundary 확인
- [ ] HAL/CMSIS/FreeRTOS API 호출 위치 안전

## Verification

- [ ] STM32 빌드 확인
- [ ] 미빌드 시 미검증 명시
- [ ] 장비 검증 필요 항목 표시
- [ ] 관련 요구사항/인터페이스/프로토콜/데이터 모델 문서 갱신 판단
