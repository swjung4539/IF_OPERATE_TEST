# Firmware information protocol

TEST mode never writes firmware. It compares the selected HEX with the firmware already running on the board.

## MAIN

MAIN uses the existing MAIN bootloader. The PC enters bootloader mode, reads the active slot with `READ_FLASH`, compares the bytes, and reboots into the operational firmware. Firmware update is available only from the bootloader screen.

## DIO

DIO operational firmware provides a read-only USB CDC response:

```text
IDENTIFY
-> DEVICE=DIO

GET_FIRMWARE_INFO
-> DEVICE=DIO;VERSION=1.0.0;SIZE=131072;CRC=<8 hex digits>
```

The CRC is standard CRC-32 with polynomial `0xEDB88320`, initial value `0xFFFFFFFF`, and final XOR `0xFFFFFFFF`. It covers the full 128 KiB range `0x08000000` through `0x0801FFFF`. Addresses absent from the selected Intel HEX are normalized to `0xFF` before comparison.

Firmware metadata occupies 32 bytes at `0x0801FFE0`:

- magic: `DIOFW01`
- version: 16-byte ASCII field
- image size: 131072
- reserved: 4 bytes

The PC reports `최신 버전입니다.` only when version, size, and CRC all match. A different value reports `선택한 펌웨어와 다릅니다.`. Missing or invalid device responses report a check failure.

DIO board ID is excluded from this response and is shown only on the switch inspection screen.
