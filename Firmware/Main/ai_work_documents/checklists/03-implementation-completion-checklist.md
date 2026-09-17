# Implementation Completion Checklist

AI용 상태 추적 문서. 상세 구현 계획은 `../documents/07-implementation-plan.md`.

## Status Values

- 작업: `미시작`, `진행중`, `완료`, `보류`
- 리뷰: `미검토`, `검토중`, `통과`, `수정필요`
- 검증: `미검증`, `빌드불가`, `빌드확인`, `장비검증필요`, `장비확인`

## Rules

- 구현/리뷰/검증 후 상태를 갱신한다.
- 구현만 완료되면 `진행중` + `미검토`.
- 리뷰 통과 전 다음 단계 완료 표시 금지.

## Steps

| ID | 단계 | 작업 | 리뷰 | 검증 | 메모 |
|---|---|---|---|---|---|
| IMP-001 | 기존 통신 코드 기준점 확인 | 완료 | 통과 | 빌드확인 | 사용자 통신 모듈 없음. CubeMX UART/DMA/GPIO/FreeRTOS 골격 재사용. |
| IMP-002 | 통신 설정 모듈 추가 | 완료 | 통과 | 빌드확인 | 기본값/RAM config/active ID API. |
| IMP-003 | 통합 map 모듈 추가 | 완료 | 통과 | 빌드확인 | active ID static entry, GST global, pending write, comm status. |
| IMP-004 | poll/write item table 추가 | 완료 | 통과 | 빌드확인 | PROTO-008 기준 정적 table. |
| IMP-005 | UART4 read path 전환 | 완료 | 통과 | 빌드확인 | 통합 map read 응답. |
| IMP-006 | UART4 write pending path 전환 | 완료 | 통과 | 빌드확인 | pending write ACK/무응답 정책. |
| IMP-007 | 하위 Modbus master task 구현 | 완료 | 통과 | 장비확인 | 3 task, pending 우선, active ID polling. 2026-07-30 실 장비 검증 완료. |
| IMP-008 | USART1 GST 3-task map 연동 | 완료 | 통과 | 장비확인 | RX/Protocol/TX task, GST queue, map sync, retry. 2026-07-30 실 장비 검증 완료. |
| IMP-009 | 기존 bridge hub 경로 제거/비활성화 | 완료 | 통과 | 빌드확인 | forwarding 없음. GST task가 map sync 담당. |
| IMP-010 | 빌드 및 단계별 검증 | 완료 | 통과 | 장비확인 | Debug 빌드 성공. 2026-07-30 UART 장비 검증 완료(DIO/Heating/Gas polling, GST 송수신, PLC read/write, config apply). |
| IMP-013 | 런타임 Config Apply (HR110~122) | 완료 | 통과 | 장비확인 | HR110~121 write 기반 active ID/Gas profile 런타임 변경, 결과 HR122 기록. 2026-07-30 실 장비 검증 완료. |
| IMP-014 | USB 펌웨어 업데이트 (A/B 이중 슬롯 + C# GUI) | 완료 | 통과 | 장비확인 | 부트로더 A/B 슬롯, IWDG 자동 롤백, C# GUI 종단 갱신. 2026-07-30 실 보드 검증 완료. |

## History

- 2026-06-27: 최초 작성, IMP-001~010 상태 추적.
- 2026-06-28: 7-task 구조 반영 완료, Debug 빌드 확인, 장비 검증 남음.
- 2026-07-30: IMP-007/IMP-008/IMP-010 실 장비 검증 완료로 갱신. IMP-013(런타임 Config Apply), IMP-014(USB 펌웨어 업데이트) 신규 행 추가 및 장비확인 반영.
