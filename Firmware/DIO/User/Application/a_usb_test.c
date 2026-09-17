#include "a_usb_test.h"
#include "main.h"
#include "usbd_cdc_if.h"
#include <stdio.h>
#include <string.h>

#define DIO_USB_RX_SIZE       (128U)
#define DIO_USB_LINE_SIZE     (48U)
#define DIO_FLASH_BASE        (0x08000000UL)
#define DIO_FLASH_SIZE        (128UL * 1024UL)
#define DIO_FIRMWARE_VERSION  "1.0.0"

typedef struct
{
  char magic[8];
  char version[16];
  uint32_t flash_size;
  uint32_t reserved;
} DioFirmwareInfo;

__attribute__((section(".dio_firmware_info"), used))
const DioFirmwareInfo g_dio_firmware_info =
{
  "DIOFW01",
  DIO_FIRMWARE_VERSION,
  DIO_FLASH_SIZE,
  0xFFFFFFFFUL
};

static uint8_t s_rx[DIO_USB_RX_SIZE];
static volatile uint32_t s_rx_head;
static volatile uint32_t s_rx_tail;
static char s_line[DIO_USB_LINE_SIZE];
static uint32_t s_line_length;
static uint8_t s_tx[DIO_USB_LINE_SIZE + 32U];

static uint32_t CalculateFlashCrc32(void)
{
  const uint8_t *flash = (const uint8_t *)DIO_FLASH_BASE;
  uint32_t crc = 0xFFFFFFFFUL;

  for( uint32_t index = 0U; index < DIO_FLASH_SIZE; index++ )
  {
    crc ^= flash[index];
    for( uint32_t bit = 0U; bit < 8U; bit++ )
    {
      crc = ( crc & 1U ) != 0U ? ( crc >> 1U ) ^ 0xEDB88320UL : crc >> 1U;
    }
  }
  return crc ^ 0xFFFFFFFFUL;
}

static void SendLine(const char *text)
{
  const size_t length = strlen(text);
  if( length < sizeof(s_tx) )
  {
    if( text != (const char *)s_tx )
    {
      memcpy(s_tx, text, length);
    }
    (void)CDC_Transmit_FS(s_tx, (uint16_t)length);
  }
}

static void ProcessLine(const char *line)
{
  if( strcmp(line, "IDENTIFY") == 0 )
  {
    SendLine("DEVICE=DIO\n");
  }
  else if( strcmp(line, "GET_SWITCH") == 0 )
  {
    const uint32_t switch1 = HAL_GPIO_ReadPin(ID_SET1_GPIO_Port, ID_SET1_Pin) == GPIO_PIN_SET ? 1U : 0U;
    const uint32_t switch2 = HAL_GPIO_ReadPin(ID_SET2_GPIO_Port, ID_SET2_Pin) == GPIO_PIN_SET ? 1U : 0U;
    (void)snprintf((char *)s_tx, sizeof(s_tx), "SW1=%lu;SW2=%lu\n",
                   (unsigned long)switch1, (unsigned long)switch2);
    SendLine((char *)s_tx);
  }
  else if( strcmp(line, "GET_FIRMWARE_INFO") == 0 )
  {
    const uint32_t crc = CalculateFlashCrc32();
    (void)snprintf((char *)s_tx, sizeof(s_tx),
                   "DEVICE=DIO;VERSION=%s;SIZE=%lu;CRC=%08lX\n",
                   g_dio_firmware_info.version,
                   (unsigned long)g_dio_firmware_info.flash_size,
                   (unsigned long)crc);
    SendLine((char *)s_tx);
  }
}

void DioUsbTest_Init(void)
{
  s_rx_head = 0U;
  s_rx_tail = 0U;
  s_line_length = 0U;
}

void DioUsbTest_OnReceive(const uint8_t *data, uint32_t length)
{
  for( uint32_t index = 0U; index < length; index++ )
  {
    const uint32_t next = ( s_rx_head + 1U ) % DIO_USB_RX_SIZE;
    if( next == s_rx_tail )
    {
      break;
    }
    s_rx[s_rx_head] = data[index];
    s_rx_head = next;
  }
}

void DioUsbTest_Process(void)
{
  while( s_rx_tail != s_rx_head )
  {
    const uint8_t value = s_rx[s_rx_tail];
    s_rx_tail = ( s_rx_tail + 1U ) % DIO_USB_RX_SIZE;

    if( value == '\r' || value == '\n' )
    {
      if( s_line_length > 0U )
      {
        s_line[s_line_length] = '\0';
        ProcessLine(s_line);
        s_line_length = 0U;
      }
    }
    else if( s_line_length < ( DIO_USB_LINE_SIZE - 1U ) )
    {
      s_line[s_line_length++] = (char)value;
    }
    else
    {
      s_line_length = 0U;
    }
  }
}
