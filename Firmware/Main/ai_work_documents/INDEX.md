# 에이전트 작업 지침

이 문서는 에이전트가 항상 먼저 읽는 목차 문서다. 

## 최우선 규칙

사용자가 명시적으로 지시한 내용만 수행한다. 사용자의 지시는 모든 작업 방식, 운영 정책 및 기본 에이전트 행동 원칙에 우선한다.

사용자의 지시를 이행하기 전에 문서 구조와 참조 문서를 우선 확인하고, 문서에 명시된 기준과 내용을 근거로 작업을 수행한다.

## 기본 행동 규칙

질문에는 우선 답변과 판단만 제공한다. 파일 수정, 코드 변경, 문서 반영, 빌드, 테스트 등 시스템 또는 산출물의 상태를 변경하는 작업은 사용자가 명시적으로 요청했거나, 제안 후 사용자의 승인을 받은 경우에만 수행한다.

수정이나 반영이 필요하다고 판단되는 경우에는 즉시 변경하지 않고, 먼저 "이런 의미로 이해했는데 반영할까요?"와 같은 방식으로 의도를 확인한다. 사용자의 승인을 받은 후에만 변경 사항을 적용한다.

모든 .md 파일은 UTF-8 인코딩을 사용하여 작성하고 저장한다.

## 문서 구조

```text
IF_MAIN/
└── ai_work_documents/
    ├── agent-guides/     # 세부 작업 지침 (README.md에 참조 기준 정리)
    ├── documents/        # 요구사항/설계/인터페이스 등 프로젝트 관리 문서
    ├── artifacts/        # 다이어그램/보고서/요약 등 결과 산출물
    ├── feedback/         # 피드백 및 작업 로그
    ├── references/       # 참고 자료
    └── checklists/       # 체크리스트
```

## 참조 방식


| 상황 | 참조 문서 |
|---|---|
| 기본 규칙 확인 | `agent-guides/00-core-rules.md` |
| SDD | `agent-guides/03-sdd.md`, `agent-guides/06-code-review.md`, `agent-guides/07-verification.md` |
| TDD | `agent-guides/04-tdd.md`, `agent-guides/07-verification.md`, `checklists/01-tdd-checklist.md` |
| 디버깅 | `agent-guides/05-debugging.md`, `checklists/02-debugging-checklist.md` |
| 코드 리뷰 | `agent-guides/06-code-review.md`, `agent-guides/07-verification.md` |
| 펌웨어 코딩 표준 | `agent-guides/14-firmware-coding-standard.md`, `checklists/04-firmware-coding-checklist.md` |
| 구현 순서 판단 | `documents/07-implementation-plan.md` |
| 문제 발생 | `agent-guides/12-issue-feedback-log.md` → `feedback/01-issue-log.md` 기록 |
| 수정 금지 경로 확인 | `agent-guides/13-protected-paths.md` |
| 컨텍스트/토큰 최적화, 지속적 학습, 메모리 영속성 | `agent-guides/15-context-learning-memory.md` |
| 이전 세션 맥락 복원 | `feedback/02-work-log.md` |
| 프로젝트 문서 갱신 | `documents/README.md` 기준 판단 |
