#include "bootloader_shared.h"

uint32_t BootShared_Crc32( const uint8_t *data, uint32_t length )
{
  uint32_t crc = 0xFFFFFFFFUL;
  uint32_t i;
  uint32_t bit;

  for( i = 0U; i < length; i++ )
  {
    crc ^= ( uint32_t )data[ i ];
    for( bit = 0U; bit < 8U; bit++ )
    {
      if( ( crc & 1U ) != 0U )
      {
        crc = ( crc >> 1 ) ^ 0xEDB88320UL;
      }
      else
      {
        crc = crc >> 1;
      }
    }
  }
  return crc ^ 0xFFFFFFFFUL;
}

uint16_t BootShared_Crc16( const uint8_t *data, uint32_t length )
{
  /* Modbus CRC16 (poly 0xA001), 기존 m_modbus.c와 동일 다항식 재사용 */
  uint16_t crc = 0xFFFFU;
  uint32_t i;
  uint32_t bit;

  for( i = 0U; i < length; i++ )
  {
    crc ^= ( uint16_t )data[ i ];
    for( bit = 0U; bit < 8U; bit++ )
    {
      if( ( crc & 1U ) != 0U )
      {
        crc = ( uint16_t )( ( crc >> 1 ) ^ 0xA001U );
      }
      else
      {
        crc = ( uint16_t )( crc >> 1 );
      }
    }
  }
  return crc;
}
