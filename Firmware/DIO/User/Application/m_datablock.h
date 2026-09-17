/******************************************************************************
 * @file m_datablock.h
 * @author jylee1 (jylee1@gst-in.com)
 * @brief 
 * @version 0.1
 * @date 2023-06-08
 * 
 * @copyright Copyright (c) 2023 Global Standard Technology Co., Ltd.
 * 
 * All rights reserved.
 * This file is part of closed source software.
 * Unauthorized reproduction and redistribution prohibited.
 *****************************************************************************/

#ifndef _M_MODBUS_DATABLOCK_H_
#define _M_MODBUS_DATABLOCK_H_

#ifdef __cplusplus
extern "C" {
#endif

/*****************************************************************************/
/** INCLUDES *****************************************************************/
/*****************************************************************************/

#include <stdlib.h>
#include <stdint.h>

/*****************************************************************************/
/** MACRO DEFINITIONS ********************************************************/
/*****************************************************************************/
#define MAX_SMPS_ID		16 		// including id 0
#define MAX_SMPS_CMD	( MAX_SMPS_ID * 2 ) //read, write
#define BRAODCAST_ID	0

#define MBS_INPUT_START 0
#define MBS_HOLDING_START 0

#define MBM_HOLDING_START 0




/*****************************************************************************/
/** DATATYPES ****************************************************************/
/*****************************************************************************/

enum {


	MBS_INPUT_REGS_CNT
};

enum {


	MBS_HOLDING_REGS_CNT
};

enum
{
	MBM_HOLDING_REGS_CNT ,
};

typedef struct Modbus_DataBlock_struct
{
	// Discrete Inputs (1-bit, read-only) - Function Code 0x02
	uint16_t * DInputs;
	uint16_t DInputsStart;
	uint16_t DInputsCnt;
	
	// Coils (1-bit, read/write) - Function Code 0x01, 0x05, 0x0F
	uint16_t * Coils;
	uint16_t CoilsStart;
	uint16_t CoilsCnt;
	
	// Holding Registers (16-bit, read/write) - Function Code 0x03, 0x06, 0x10
	uint16_t * Holdings;
	uint16_t HoldingsStart;
	uint16_t HoldingsCnt;
	
	// Input Registers (16-bit, read-only) - Function Code 0x04
	uint16_t * Inputs;
	uint16_t InputsStart;
	uint16_t InputsCnt;
} DataBlock_st;

/*****************************************************************************/
/** FUNCTION DECLARATIONS ****************************************************/
/*****************************************************************************/

/*****************************************************************************/
/** LOCAL VARIABLES **********************************************************/
/*****************************************************************************/

/*****************************************************************************/
/** GLOBAL VARIABLES *********************************************************/
/*****************************************************************************/

extern const DataBlock_st MBSDB;
extern const DataBlock_st MBM_READ_DB[ MAX_SMPS_ID ];
extern const DataBlock_st MBM_WRITE_DB[ MAX_SMPS_ID ];

/*****************************************************************************/
/** FUNCTION DEFINITIONS *****************************************************/
/*****************************************************************************/


#ifdef __cplusplus
}
#endif

#endif /* _M_MODBUS_DATABLOCK_H_ */
