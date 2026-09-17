#include "a_test_link.h"
#include "main.h"
#include "usbd_cdc_if.h"
#include <stdio.h>
#include <string.h>

#define TEST_LINK_RX_SIZE (128U)
#define TEST_LINK_LINE_SIZE (32U)
#define TEST_LINK_FAULT_TIMEOUT_MS (1500U)

static uint8_t s_rx_buffer[ TEST_LINK_RX_SIZE ];
static volatile uint32_t s_rx_head;
static volatile uint32_t s_rx_tail;
static char s_line[ TEST_LINK_LINE_SIZE ];
static uint32_t s_line_length;
static bool s_fault_override;
static GPIO_PinState s_fault_state;
static uint32_t s_fault_deadline;
static uint8_t s_tx_buffer[ TEST_LINK_LINE_SIZE ];

static void SendLine( const char *text )
{
  const uint16_t length = ( uint16_t )strlen( text );
  if( length < sizeof( s_tx_buffer ) )
  {
    ( void )memcpy( s_tx_buffer, text, length );
    ( void )CDC_Transmit_FS( s_tx_buffer, length );
  }
}

static bool PopByte( uint8_t *value )
{
  if( s_rx_head == s_rx_tail )
  {
    return false;
  }

  *value = s_rx_buffer[ s_rx_tail ];
  s_rx_tail = ( s_rx_tail + 1U ) % TEST_LINK_RX_SIZE;
  return true;
}

static void SetFaultState( GPIO_PinState state )
{
  s_fault_state = state;
  s_fault_override = true;
  s_fault_deadline = HAL_GetTick() + TEST_LINK_FAULT_TIMEOUT_MS;
  HAL_GPIO_WritePin( FAULT_GPIO_Port, FAULT_Pin, state );
}

static void ProcessLine( const char *line )
{
  if( strcmp( line, "IDENTIFY" ) == 0 )
  {
    SendLine( "DEVICE=MAIN\n" );
  }
  else if( strcmp( line, "GET_ROTARY" ) == 0 )
  {
    const uint32_t value = ( uint32_t )HAL_GPIO_ReadPin( CAN_ID4_GPIO_Port, CAN_ID4_Pin )
                         | ( ( uint32_t )HAL_GPIO_ReadPin( CAN_ID5_GPIO_Port, CAN_ID5_Pin ) << 1U )
                         | ( ( uint32_t )HAL_GPIO_ReadPin( CAN_ID6_GPIO_Port, CAN_ID6_Pin ) << 2U )
                         | ( ( uint32_t )HAL_GPIO_ReadPin( CAN_ID7_GPIO_Port, CAN_ID7_Pin ) << 3U );
    ( void )snprintf( ( char * )s_tx_buffer, sizeof( s_tx_buffer ), "ROTARY=%02lX\n", ( unsigned long )value );
    SendLine( ( char * )s_tx_buffer );
  }
  else if( strcmp( line, "FAULT OPEN" ) == 0 )
  {
    SetFaultState( GPIO_PIN_RESET );
    SendLine( "FAULT=OPEN\n" );
  }
  else if( strcmp( line, "FAULT CLOSE" ) == 0 )
  {
    SetFaultState( GPIO_PIN_SET );
    SendLine( "FAULT=CLOSE\n" );
  }
}

void TestLink_Init(void)
{
  s_rx_head = 0U;
  s_rx_tail = 0U;
  s_line_length = 0U;
  s_fault_override = false;
  s_fault_state = GPIO_PIN_RESET;
  s_fault_deadline = 0U;
}

void TestLink_OnUsbReceive(const uint8_t *data, uint32_t len)
{
  uint32_t index;
  for( index = 0U; index < len; index++ )
  {
    const uint32_t next_head = ( s_rx_head + 1U ) % TEST_LINK_RX_SIZE;
    if( next_head == s_rx_tail )
    {
      break;
    }
    s_rx_buffer[ s_rx_head ] = data[ index ];
    s_rx_head = next_head;
  }
}

void TestLink_Process(void)
{
  uint8_t byte;

  if( s_fault_override && ( int32_t )( HAL_GetTick() - s_fault_deadline ) >= 0 )
  {
    s_fault_override = false;
  }

  while( PopByte( &byte ) )
  {
    if( byte == '\n' || byte == '\r' )
    {
      if( s_line_length > 0U )
      {
        s_line[ s_line_length ] = '\0';
        ProcessLine( s_line );
        s_line_length = 0U;
      }
    }
    else if( s_line_length < ( TEST_LINK_LINE_SIZE - 1U ) )
    {
      s_line[ s_line_length++ ] = ( char )byte;
    }
    else
    {
      s_line_length = 0U;
    }
  }
}

bool TestLink_IsFaultOverrideActive(void)
{
  return s_fault_override;
}

GPIO_PinState TestLink_GetFaultState(void)
{
  return s_fault_state;
}
