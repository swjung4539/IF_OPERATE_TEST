# Checklists Guide

이 폴더의 `README.md`는 사람이 보는 안내 문서다. 다른 체크리스트 `.md` 파일은 AI가 작업 중 빠르게 확인하는 압축 문서로 유지한다.

## 작성 방식

- `README.md`: 체크리스트의 목적, 역할, 사용 시점 설명.
- 개별 체크리스트: 실행 중 확인할 항목만 짧게 유지.
- 요구사항, 설계, 인터페이스, 프로토콜 문서는 `../documents/`에 둔다.
- 산출물은 `../artifacts/`에 둔다.
- 모든 `.md` 파일은 UTF-8로 작성한다.

## 주요 문서

- `01-tdd-checklist.md`: TDD 시작, Red, Green, Refactor, 보고 확인.
- `02-debugging-checklist.md`: 디버깅 시작, 원인 분석, 수정, 검증, 보고 확인.
- `03-implementation-completion-checklist.md`: 구현 단계별 작업/리뷰/검증 상태 추적.
- `04-firmware-coding-checklist.md`: 펌웨어 C 코딩 표준 점검.
- `05-communication-checklist.md`: UART/Modbus/GST 등 통신 항목 전체 점검.

## 운영 원칙

체크리스트는 긴 설명 문서가 아니라 누락 방지 도구다. 판단 기준의 상세 설명은 `../agent-guides/README.md`와 관련 가이드 문서에 둔다.
