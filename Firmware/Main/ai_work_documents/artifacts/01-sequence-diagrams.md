# Sequence Diagrams

## 목적

이 문서는 현재 코드 기준으로 IF_main 통신 애플리케이션의 주요 실행 흐름을 Mermaid 시퀀스 다이어그램으로 정리한다.

## 기준 코드

- `Core/Src/main.c`
- `Core/Src/freertos.c`
- `Core/Src/stm32l4xx_it.c`
- `User/Application/Src/a_application.c`
- `User/Application/Src/a_uart.c`
- `User/Application/Src/m_modbus.c`
- `User/Application/Src/a_modbus_slave.c`
- `User/Application/Src/a_modbus_master.c`
- `User/Application/Src/a_gst_protocol.c`
- `User/Application/Src/a_poll_table.c`
- `User/Application/Src/a_shared_map.c`
- `User/Application/Src/a_comm_config.c`

## 전체 부팅 및 Task 생성 흐름

```mermaid
sequenceDiagram
    autonumber
    title IF_main boot and RTOS task startup
    participant Main as main()
    participant HAL as HAL/CubeMX Init
    participant Kernel as osKernel
    participant AppInit as Application_Init()
    participant Config as CommConfig
    participant Map as SharedMap
    participant Uart as a_uart
    participant MasterCtx as ModbusMaster contexts
    participant GstCtx as GstProtocol context
    participant AppCreate as Application_CreateTasks()
    participant Tasks as FreeRTOS tasks

    Main->>HAL: HAL_Init() / SystemClock_Config()
    Main->>HAL: MX_GPIO/DMA/CAN1/SPI3/UART4/USART1/USART2/USART3/LPUART1/USB/TIM6_Init()
    Main->>Kernel: osKernelInitialize()
    Main->>Tasks: osThreadNew(StartDefaultTask)
    Note right of Tasks: defaultTask는 MODULE_RUN GPIO set 후<br/>osDelay(1000) 무한 루프만 수행 (실질 로직 없음)

    Main->>AppInit: Application_Init()
    AppInit->>Config: CommConfig_InitDefaults()
    AppInit->>Map: SharedMap_Init()
    AppInit->>Map: SharedMap_SetCurrentTimeMs(HAL_GetTick())
    AppInit->>AppInit: s_uart4_last_rx_ms=now, s_uart4_watchdog_latched=false
    AppInit->>AppInit: Uart4/Lpuart1/Usart1/Usart2/Usart3_SetRxMode()
    AppInit->>Uart: Uart_Init()
    Note right of Uart: 포트별 Uart_ClearData() + Uart_StartReceive()<br/>(HAL_UARTEx_ReceiveToIdle_DMA)

    AppInit->>AppInit: osMutexNew(s_uart4_tx_mutex)
    alt mutex == NULL
        AppInit->>HAL: Error_Handler()
    end

    AppInit->>MasterCtx: ReinitDioContext()/ReinitHeatingContext()/ReinitGasContext()
    Note right of MasterCtx: 내부적으로 ModbusMaster_InitContext()<br/>(LPUART1→DIO, USART2→Heating, USART3→Gas)
    AppInit->>MasterCtx: ModbusMaster_InitQueues()
    alt 큐 생성 실패
        AppInit->>HAL: Error_Handler()
    end

    AppInit->>GstCtx: GstProtocol_InitContext(&s_gst_ctx, &huart1, usart1_period_ms, TxMode, RxMode)
    AppInit->>GstCtx: GstProtocol_SetAckCallback(Uart4_Transmit)
    GstCtx-->>AppInit: GstProtocol_IsReady()
    alt not ready
        AppInit->>HAL: Error_Handler()
    end

    Main->>AppCreate: Application_CreateTasks()
    AppCreate->>Tasks: osThreadNew(UART4_Task)
    AppCreate->>Tasks: osThreadNew(LPUART1_Task)
    AppCreate->>Tasks: osThreadNew(USART2_Task)
    AppCreate->>Tasks: osThreadNew(USART3_Task)
    AppCreate->>Tasks: osThreadNew(USART1_RxTask)
    AppCreate->>Tasks: osThreadNew(USART1_ProtocolTask)
    AppCreate->>Tasks: osThreadNew(USART1_TxTask)

    alt any task handle is NULL
        AppCreate-->>Main: false
        Main->>HAL: Error_Handler()
    else all task handles valid
        AppCreate-->>Main: true
        Main->>Kernel: osKernelStart()
    end
```

## 전체 Runtime 구성

```mermaid
sequenceDiagram
    autonumber
    title IF_main runtime task overview
    actor PLC
    participant UART4 as UART4_Task
    participant Slave as ModbusSlave
    participant Map as SharedMap
    participant Master as LPUART1/USART2/USART3_Task<br/>(TxStep/RxStep/ProcessStep)
    participant GstRx as USART1_RxTask
    participant GstProtocol as USART1_ProtocolTask
    participant GstTx as USART1_TxTask
    participant GstPeer as GST peer

    PLC->>UART4: Modbus RTU request
    UART4->>Slave: ModbusSlave_HandleRequest()
    alt read
        Slave->>Map: SharedMap_Read()/SharedMap_ReadGlobal()
        Slave-->>UART4: RESPONSE_READY
        UART4-->>PLC: 즉시 응답
    else write
        Slave->>Map: SharedMap_SetPendingWrite(Global)
        Slave-->>UART4: WRITE_PENDING
        UART4->>Map: ModbusSlave_ProcessWriteRequest() → 즉시 commit
        UART4-->>PLC: 즉시 ACK (하위 반영 대기 안 함)
        alt 일반 슬레이브 주소
            UART4->>Master: ModbusMaster_ForwardWrite() → tx_queue (best-effort)
        else GST global 영역(201~322)
            UART4->>GstProtocol: GstProtocol_ForwardWrite() (best-effort)
        end
    else 미정의/미허용 요청
        UART4-->>PLC: no response
    end

    loop every 1 ms
        Master->>Master: TxStep(ctx) → RxStep(ctx) → ProcessStep(ctx)
        Master->>Map: 폴 결과 갱신 / SetCommStatus / SetSlaveStatus
    end

    loop USART1 GST, every 1 ms
        GstRx->>GstRx: GstProtocol_RxStep() (frame 재조립 → rx_queue)
        GstProtocol->>Map: 242~282 반영(ApplyReadFrame) / 201~241 조회(BuildWriteFrame)
        GstProtocol->>GstTx: tx_queue에 GST frame enqueue
        GstTx->>GstPeer: GST write frame 송신
        GstPeer-->>GstRx: GST read frame 수신
    end
```

## UART4 PLC Read 흐름

```mermaid
sequenceDiagram
    autonumber
    title UART4 PLC read sequence
    actor PLC
    participant UartRx as UART4 DMA/IDLE
    participant UART4 as UART4_Task
    participant Slave as ModbusSlave_HandleRequest()
    participant Map as SharedMap
    participant UartTx as UART4 TX

    PLC->>UartRx: Modbus read frame
    UartRx->>UART4: Uart_GetData()
    UART4->>Slave: ModbusSlave_HandleRequest(frame)
    Slave->>Slave: Modbus_VerifyCrc() + IsValidSlaveId(<=247)

    alt CRC 오류 또는 잘못된 ID
        Slave-->>UART4: NO_RESPONSE
        UART4-->>PLC: no response
    else 유효 요청
        Slave->>Slave: GetReadTable(function), quantity in (0,124]

        alt FC03이고 주소범위가 global 영역(config 110~125 / slave-status 131~145 / GST 199~322) 안에 완전히 포함
            Slave->>Map: SharedMap_ReadGlobal(table, address) × quantity
        else 다른 함수코드인데 global 영역과 주소가 겹침
            Slave-->>UART4: NO_RESPONSE
            UART4-->>PLC: no response
        else 일반 슬레이브 주소
            Slave->>Map: SharedMap_Read(id, table, address) × quantity
        end

        alt 모든 주소가 존재하고 readable
            Map-->>Slave: values[]
            Slave->>Slave: Modbus_BuildReadResponse()<br/>(FC01/02 bit-pack, FC03/04 word-pack) + CRC
            Slave->>Map: SharedMap_IncrementPlcHeartbeat() (HR200++)
            Slave-->>UART4: RESPONSE_READY
            UART4->>UartTx: Uart4_Transmit(response)
            UartTx-->>PLC: Modbus read response
        else entry 없음 또는 읽기 불가
            Slave-->>UART4: NO_RESPONSE
            UART4-->>PLC: no response
        end
    end
```

## UART4 PLC Write → 하위 Modbus 장치 (즉시 ACK + 비동기 전달)

```mermaid
sequenceDiagram
    autonumber
    title UART4 PLC write to lower Modbus device (immediate ACK, best-effort forward)
    actor PLC
    participant UART4 as UART4_Task
    participant Slave as ModbusSlave
    participant Map as SharedMap
    participant TxQ as 포트별 tx_queue
    participant LowerTask as LPUART1/USART2/USART3_Task
    participant Device as 하위 Modbus 장치

    PLC->>UART4: Modbus write frame (일반 슬레이브 주소)
    UART4->>Slave: ModbusSlave_HandleRequest(frame)
    Slave->>Slave: ValidateWriteRange()<br/>(entry writable? + HasWriteItem()/CommConfig_IsActiveId())

    alt 검증 실패 (미기록 entry, 비활성 ID, poll-table에 없는 주소)
        Slave-->>UART4: NO_RESPONSE
        UART4-->>PLC: no response
    else 검증 통과
        Slave->>Slave: StorePendingHeader() + 값 스테이징
        Slave->>Slave: Modbus_BuildWriteSingleAck()/BuildWriteMultipleAck()<br/>(ACK 프레임을 commit 전에 미리 생성)
        Slave->>Map: SetPendingRange() → SharedMap_SetPendingWrite() (dirty=1)
        Slave-->>UART4: WRITE_PENDING(pending)

        UART4->>Map: ModbusSlave_ProcessWriteRequest(pending, success=true)
        Map->>Map: CommitPendingRange() → SharedMap_CommitWrite()<br/>(value=pending_value, dirty=0)
        Map-->>UART4: ack_frame
        UART4->>Map: SharedMap_IncrementPlcHeartbeat()
        UART4-->>PLC: Modbus write ACK (즉시 전송)

        Note over UART4,Device: PLC는 이미 ACK를 받았고,<br/>이하 하위 전달 성공 여부는 PLC ACK에 영향 없음 (fire-and-forget)
        UART4->>TxQ: ModbusMaster_ForwardWrite(pending) → osMessageQueuePut(tx_queue)

        LowerTask->>TxQ: TxStep() → osMessageQueueGet(tx_queue, timeout=0)
        alt 이 포트 대상 write
            TxQ->>LowerTask: write_req
            LowerTask->>LowerTask: ProcessPendingAttempt()<br/>(SelectWriteFunction + BuildWriteRequest)
            LowerTask->>Device: TransmitOnly(write frame) (응답 대기 없음)
            alt 송신 성공
                LowerTask->>Map: SharedMap_SetCommStatus(group, true)
            else 송신 실패
                LowerTask->>Map: SharedMap_SetCommStatus(group, false)
            end
        else 다른 포트 대상 (드묾)
            TxQ->>LowerTask: write_req
            LowerTask->>LowerTask: ModbusSlave_ProcessWriteRequest(dummy, false, ...) (drop/no-op)
        end
    end
```

## UART4 PLC Write → USART1 GST Global 영역 (즉시 ACK + best-effort GST 전달)

```mermaid
sequenceDiagram
    autonumber
    title UART4 PLC write to USART1 GST global map
    actor PLC
    participant UART4 as UART4_Task
    participant Slave as ModbusSlave
    participant Map as SharedMap
    participant GstProtocol as USART1_ProtocolTask (GstProtocol_ForwardWrite)
    participant TxQ as GST tx_queue
    participant GstTx as USART1_TxTask
    participant Peer as GST peer

    PLC->>UART4: Modbus write to 201~241 또는 283~322 (global holding)
    UART4->>Slave: ModbusSlave_HandleRequest(frame)
    Slave->>Slave: ValidateWriteRange() (global: writable 여부만 확인, active-id 체크 생략)
    Slave->>Map: SharedMap_SetPendingWriteGlobal()
    Slave-->>UART4: WRITE_PENDING(is_global=1)

    UART4->>Map: ModbusSlave_ProcessWriteRequest(pending, true) → CommitPendingRange()
    UART4-->>PLC: Modbus write ACK (즉시 전송, 아래는 이후 best-effort 처리)

    alt IsGstForwardRange (201~241 또는 283~322)
        UART4->>GstProtocol: GstProtocol_ForwardWrite(gst_req)
        alt 주소가 283~322 (예약 영역)
            GstProtocol->>GstProtocol: CompleteModbusPending(success)<br/>(로컬 즉시 완료, 실제 GST frame 송신 없음)
        else 주소가 201~241 (GST write 영역)
            GstProtocol->>GstProtocol: pending_active = 1 로 스테이징
            Note over GstProtocol: 이후 ServicePendingTransfer()가 처리
            GstProtocol->>Map: SharedMap_ReadGlobal(201~241) + PendingOverlaysAddress() 오버레이
            GstProtocol->>GstProtocol: BuildWriteFrame() (little-endian Lo,Hi)
            GstProtocol->>TxQ: enqueue(frame, expect_response=true)
            GstTx->>TxQ: osMessageQueueGet(wait forever)
            GstTx->>Peer: HAL_UART_Transmit(GST write frame)
            Peer-->>GstProtocol: GST read response frame (RxStep 재조립 → rx_queue)
            alt 응답 유효 (timeout 이전)
                GstProtocol->>GstProtocol: CompletePendingTransfer(success)
                GstProtocol->>Map: SharedMap_SetCommStatus(GST, true)
            else timeout 또는 응답 무효
                GstProtocol->>GstProtocol: CompletePendingTransfer(fail) 후 재시도
                GstProtocol->>Map: SharedMap_SetCommStatus(GST, false)
            end
        end
    end
```

## 하위 Modbus Master Periodic Polling (TxStep/RxStep/ProcessStep)

```mermaid
sequenceDiagram
    autonumber
    title Lower Modbus master 3-step polling sequence
    participant Task as LPUART1/USART2/USART3_Task
    participant Master as ModbusMaster
    participant Config as CommConfig
    participant PollTable as PollTable
    participant Device as 하위 장치
    participant Map as SharedMap

    loop every 1 ms
        Task->>Master: TxStep(ctx)
        alt waiting_response 중
            Master->>Master: timeout 경과? → RetryOrGivePoll()
        else 대기 중 아님
            Master->>Master: osMessageQueueGet(tx_queue, timeout=0) (non-blocking)
            alt write 항목 dequeue됨
                Master->>Master: write 처리 (상세는 write 흐름 다이어그램 참조), 이번 tick polling 생략
            else write 없음, poll 주기 도래
                Master->>Config: CommConfig_GetActiveIds(group)
                Master->>Master: discovered_mask로 미탐지 active_id_index skip
                Master->>PollTable: PollTable_GetPollItems(profile)[poll_item_index]
                Master->>Device: SendPollRequest() (TX 후 waiting_response=1)
            end
        end

        Task->>Master: RxStep(ctx)
        alt waiting_response 중
            Master->>Master: Uart_GetData() → osMessageQueuePut(rx_queue)
        end

        Task->>Master: ProcessStep(ctx)
        alt waiting_response 중이고 rx_queue에 메시지 있음
            Master->>Master: ParseReadResponse(frame)
            alt 파싱 성공
                Master->>Map: SharedMap_UpdateFromPoll() (FC01/02 bit-unpack, FC03/04 word)
                Master->>Map: SharedMap_SetCommStatus(group, true) + SetSlaveStatus(...)
                Master->>Master: AdvancePollCursor()<br/>(poll item 소진 시 다음 discovered 활성 ID로 rollover)
            else 파싱 실패
                Master->>Master: RetryOrGivePoll() (재시도 또는 실패 → SetCommStatus(false))
            end
        end
    end
```

## Slave Discovery 흐름

```mermaid
sequenceDiagram
    autonumber
    title Modbus slave discovery (port task 시작 시 1회)
    participant Task as LPUART1/USART2/USART3_Task
    participant Master as ModbusMaster_DiscoverSlaves()
    participant Config as CommConfig
    participant Device as 하위 장치
    participant Map as SharedMap

    Task->>Master: ModbusMaster_DiscoverSlaves(ctx)
    Master->>Config: CommConfig_GetActiveIds(group)

    loop 각 active ID
        Master->>Device: presence probe<br/>(DIO/Heating: FC03 addr=0 / Gas: FC04 addr=30000(H2) 후 addr=1(TFT))
        alt 응답 있음 (최대 5회 재시도 이내)
            Device-->>Master: probe 응답
            Master->>Master: discovered_mask bit set<br/>(Gas: discovered_profiles[id] = H2 또는 H2_TFT)
        else 5회 재시도 후 무응답
            Master->>Map: SharedMap_SetSlaveStatus(group, index, false) (offline)
            Master->>Master: polling 대상에서 제외 (mask bit 0 유지)
        end
    end

    Master->>Master: 발견된 ID 오름차순 정렬
    loop 발견된 ID (오름차순)
        Master->>Map: SharedMap_RegisterSlave(id, group) (실제 ID 기준 entry 동적 등록)
    end
    Master->>Master: discovery_done = true
```

## UART4 PLC Watchdog Fail-safe 흐름

```mermaid
sequenceDiagram
    autonumber
    title UART4 PLC watchdog fail-safe (DIO output zero on PLC silence)
    participant UART4 as UART4_Task
    participant Watchdog as ServiceUart4Watchdog()
    participant Config as CommConfig
    participant Map as SharedMap
    participant Master as ModbusMaster (tx_queue)

    loop every 1 ms
        UART4->>UART4: ProcessUart4Request()
        alt 유효 요청 처리됨
            UART4->>UART4: s_uart4_last_rx_ms = now, s_uart4_watchdog_latched = false
        end

        UART4->>Watchdog: ServiceUart4Watchdog()
        alt (now - last_rx_ms) >= APP_UART4_WATCHDOG_TIMEOUT_MS(기본 1000ms) 이고 아직 latch 안 됨
            Watchdog->>Config: CommConfig_GetActiveIds(DIO)
            loop 활성 DIO ID 전체
                Watchdog->>Map: SharedMap_SetPendingWrite(HR129, 0) + SharedMap_CommitWrite()
                Watchdog->>Master: ModbusMaster_ForwardWrite(zero-output request) (tx_queue)
            end
            Watchdog->>Watchdog: s_uart4_watchdog_latched = true (재알림 방지, PLC 통신 재개 시까지)
        end
    end
```

## 런타임 Config Apply 흐름

```mermaid
sequenceDiagram
    autonumber
    title Runtime CommConfig apply via holding registers 121/122
    participant UART4 as UART4_Task
    participant ConfigApply as ServiceConfigApply()
    participant Map as SharedMap
    participant Config as CommConfig

    loop every 1 ms (ProcessUart4Request 이후)
        UART4->>ConfigApply: ServiceConfigApply()
        ConfigApply->>Map: SharedMap_ReadGlobal(HR121, trigger)
        alt trigger == 1
            ConfigApply->>Map: DIO/Heating/Gas ID + gas-type holding 블록 읽기
            ConfigApply->>Config: CommConfig_ApplyDeviceSettings(dio_ids, heating_ids, gas_ids, gas_types)
            alt 검증 통과 (유효 ID, 중복 없음, 유효 gas type)
                Config->>Config: BuildActiveList() 갱신 + revision++
                Config-->>ConfigApply: COMM_CONFIG_RESULT_OK
            else 검증 실패
                Config-->>ConfigApply: INVALID_ID / DUPLICATE_ID / INVALID_GAS_TYPE / INTERNAL_ERROR
            end
            ConfigApply->>Map: SharedMap_WriteGlobal(HR122 = result code)
            ConfigApply->>Map: SharedMap_WriteGlobal(HR121 = 0, trigger clear)
        end
    end

    Note over Config: 하위 포트 task는 CommConfig_GetRevision() 변경을 감지하면<br/>ReinitXxxContext() + ModbusMaster_DiscoverSlaves()를 재실행한다.
```

> 참고: `documents/04-data-model.md`(DATA-005)는 "Flash 저장과 PLC register 기반 설정 변경은 초기 구현 범위에서 제외한다"고 기술하고 있으나, 현재 코드에는 HR121/122를 통한 런타임 config apply 메커니즘(`ServiceConfigApply`/`CommConfig_ApplyDeviceSettings`)이 이미 구현되어 있다. 이 다이어그램은 실제 코드 동작을 기준으로 작성했으며, `documents/` 쪽 문서 갱신 여부는 별도로 판단이 필요하다.

## USART1 GST 3-Task 흐름

```mermaid
sequenceDiagram
    autonumber
    title USART1 GST RX / Protocol / TX sequence
    participant Peer as GST peer
    participant UartRx as USART1 DMA/IDLE
    participant RxTask as USART1_RxTask
    participant RxQueue as GST RX Queue
    participant ProtocolTask as USART1_ProtocolTask
    participant Map as SharedMap
    participant TxQueue as GST TX Queue
    participant TxTask as USART1_TxTask

    rect rgb(245, 245, 245)
        Note over Peer,RxQueue: RX path
        Peer->>UartRx: GST byte stream
        RxTask->>UartRx: Uart_GetData() (원시 fragment)
        RxTask->>RxTask: reassembly_buf에 재조립<br/>(GST_HEADER_0까지 garbage skip, 200ms 재조립 timeout)
        alt 프레임 전체 길이 확보
            alt rx_queue 여유 있음
                RxTask->>RxQueue: osMessageQueuePut(frame)
            else queue full
                RxTask->>RxQueue: 오래된 frame 버리고 최신 frame 기준 재동기화
            end
        end
    end

    rect rgb(245, 245, 245)
        Note over ProtocolTask,TxQueue: Protocol path
        ProtocolTask->>RxQueue: osMessageQueueGet() (dequeue)
        alt ValidateFrame 통과 (header/tail/CRC, FC∈{0x01,0x02})
            ProtocolTask->>Map: ApplyReadFrame() → SharedMap_WriteGlobal(242~282)
            ProtocolTask->>Map: SharedMap_SetCommStatus(GST, true)
            alt 응답 대기 중인 forced write 있음
                ProtocolTask->>ProtocolTask: CompletePendingTransfer(success)
            end
        else 프레임 무효/누락
            ProtocolTask->>Map: SharedMap_SetCommStatus(GST, false) (연속 실패 누적 시)
        end

        ProtocolTask->>ProtocolTask: ServicePendingTransfer() (forced write 재시도/timeout)
        ProtocolTask->>ProtocolTask: ServicePeriodicTransfer()
        alt 주기 도래 and pending_active 없음
            ProtocolTask->>Map: SharedMap_ReadGlobal(201~241) × 41
            ProtocolTask->>ProtocolTask: BuildWriteFrame() (little-endian Lo,Hi payload + CRC)
            ProtocolTask->>TxQueue: enqueue(frame, expect_response=false)
        end
        ProtocolTask->>ProtocolTask: ServiceLinkTimeout()
    end

    rect rgb(245, 245, 245)
        Note over TxQueue,Peer: TX path (osDelay 없이 큐 대기)
        TxTask->>TxQueue: osMessageQueueGet(osWaitForever)
        TxTask->>Peer: HAL_UART_Transmit(GST frame)<br/>(RS422: DE 유지, RX DMA 유지)
        alt 송신 실패
            TxTask->>TxTask: 짧은 delay 후 GetRetryCount() 만큼 재시도
        else 송신 성공, 응답 기대
            TxTask->>ProtocolTask: pending_waiting_response=1, pending_started_ms=now
        end
    end
```

## 공통 UART 수신 흐름

```mermaid
sequenceDiagram
    autonumber
    title Common UART DMA/IDLE receive sequence
    participant Task as Task
    participant UartCtx as a_uart context
    participant HAL as HAL UART/DMA
    participant Callback as HAL_UARTEx_RxEventCallback()

    Task->>UartCtx: Uart_StartReceive(huart)
    UartCtx->>HAL: HAL_UART_DMAStop() + HAL_UARTEx_ReceiveToIdle_DMA(dma_buffer)
    UartCtx->>HAL: __HAL_DMA_DISABLE_IT(DMA_IT_HT) (half-transfer IRQ 비활성)

    HAL->>Callback: RX event(huart, Size)
    Callback->>UartCtx: StoreFrame(ctx, Size) [critical section]
    UartCtx->>UartCtx: 신규 수신 구간 계산 (old_pos..Size, DMA circular wraparound 처리)
    alt ring buffer(depth 2) 여유 있음
        UartCtx->>UartCtx: frame push, old_pos=Size
    else ring 가득 참
        UartCtx->>UartCtx: 가장 오래된 슬롯 덮어쓰기, dropped_frames++
    end

    Task->>UartCtx: Uart_GetData() [critical section, non-blocking]
    alt frame 있음
        UartCtx-->>Task: frame data + length
    else 없음
        UartCtx-->>Task: false
    end

    alt UART 에러(framing/noise/overrun 등)
        HAL->>Callback: HAL_UART_ErrorCallback(huart)
        Callback->>UartCtx: Uart_StartReceive(huart)
        Note right of UartCtx: DMA만 재시작(old_pos/dma_buffer 초기화),<br/>이미 큐잉된 frames[]는 유지됨
    end
```

## 변경 이력

- 2026-06-28: 현재 코드 기준 전체 통신 흐름 시퀀스 다이어그램 작성.
- 2026-06-28: Mermaid `sequenceDiagram` 형식으로 변경.
- 2026-07-06: 현재 코드 기준(즉시 ACK + 비동기 전달, TxStep/RxStep/ProcessStep 3단계, Slave Discovery, UART4 watchdog fail-safe, 런타임 Config Apply)으로 전면 갱신. 기존 문서의 blocking-write/단일 `ModbusMaster_Step` 표현을 실제 코드와 일치하도록 교체.
