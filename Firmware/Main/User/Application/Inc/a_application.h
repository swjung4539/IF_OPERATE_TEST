#ifndef A_APPLICATION_H
#define A_APPLICATION_H

#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

void Application_Init(void);
bool Application_CreateTasks(void);
void UART4_Task(void *argument);
void LPUART1_Task(void *argument);
void USART2_Task(void *argument);
void USART3_Task(void *argument);
void USART1_RxTask(void *argument);
void USART1_ProtocolTask(void *argument);
void USART1_TxTask(void *argument);

#ifdef __cplusplus
}
#endif

#endif /* A_APPLICATION_H */
