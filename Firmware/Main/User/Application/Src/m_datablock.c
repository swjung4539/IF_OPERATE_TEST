/******************************************************************************
 * @file m_datablock.c
 * @author jylee1 (jylee1@gst-in.com)
 * @brief 
 * @version 0.1
 * @date 2023-06-22
 * 
 * @copyright Copyright (c) 2023 Global Standard Technology Co., Ltd.
 * 
 * All rights reserved.
 * This file is part of closed source software.
 * Unauthorized reproduction and redistribution prohibited.
 *****************************************************************************/

/*****************************************************************************/
/** INCLUDES *****************************************************************/
/*****************************************************************************/

#include <m_datablock.h>

/*****************************************************************************/
/** MACRO DEFINITIONS ********************************************************/
/*****************************************************************************/

#define MBM_DB_ENTRY( DEVICE_IDX, BUFFER_IDX ) \
	[ DEVICE_IDX ] = { \
	.Holdings = mbm_hold_regs[ BUFFER_IDX ], \
	.HoldingsCnt = MBM_HOLDING_REGS_CNT, \
	.HoldingsStart = MBM_HOLDING_START, \
	},

#define MBM_READ_DB_MAP( ENTRY ) \
	ENTRY( 0, BROADCAST_ID ) \
	ENTRY( 1, 1 ) \
	ENTRY( 2, 2 ) \
	ENTRY( 3, 3 ) \
	ENTRY( 4, 4 ) \
	ENTRY( 5, 5 ) \
	ENTRY( 6, 6 ) \
	ENTRY( 7, 7 ) \
	ENTRY( 8, 8 ) \
	ENTRY( 9, 9 ) \
	ENTRY( 10, 10 ) \
	ENTRY( 11, 11 ) \
	ENTRY( 12, 12 ) \
	ENTRY( 13, 13 ) \
	ENTRY( 14, 14 ) \
	ENTRY( 15, 15 )

#define MBM_WRITE_DB_MAP( ENTRY ) \
	ENTRY( 0, 16 ) \
	ENTRY( 1, 17 ) \
	ENTRY( 2, 18 ) \
	ENTRY( 3, 19 ) \
	ENTRY( 4, 20 ) \
	ENTRY( 5, 21 ) \
	ENTRY( 6, 22 ) \
	ENTRY( 7, 23 ) \
	ENTRY( 8, 24 ) \
	ENTRY( 9, 25 ) \
	ENTRY( 10, 26 ) \
	ENTRY( 11, 27 ) \
	ENTRY( 12, 28 ) \
	ENTRY( 13, 29 ) \
	ENTRY( 14, 30 ) \
	ENTRY( 15, 31 )

/*****************************************************************************/
/** DATATYPES ****************************************************************/
/*****************************************************************************/

/*****************************************************************************/
/** FUNCTION DECLARATIONS ****************************************************/
/*****************************************************************************/

/*****************************************************************************/
/** LOCAL VARIABLES **********************************************************/
/*****************************************************************************/

static uint16_t mbs_input_regs[ MBS_INPUT_REGS_CNT ];
static uint16_t mbs_hold_regs[ MBS_HOLDING_REGS_CNT ]; 
static uint16_t mbm_hold_regs[ MAX_DEVICE_BUFFER_COUNT ][ MBM_HOLDING_REGS_CNT ]; 

/*****************************************************************************/
/** GLOBAL VARIABLES *********************************************************/
/*****************************************************************************/

const DataBlock_st MBSDB = {
	.DInputs = NULL,
	.DInputsCnt = 0,
	.DInputsStart = 0,

	.Coils = NULL,
	.CoilsCnt = 0,
	.CoilsStart = 0,

	.Inputs = mbs_input_regs,
	.InputsCnt = MBS_INPUT_REGS_CNT,
	.InputsStart = MBS_INPUT_START,

	.Holdings = mbs_hold_regs,
	.HoldingsCnt = MBS_HOLDING_REGS_CNT,
	.HoldingsStart = MBS_HOLDING_START,
};

const DataBlock_st MBM_READ_DB[ MAX_DEVICE_COUNT ] = {
	MBM_READ_DB_MAP( MBM_DB_ENTRY )
};

const DataBlock_st MBM_WRITE_DB[ MAX_DEVICE_COUNT ] = {
	MBM_WRITE_DB_MAP( MBM_DB_ENTRY )
};
/*****************************************************************************/
/** FUNCTION DEFINITIONS *****************************************************/
/*****************************************************************************/



