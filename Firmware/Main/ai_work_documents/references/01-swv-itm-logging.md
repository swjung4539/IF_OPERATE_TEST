# SWV / ITM 로그 (STM32CubeIDE)

## 개요

SWV(Serial Wire Viewer)는 SWD 디버그 인터페이스의 SWO 핀을 통해 ITM(Instrumentation Trace Macrocell) 데이터를 PC로 전송하는 기능이다.
`printf()`를 ITM으로 리다이렉션하면 UART 없이 디버그 로그를 출력할 수 있다.

참조: https://wikidocs.net/160075

---

## 필요 조건

### 하드웨어
- SWD 인터페이스 사용 (JTAG 아님)
- ST-Link V2 이상 (SWO 핀 지원 필수)
- 보드에서 SWO 핀(PA14 또는 PB3)이 ST-Link에 연결되어 있어야 함

### CubeMX 설정
- System Core → SYS → Debug: **Serial Wire** 선택

---

## CubeIDE 디버그 설정

1. `Run → Debug Configurations` 열기
2. 사용 중인 디버그 설정 선택
3. `Debugger` 탭 → **Serial Wire Viewer (SWV)** 활성화 체크
4. **Core Clock (MHz)** 에 실제 MCU 동작 클럭 입력 (예: STM32L4 → `80`)
5. `Enable` 체크 후 Apply

디버그 시작 후 `Window → Show View → SWV → SWV ITM Data Console` 열고 포트 0 활성화 후 **Start Trace** 클릭.

---

## 코드 구현

### 방법 1 — `syscalls.c` `_write` 오버라이드 (권장)

STM32CubeIDE가 생성하는 `syscalls.c` 파일의 `_write` 함수를 수정한다.

```c
/* syscalls.c */
#include "stm32l4xx_hal.h"

__attribute__((weak)) int _write(int fd, char *ptr, int len)
{
    (void)fd;
    int i;

    for (i = 0; i < len; i++) {
        ITM_SendChar((uint32_t)ptr[i]);
    }

    return len;
}
```

이후 어디서든 `printf()` 사용 가능:

```c
#include <stdio.h>

printf("[UART4] rx_len=%u\r\n", rx_len);
```

### 방법 2 — `ITM_SendChar` 직접 호출

`printf` 없이 단일 문자 전송:

```c
ITM_SendChar('A');
```

---

## 주의 사항

| 항목 | 내용 |
|---|---|
| Core Clock 불일치 | CubeIDE의 클럭 설정이 실제 MCU 클럭과 다르면 출력이 깨짐 |
| FreeRTOS 사용 시 | `taskENTER_CRITICAL` 구간 안에서 `printf` 호출 금지 (블로킹 가능) |
| 릴리스 빌드 | `ITM_SendChar`은 ITM 연결이 없을 때 블로킹될 수 있으므로 조건부 컴파일 권장 |
| SWO 핀 미연결 | 하드웨어에서 SWO가 ST-Link에 연결되어 있지 않으면 동작 안 함 |

### 릴리스 빌드 안전 처리 예시

```c
#ifdef DEBUG
#include <stdio.h>
#define LOG(fmt, ...) printf(fmt, ##__VA_ARGS__)
#else
#define LOG(fmt, ...) do {} while (0)
#endif
```

---

## 변경 이력

- 2026-06-28: 최초 작성
