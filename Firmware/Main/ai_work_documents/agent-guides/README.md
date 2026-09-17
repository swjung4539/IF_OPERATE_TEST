# Agent Guides

이 폴더는 에이전트가 작업할 때 따르는 세부 작업 규칙을 보관하는 곳이다.

## 사용 원칙

- 공통 작업 규칙, workflow, SDD/TDD, debugging, review, verification, 안전 규칙을 문서화한다.
- 사용자가 질문한 경우 바로 수정하지 않고 먼저 판단과 반영안을 제시해야 하는 규칙도 이 폴더에서 관리한다.
- 프로젝트 요구사항, 설계, 프로토콜 같은 관리 문서는 `ai_work_documents/documents/`에 둔다.
- 결과 산출물은 `ai_work_documents/artifacts/`에 둔다.
- 모든 `.md` 파일은 UTF-8 인코딩으로 작성하고 저장한다.

## 문서 작성 방식

이 폴더의 `README.md`는 사람이 읽는 안내 문서다. 문서의 목적, 배경, 참조 방식, 각 가이드의 역할은 여기에서 자세히 설명한다.

개별 `.md` 파일은 인공지능이 작업 중 직접 참조하는 실행 규칙 문서다. 토큰 사용량을 줄이기 위해 배경 설명을 길게 쓰지 않고, 트리거와 행동 규칙만 짧게 유지한다.

작성 기준은 다음과 같다.

- `INDEX.md`: 에이전트가 항상 먼저 읽는 최상위 목차. 상황별 참조 경로만 둔다.
- `README.md`: 사람이 보는 상세 안내. 구조, 의도, 사용 예시를 설명한다.
- 개별 가이드 `.md`: AI가 보는 압축 규칙. `언제 볼지`, `무엇을 할지`, `어디를 참조할지`만 남긴다.
- 같은 규칙은 한 곳에만 원본으로 둔다. 다른 문서에서는 링크나 짧은 참조만 사용한다.
- 사람이 이해해야 하는 긴 설명은 `README.md`에 두고, AI가 반복 참조하는 문서는 체크리스트와 금지/허용 규칙 중심으로 압축한다.

## 주요 문서

- `00-core-rules.md`: 기본 작업 규칙.
- `01-superpowers-scope.md`: Superpowers 방법론을 프로젝트에 어느 범위까지 참고할지 정한다.
- `02-workflow.md`: 일반 작업 흐름.
- `03-sdd.md`: Subagent-Driven Development 규칙.
- `04-tdd.md`: Test-Driven Development 규칙.
- `05-debugging.md`: 디버깅 절차.
- `06-code-review.md`: 코드 리뷰 기준.
- `07-verification.md`: 완료 전 검증 기준.
- `13-protected-paths.md`: 수정 금지 경로.
- `14-firmware-coding-standard.md`: 펌웨어 C 코딩 표준.
- `15-context-learning-memory.md`: 장시간 작업에서 컨텍스트를 압축하고, 재사용 가능한 학습 후보와 기억 위치를 판단하는 AI용 압축 규칙.

## 가이드별 상세 설명

`00-core-rules.md`는 모든 작업의 최상위 행동 규칙이다. 사용자가 명시적으로 지시한 것만 수행하고, 질문형 요청은 곧바로 수정 작업으로 해석하지 않는다는 원칙을 담는다. 구현이나 문서 반영이 필요한 경우에는 변경 파일과 내용을 먼저 보여주고, 승인 후에만 실제 반영한다.

`01-superpowers-scope.md`는 외부 Superpowers 방법론의 적용 범위를 제한한다. 이 프로젝트에서는 성급한 구현을 막기 위한 참고 방법론으로만 사용하고, 자동 강제하지 않는다. 브레인스토밍, 계획, SDD, TDD, 디버깅, 리뷰, 검증처럼 작업 품질을 높이는 영역에만 선택적으로 적용한다.

`02-workflow.md`는 일반 작업 흐름을 정의한다. 질문에는 답변만 하고, 설계나 기능 추가처럼 상태를 바꾸는 작업은 목적, 제약, 성공 기준과 검증 방법을 먼저 정한다.

`03-sdd.md`는 큰 작업을 독립 단위로 나눠 구현과 리뷰를 분리하는 방식이다. subagent를 사용할 수 없는 환경에서는 강제하지 않고, 메인 에이전트가 작은 단위로 나눠 자체 리뷰한다.

`04-tdd.md`는 테스트로 검증 가능한 기능 추가, 버그 수정, 로직 변경에 적용한다. 실패 테스트를 먼저 만들고, 실패 확인 후 최소 구현과 재검증을 진행한다.

`05-debugging.md`는 원인 확인 없는 수정을 금지한다. 에러 메시지, 재현 절차, 최근 변경, 정상 예시를 확인한 뒤 가설을 하나씩 검증한다.

`06-code-review.md`는 코드 리뷰 기준이다. 단순 스타일보다 실제 버그 가능성, 안정성, 회귀, 테스트 누락, 요구사항 불일치를 우선한다. 펌웨어 코드는 ISR/task 경계, 공유 데이터, timeout, CRC/error path, boundary 값을 특히 확인한다.

`07-verification.md`는 검증 보고 기준이다. 빌드, 테스트, 실행을 실제로 확인하지 않았다면 성공했다고 말하지 않고, 수행한 명령과 결과 또는 미검증 사실을 명확히 남긴다.

`08-yagni-dry.md`는 불필요한 기능과 성급한 추상화를 막는다. DRY는 비슷해 보이는 코드를 무조건 합치는 뜻이 아니라, 같은 지식이 여러 곳에 흩어져 불일치하는 상황을 막는 기준으로 적용한다.

`09-safety-communication.md`는 사용자 변경 보호와 보고 방식을 정한다. 사용자 변경을 되돌리거나 덮어쓰지 않고, 수정 전에는 어떤 파일을 왜 바꾸는지 말한다.

`10-skill-tool-recommendation.md`는 skill, MCP, tool 추천 기준이다. 적합한 도구가 있어도 자동 실행하지 않고, 이유와 승인 필요 여부를 먼저 말한다.

`11-auto-correction-loop.md`는 빌드/테스트/린트 실패를 반복 수정할 때 쓰는 제한적 루프다. 시작 전 검증 명령, 수정 범위, 반복 횟수를 정하고 기본 3회까지만 반복한다.

`12-issue-feedback-log.md`는 AI 작업 중 문제가 발생했을 때 `feedback/01-issue-log.md`에 사실 중심으로 기록하는 기준이다.

`13-protected-paths.md`는 CubeMX, 외부 라이브러리, 모듈 소유 파일처럼 수정하면 안 되는 경로를 정한다.

`14-firmware-coding-standard.md`는 회사 펌웨어 C 코딩 표준의 AI 작업용 축약본이다. 원본 참고 자료는 `../references/02-펌웨어개발표준및관리지침.md`이며, 실제 코드 작업에서는 이 축약본과 체크리스트를 먼저 본다.

`15-context-learning-memory.md`는 장시간 작업에서 컨텍스트를 줄이고, 학습할 가치가 있는 패턴과 기억 저장 위치를 판단하는 기준이다.

## 세부 참조 기준

### 기본 참조

- 단순 질문이면 세부 문서를 열지 않고 질문에만 답한다.
- 기본 규칙 확인이 필요하면 `00-core-rules.md`를 참조한다.
- Superpowers 반영 범위 판단이 필요하면 `01-superpowers-scope.md`를 참조한다.
- 일반 작업 절차가 필요하면 `02-workflow.md`를 참조한다.
- YAGNI / DRY 기준이 필요하면 `08-yagni-dry.md`를 참조한다.
- 안전, 커뮤니케이션, UTF-8 저장 기준이 필요하면 `09-safety-communication.md`를 참조한다.
- 펌웨어 C 코드 작성, 수정, 리뷰 기준이 필요하면 `14-firmware-coding-standard.md`와 `../checklists/04-firmware-coding-checklist.md`를 참조한다.

### 작업 유형별 참조

- SDD 요청이면 `03-sdd.md`, `06-code-review.md`, `07-verification.md`를 참조한다.
- TDD 요청이면 `04-tdd.md`, `07-verification.md`, `../checklists/01-tdd-checklist.md`를 참조한다.
- 디버깅 요청이면 `05-debugging.md`, `07-verification.md`, `../checklists/02-debugging-checklist.md`를 참조한다.
- 코드 리뷰 요청이면 `06-code-review.md`, `07-verification.md`를 참조한다.
- 펌웨어 코드 리뷰 요청이면 `06-code-review.md`, `07-verification.md`, `14-firmware-coding-standard.md`, `../checklists/04-firmware-coding-checklist.md`를 참조한다.
- 완료 전 검증이 필요하면 `07-verification.md`를 참조한다.
- 구현 작업을 시작하거나 구현 순서를 판단해야 하면 `../documents/07-implementation-plan.md`를 참조한다.
- Skill, MCP, tool 추천이 필요하면 `10-skill-tool-recommendation.md`를 참조한다.
- 자동 교정 루프가 필요하면 `11-auto-correction-loop.md`를 참조한다.
- AI 작업 중 문제가 발생하면 `12-issue-feedback-log.md`를 참조하고 `../feedback/01-issue-log.md`에 자동 기록한다.
- 파일 수정 전 수정 금지 대상인지 확인이 필요하면 `13-protected-paths.md`를 참조한다.
- 장시간 작업, 대량 파일 탐색, 세션 요약, 학습 패턴 추출, 기억 위치 판단이 필요하면 `15-context-learning-memory.md`를 참조한다.

### 보조 문서 참조

- 참고 자료가 필요하면 `references/`를 참조한다.
- 요구사항, 설계, 인터페이스, 데이터 구조, 통신 프로토콜, 설계 결정, 구현 계획 문서는 `documents/`를 사용한다.
- 코드 변경이 요구사항, 인터페이스, 데이터 구조, 통신 프로토콜, 설계 결정에 영향을 주면 `../documents/README.md` 기준으로 관련 문서 갱신 필요 여부를 판단하고 git에 커밋한다.
- git 커밋 전에는 커밋 대상 후보 파일 목록과 커밋 메시지 초안을 사용자에게 먼저 보여주고 승인받는다. 승인받은 파일만 stage/commit 하며, 기존 사용자 변경이나 IDE 메타데이터는 명시 승인 없이 포함하지 않는다.
- 시퀀스 다이어그램, 설명용 다이어그램, 검토 보고서, 최종 요약 같은 결과 산출물은 `artifacts/`를 사용한다.
- 이전 세션 맥락 복원이 필요하거나 사용자가 이전 작업을 언급하면 `../feedback/02-work-log.md`를 참조한다.
- 사용자가 대화에서 질문하고 결정한 내용은 `../feedback/02-work-log.md`에 날짜 기준으로 누적 기록한다.
