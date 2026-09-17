#ifndef A_DESIRED_STATE_MIRROR_H
#define A_DESIRED_STATE_MIRROR_H

#ifdef __cplusplus
extern "C" {
#endif

#include "a_modbus_slave.h"

#include <stdbool.h>
#include <stdint.h>

bool DesiredStateMirror_Init( void );
void DesiredStateMirror_TrackWrite( uint8_t port, const ModbusSlaveWriteRequest_t *request );
void DesiredStateMirror_Service( uint32_t now_ms );
void DesiredStateMirror_SetPortReady( uint8_t port );
void DesiredStateMirror_RestoreService( void );

#ifdef __cplusplus
}
#endif

#endif /* A_DESIRED_STATE_MIRROR_H */
