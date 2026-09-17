# DIO USB TEST protocol

DIO uses USB CDC only for read-only production inspection. TestMode never writes DIO firmware and does not change Modbus, DI/DO, UART2, or output control.

## USB hardware

- MCU: STM32G0B1CBT6
- USB D+: PA12
- USB D-: PA11
- USB clock: HSI48 with CRS

## Commands

Commands and responses are ASCII lines terminated by `\n`.

- `IDENTIFY` -> `DEVICE=DIO`
- `GET_SWITCH` -> `SW1=<0|1>;SW2=<0|1>`
- `GET_FIRMWARE_INFO` -> `DEVICE=DIO;VERSION=<major>.<minor>.<patch>;SIZE=131072;CRC=<8 hex digits>`

`GET_SWITCH` reports the physical pin levels only:

| Field | Pin |
|---|---|
| SW1 | ID_SET1 / PA0 |
| SW2 | ID_SET2 / PC15 |

The PC switch screen converts these levels to ID 1-4. Board ID is not included in the firmware inspection response or firmware screen.

DIO firmware is programmed with ST-LINK during production. There are no USB erase, write, finalize, or reboot commands for DIO.
