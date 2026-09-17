# 13 Protected Paths

AI용 압축 규칙. 상세 설명은 `README.md`를 따른다.

## Do Not Edit

- `Drivers/`
- `Middlewares/`
- `m_*.c`, `m_*.h` 예: `m_modbus.c`, `m_datablock.h`

## Reason

- CubeMX/외부 라이브러리/모듈 파일은 재생성 또는 별도 소유권으로 변경 손실 위험.

## Exception

- 사용자가 특정 파일 수정을 명시한 경우만 가능.
- 실행 전 근거를 제시하고 재확인한다.
