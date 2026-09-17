/******************************************************************************
 * @file m_datablock.h
 * @author gjkwon1 (gjkwon1@gst-in.com)
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

#define BROADCAST_ID	0

#define MBS_INPUT_START 0
#define MBS_HOLDING_START 0

#define MBM_HOLDING_START 0
#define MBM_HOLDING_REGS_CNT 16

#define MAX_DEVICE_COUNT 16
#define MAX_DEVICE_BUFFER_COUNT ( MAX_DEVICE_COUNT * 2 )

#define MBS_PV_WORDS 2



/*****************************************************************************/
/** DATATYPES ****************************************************************/
/*****************************************************************************/

// Discrete Inputs map index (1-bit, read-only)
// Modbus Function Code: 0x02
enum {
	MBS_Discrete_Input_SYSTEM_READY,
	MBS_Discrete_Input_FAULT,
	MBS_Discrete_Input_CNT
};

// Coils map index (1-bit, read/write)
// Modbus Function Code: 0x01, 0x05, 0x0F
enum {
	MBS_Coil_RUN,
	MBS_Coil_ALARM_RESET,
	MBS_Coil_CNT
};

// Input Registers map index (16-bit, read-only)
// Modbus Function Code: 0x04

enum {
	MBS_Input_Register_DO_Write_Start_address =1 ,
	MBS_Input_Register_DO_Write_Num = 1 ,
	



	MBS_INPUT_REGS_CNT
};

// Holding Registers map index (16-bit, read/write)
// Modbus Function Code: 0x03, 0x06, 0x10
enum {
	MBS_Holding_Register_RUN,
	MBS_Holding_Register_DO_1_Write_Num_of_coils = 1 ,
	MBS_Holding_Register_DO_1_Write_Start_address =16 ,
	MBS_Holding_Register_DO_1_Write_address = 16 ,
	MBS_Holding_Register_DO_2_Write_Num_of_coils = 1 ,
		MBS_Holding_Register_DO_2_Write_Start_address =17 ,
		MBS_Holding_Register_DO_2_Write_address = 17 ,
	MBS_Holding_Register_DI_1_Read_address  ,
	MBS_Holding_Register_DI_2_Read_address  ,
	MBS_HOLDING_REGS_CNT
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
	
	// Holding Registers (16-bit, read/write)
	// Function Code 0x03: Read Holding Registers
	// Function Code 0x06: Write Single Register
	// Function Code 0x10: Write Multiple Registers
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
extern const DataBlock_st MBM_READ_DB[ MAX_DEVICE_COUNT ];
extern const DataBlock_st MBM_WRITE_DB[ MAX_DEVICE_COUNT ];

/*****************************************************************************/
/** FUNCTION DEFINITIONS *****************************************************/
/*****************************************************************************/


#ifdef __cplusplus
}
#endif

#endif /* _M_MODBUS_DATABLOCK_H_ */
