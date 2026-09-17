#ifndef A_AT25SF041B_H
#define A_AT25SF041B_H

#ifdef __cplusplus
extern "C" {
#endif

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

bool At25Sf041b_Init( void );
bool At25Sf041b_ReadJedecId( uint8_t id[ 3 ] );
bool At25Sf041b_Read( uint32_t address, uint8_t *buffer, size_t length );
bool At25Sf041b_PageProgram( uint32_t address, const uint8_t *data, size_t length );
bool At25Sf041b_Erase4K( uint32_t address );

#ifdef __cplusplus
}
#endif

#endif /* A_AT25SF041B_H */
