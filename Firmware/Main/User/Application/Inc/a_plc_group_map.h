#ifndef A_PLC_GROUP_MAP_H
#define A_PLC_GROUP_MAP_H

#ifdef __cplusplus
extern "C" {
#endif

#include "a_modbus_slave.h"

#include <stdbool.h>
#include <stdint.h>

/* PROTO-012: UART4 PLC 4-그룹 통합 Read/Write 맵 번역 계층.
 *
 * PLC는 물리 장치 Slave ID를 몰라도 그룹 Slave ID와 사양서 절대주소만으로
 * 통합 map에 접근한다. SharedMap 구조와 하위 통신 task는 변경하지 않는다.
 *
 * Read  (FC03): ID1=DIO(1~2), ID2=Heating(3~110), ID3=Gas(111~122), ID4=Backup(123~164)
 * Write (FC10): ID5=DIO(201~202), ID6=Heating(203~265), ID7=Backup(266~305), ID8=SlaveID설정(306~317, 317=Apply Trigger)
 */

#define PLC_GROUP_MAX_FORWARDS 3U

typedef enum {
    PLC_GROUP_RESULT_NOT_HANDLED = 0, /* 그룹 요청이 아님 - 기존 slave 경로로 처리 */
    PLC_GROUP_RESULT_NO_RESPONSE,     /* 그룹 요청이지만 무효 - 응답하지 않음 */
    PLC_GROUP_RESULT_RESPONSE_READY   /* read 응답 또는 write ACK 준비됨 */
} PlcGroupResult_t;

typedef struct {
    /* 하위 Modbus master로 전달할 write (DIO/Heating) */
    ModbusSlaveWriteRequest_t forwards[PLC_GROUP_MAX_FORWARDS];
    uint8_t forward_count;
    /* GST peer로 전달할 write (Backup Interface) */
    ModbusSlaveWriteRequest_t gst_request;
    uint8_t gst_forward;
} PlcGroupWriteActions_t;

PlcGroupResult_t PlcGroupMap_HandleRequest(const uint8_t *request,
                                           uint16_t request_len,
                                           uint8_t *response,
                                           uint16_t response_capacity,
                                           uint16_t *response_len,
                                           PlcGroupWriteActions_t *actions);

/* Heating jacket당 EEPROM Save/ALARM Reset 2개 x MAX_ACTIVE_HEATING_ID_COUNT(3)개 jacket */
#define PLC_GROUP_POLL_MAX_FORWARDS 6U

/* PLC가 EEPROM Save/ALARM Reset을 1로 유지한 채 write를 멈춘 경우를 감지해
 * 슬레이브로 0을 대신 write하도록 forward 목록을 채운다. 메인 루프에서 주기 호출한다.
 * 반환값: forwards에 채워진 유효 항목 수 */
uint8_t PlcGroupMap_Poll(ModbusSlaveWriteRequest_t *forwards, uint8_t max_forwards);

/* Heating 국번이 재배정되면 해당 jacket 슬롯의 EEPROM Save/ALARM Reset
 * rising-edge 상태를 초기화한다. ServiceConfigApply에서 국번 변경 감지 시 호출한다. */
void PlcGroupMap_ResetHeatingEdgeState(uint8_t jacket);

#ifdef __cplusplus
}
#endif

#endif /* A_PLC_GROUP_MAP_H */
