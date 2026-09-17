/*
 * [누가] IF 모듈의 Desired-State Mirror가
 * [언제] watchdog 복구용 상태를 읽고 쓸 때
 * [어디서] SPI3 및 PA15(MEM_CS3)에 연결된 AT25SF041B에서
 * [무엇을] NOR Flash의 read/program/erase를 수행한다.
 * [왜] MCU reset 뒤에도 마지막 Slave 목표 상태를 보존하기 위해서다.
 * [어떻게] 명령 전체 동안 CS를 유지하고, write/erase 완료를 WIP bit로 확인한다.
 */
#include "a_at25sf041b.h"

#include "main.h"

#define AT25_FLASH_SIZE_BYTES         ( 512U * 1024U )
#define AT25_PAGE_SIZE_BYTES          256U
#define AT25_SECTOR_SIZE_BYTES        4096U
#define AT25_SPI_TIMEOUT_MS           100U
#define AT25_PROGRAM_TIMEOUT_MS       20U
#define AT25_ERASE_TIMEOUT_MS         1000U

#define AT25_CMD_WRITE_ENABLE         0x06U
#define AT25_CMD_READ_STATUS          0x05U
#define AT25_CMD_READ_DATA            0x03U
#define AT25_CMD_PAGE_PROGRAM         0x02U
#define AT25_CMD_SECTOR_ERASE_4K      0x20U
#define AT25_CMD_JEDEC_ID             0x9FU
#define AT25_STATUS_WIP               0x01U

extern SPI_HandleTypeDef hspi3;

static void at25_select( void )
{
  HAL_GPIO_WritePin( MEM_CS3_GPIO_Port, MEM_CS3_Pin, GPIO_PIN_RESET );
}

static void at25_deselect( void )
{
  HAL_GPIO_WritePin( MEM_CS3_GPIO_Port, MEM_CS3_Pin, GPIO_PIN_SET );
}

static bool at25_address_is_valid( uint32_t address, size_t length )
{
  return ( address < AT25_FLASH_SIZE_BYTES ) && ( length <= ( size_t )( AT25_FLASH_SIZE_BYTES - address ) );
}

static bool at25_read_status( uint8_t *status )
{
  uint8_t command[ 2 ] = { AT25_CMD_READ_STATUS, 0xFFU };
  uint8_t response[ 2 ] = { 0U, 0U };

  if( status == NULL )
  {
    return false;
  }

  at25_select();
  if( HAL_SPI_TransmitReceive( &hspi3, command, response, sizeof( command ), AT25_SPI_TIMEOUT_MS ) != HAL_OK )
  {
    at25_deselect();
    return false;
  }
  at25_deselect();
  *status = response[ 1 ];
  return true;
}

static bool at25_wait_ready( uint32_t timeout_ms )
{
  const uint32_t started_ms = HAL_GetTick();
  uint8_t status;

  do
  {
    if( !at25_read_status( &status ) )
    {
      return false;
    }
    if( ( status & AT25_STATUS_WIP ) == 0U )
    {
      return true;
    }
  } while( ( HAL_GetTick() - started_ms ) < timeout_ms );

  return false;
}

static bool at25_write_enable( void )
{
  const uint8_t command = AT25_CMD_WRITE_ENABLE;

  at25_select();
  if( HAL_SPI_Transmit( &hspi3, ( uint8_t * )&command, 1U, AT25_SPI_TIMEOUT_MS ) != HAL_OK )
  {
    at25_deselect();
    return false;
  }
  at25_deselect();
  return true;
}

static void at25_build_address_command( uint8_t command, uint32_t address, uint8_t frame[ 4 ] )
{
  frame[ 0 ] = command;
  frame[ 1 ] = ( uint8_t )( address >> 16U );
  frame[ 2 ] = ( uint8_t )( address >> 8U );
  frame[ 3 ] = ( uint8_t )address;
}

bool At25Sf041b_Init( void )
{
  /* [누가] Application 초기화 코드가 부팅 직후 SPI3 Flash를 사용하기 전에 CS를 비선택 상태로 둔다.
   * [언제] 모든 SPI Flash 동작 전에, [어디서] PA15 MEM_CS3에서, [무엇을] High로 설정한다.
   * [왜] 부팅 중 우발적인 Flash 명령 수신을 막기 위해서며, [어떻게] GPIO write로 수행한다. */
  at25_deselect();
  return at25_wait_ready( AT25_PROGRAM_TIMEOUT_MS );
}

bool At25Sf041b_ReadJedecId( uint8_t id[ 3 ] )
{
  const uint8_t command = AT25_CMD_JEDEC_ID;

  if( id == NULL )
  {
    return false;
  }

  at25_select();
  if( ( HAL_SPI_Transmit( &hspi3, ( uint8_t * )&command, 1U, AT25_SPI_TIMEOUT_MS ) != HAL_OK )
      || ( HAL_SPI_Receive( &hspi3, id, 3U, AT25_SPI_TIMEOUT_MS ) != HAL_OK ) )
  {
    at25_deselect();
    return false;
  }
  at25_deselect();
  return true;
}

bool At25Sf041b_Read( uint32_t address, uint8_t *buffer, size_t length )
{
  uint8_t frame[ 4 ];

  if( ( buffer == NULL ) || ( length == 0U ) || !at25_address_is_valid( address, length ) )
  {
    return false;
  }

  at25_build_address_command( AT25_CMD_READ_DATA, address, frame );
  at25_select();
  if( ( HAL_SPI_Transmit( &hspi3, frame, sizeof( frame ), AT25_SPI_TIMEOUT_MS ) != HAL_OK )
      || ( HAL_SPI_Receive( &hspi3, buffer, ( uint16_t )length, AT25_SPI_TIMEOUT_MS ) != HAL_OK ) )
  {
    at25_deselect();
    return false;
  }
  at25_deselect();
  return true;
}

bool At25Sf041b_PageProgram( uint32_t address, const uint8_t *data, size_t length )
{
  uint8_t frame[ 4 ];
  size_t page_remaining;
  size_t write_length;

  if( ( data == NULL ) || ( length == 0U ) || !at25_address_is_valid( address, length ) )
  {
    return false;
  }

  while( length > 0U )
  {
    page_remaining = AT25_PAGE_SIZE_BYTES - ( address % AT25_PAGE_SIZE_BYTES );
    write_length = ( length < page_remaining ) ? length : page_remaining;
    if( !at25_write_enable() )
    {
      return false;
    }

    at25_build_address_command( AT25_CMD_PAGE_PROGRAM, address, frame );
    at25_select();
    if( ( HAL_SPI_Transmit( &hspi3, frame, sizeof( frame ), AT25_SPI_TIMEOUT_MS ) != HAL_OK )
        || ( HAL_SPI_Transmit( &hspi3, ( uint8_t * )data, ( uint16_t )write_length, AT25_SPI_TIMEOUT_MS ) != HAL_OK ) )
    {
      at25_deselect();
      return false;
    }
    at25_deselect();
    if( !at25_wait_ready( AT25_PROGRAM_TIMEOUT_MS ) )
    {
      return false;
    }
    address += ( uint32_t )write_length;
    data += write_length;
    length -= write_length;
  }

  return true;
}

bool At25Sf041b_Erase4K( uint32_t address )
{
  uint8_t frame[ 4 ];

  if( ( address % AT25_SECTOR_SIZE_BYTES ) != 0U || !at25_address_is_valid( address, AT25_SECTOR_SIZE_BYTES ) )
  {
    return false;
  }
  if( !at25_write_enable() )
  {
    return false;
  }

  at25_build_address_command( AT25_CMD_SECTOR_ERASE_4K, address, frame );
  at25_select();
  if( HAL_SPI_Transmit( &hspi3, frame, sizeof( frame ), AT25_SPI_TIMEOUT_MS ) != HAL_OK )
  {
    at25_deselect();
    return false;
  }
  at25_deselect();
  return at25_wait_ready( AT25_ERASE_TIMEOUT_MS );
}
