using System.IO.Compression;
using System.IO.Ports;
using System.Security.Cryptography;
using System.Xml.Linq;
using OperateTest.App.Models;
using OperateTest.App.Services;
using OperateTest.App.Services.StLink;
using OperateTest.App.Services.Gst;
using OperateTest.App.Services.Modbus;
using OperateTest.App.Services.DioInspection;
using OperateTest.App.Services.ModbusSlave;
using OperateTest.App.Forms;
using TestEmulator.Controls;
using TestEmulator.Theme;

var verifier = new FlowVerifier();
return verifier.Run();

internal sealed class FlowVerifier
{
    private readonly InspectionWorkbookService _workbooks = new();
    private readonly InspectionCheckpointService _checkpoints = new();
    private readonly List<string> _passed = [];
    private string _root = string.Empty;
    private string _dioTemplate = string.Empty;
    private string _commTemplate = string.Empty;

    public int Run()
    {
        _root = Path.Combine(Path.GetTempPath(), "OperateTestFlowVerification", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        try
        {
            _dioTemplate = Path.Combine(_root, "dio-template.xlsx");
            _commTemplate = Path.Combine(_root, "comm-template.xlsx");
            WorkbookFixture.Create(_dioTemplate, InspectionBoardType.DigitalIo);
            WorkbookFixture.Create(_commTemplate, InspectionBoardType.Communication);

            VerifyProtocolContract();
            VerifyProtocolFaultInjection();
            VerifyWorkbookProfiles();
            VerifyWorkflowStateRules();
            VerifyPowerCriteriaBoundaries();
            VerifySafetyOperationRules();
            VerifyResponsiveUiContract();
            VerifyTenBoardsAndThirtyRowLimit();
            VerifyCheckpointResume();
            VerifyTraceabilityAndCheckpointRecovery();
            VerifyNewDocumentAndBoardSeparation();
            VerifyLockedExcelIsNotDamaged();

            Console.WriteLine($"FLOW VERIFICATION PASS ({_passed.Count} checks)");
            foreach (var item in _passed) Console.WriteLine($"  PASS  {item}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FLOW VERIFICATION FAILED");
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            try { Directory.Delete(_root, true); } catch { }
        }
    }

    private void VerifyWorkflowStateRules()
    {
        var session = new InspectionSession(
            InspectionBoardType.DigitalIo, "template.xlsx", "result.xlsx",
            new DateTime(2026, 10, 12), "흐름검증", "DIO-2610-999", 6);

        var disconnected = InspectionWorkflowService.Evaluate(session, false, "MAIN UART4 연결 필요");
        Equal(5, disconnected.Steps.Count, "검사 흐름 5단계 고정");
        Equal(WorkflowStageState.Current, disconnected.Steps[1].State, "포트 미연결 시 장치 연결 단계 표시");
        True(disconnected.NextAction.Contains("통신 포트", StringComparison.Ordinal), "포트 미연결 다음 행동 안내");

        var connected = InspectionWorkflowService.Evaluate(session, true, "MAIN UART4 포트 연결");
        Equal(WorkflowStageState.Current, connected.Steps[2].State, "포트 연결 후 검사 수행 단계 표시");

        session.Results.ManualStatus = InspectionExecutionStatus.Pass;
        session.Results.DioLoopbackStatus = InspectionExecutionStatus.Error;
        session.Results.DipStatus = InspectionExecutionStatus.NotRun;
        session.Results.DioSafetyStatus = DioSafetyStatus.NotChecked;
        var error = InspectionWorkflowService.Evaluate(session, true, "MAIN UART4 포트 연결");
        Equal(WorkflowStageState.Attention, error.Steps[2].State, "검사 오류를 확인 필요 상태로 구분");
        Equal(1, error.CompletedItems, "오류 항목은 완료 수에 포함하지 않음");

        session.Results.DioLoopbackStatus = InspectionExecutionStatus.Pass;
        session.Results.DipStatus = InspectionExecutionStatus.Fail;
        session.Results.DioSafetyStatus = DioSafetyStatus.OffVerified;
        var complete = InspectionWorkflowService.Evaluate(session, true, "MAIN UART4 포트 연결");
        Equal(4, complete.CompletedItems, "PASS/FAIL 완료 항목 집계");
        Equal(WorkflowStageState.Current, complete.Steps[3].State, "필수 검사 완료 후 결과 확인 안내");

        session.Results.IsFinalized = true;
        session.Results.OverallPassed = false;
        session.Results.WorkbookSavedAt = DateTime.Now;
        var saved = InspectionWorkflowService.Evaluate(session, true, "MAIN UART4 포트 연결");
        True(saved.Steps.All(step => step.State == WorkflowStageState.Complete), "Excel 저장 후 전체 흐름 완료");

        Equal(DeviceResponseStatus.ProtocolError, DeviceResponseTracker.ClassifyFailure("CRC 불일치"), "CRC 오류와 무응답 구분");
        Equal(DeviceResponseStatus.NoResponse, DeviceResponseTracker.ClassifyFailure("응답 시간 초과"), "장치 무응답 분류");
    }

    private void VerifyPowerCriteriaBoundaries()
    {
        foreach (var boardType in new[] { InspectionBoardType.Communication, InspectionBoardType.DigitalIo })
        {
            var criteria = PowerInspectionCriteriaService.Get(boardType);
            foreach (var criterion in criteria.Where(item => item.Minimum.HasValue && item.Maximum.HasValue))
            {
                Equal(VoltageEvaluation.Pass,
                    PowerInspectionCriteriaService.Evaluate(criterion.Minimum!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), criterion.Minimum.Value, criterion.Maximum!.Value, out _),
                    $"{boardType} {criterion.Key} 최소 경계 PASS");
                Equal(VoltageEvaluation.Pass,
                    PowerInspectionCriteriaService.Evaluate(criterion.Maximum!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), criterion.Minimum.Value, criterion.Maximum.Value, out _),
                    $"{boardType} {criterion.Key} 최대 경계 PASS");
                Equal(VoltageEvaluation.Fail,
                    PowerInspectionCriteriaService.Evaluate((criterion.Minimum.Value - 0.01).ToString(System.Globalization.CultureInfo.InvariantCulture), criterion.Minimum.Value, criterion.Maximum.Value, out _),
                    $"{boardType} {criterion.Key} 최소 미달 FAIL");
                Equal(VoltageEvaluation.Fail,
                    PowerInspectionCriteriaService.Evaluate((criterion.Maximum.Value + 0.01).ToString(System.Globalization.CultureInfo.InvariantCulture), criterion.Minimum.Value, criterion.Maximum.Value, out _),
                    $"{boardType} {criterion.Key} 최대 초과 FAIL");
            }
        }

        Equal(VoltageEvaluation.Pass, PowerInspectionCriteriaService.Evaluate("5,0V", 4.9, 5.1, out _), "전압 쉼표와 V 단위 입력 허용");
        Equal(VoltageEvaluation.Missing, PowerInspectionCriteriaService.Evaluate("  ", 4.9, 5.1, out _), "빈 전압 입력 미완료 구분");
        Equal(VoltageEvaluation.Invalid, PowerInspectionCriteriaService.Evaluate("abc", 4.9, 5.1, out _), "문자 전압 입력 오류 구분");
        Equal(VoltageEvaluation.Invalid, PowerInspectionCriteriaService.Evaluate("NaN", 4.9, 5.1, out _), "NaN 전압 입력 오류 구분");
    }

    private void VerifyProtocolFaultInjection()
    {
        var validModbus = AddCrc([0x01, ModbusFunctionCode.ReadHoldingRegisters, 0x02, 0x12, 0x34]);
        var read = ModbusRtuCodec.ParseReadResponse(1, ModbusFunctionCode.ReadHoldingRegisters, 163, 1, validModbus);
        Equal((ushort)0x1234, read.Registers[0], "Modbus 정상 응답 파싱");

        var badCrc = validModbus.ToArray();
        badCrc[^1] ^= 0xFF;
        ExpectException<ModbusCrcMismatchException>(() =>
            ModbusRtuCodec.ParseReadResponse(1, ModbusFunctionCode.ReadHoldingRegisters, 163, 1, badCrc), "Modbus CRC 오류 주입");
        ExpectException<ModbusSlaveIdMismatchException>(() =>
            ModbusRtuCodec.ParseReadResponse(1, ModbusFunctionCode.ReadHoldingRegisters, 163, 1,
                AddCrc([0x02, ModbusFunctionCode.ReadHoldingRegisters, 0x02, 0x12, 0x34])), "Modbus Slave ID 오류 주입");
        ExpectException<ModbusFunctionCodeMismatchException>(() =>
            ModbusRtuCodec.ParseReadResponse(1, ModbusFunctionCode.ReadHoldingRegisters, 163, 1,
                AddCrc([0x01, ModbusFunctionCode.ReadInputRegisters, 0x02, 0x12, 0x34])), "Modbus Function 오류 주입");
        ExpectException<ModbusByteCountMismatchException>(() =>
            ModbusRtuCodec.ParseReadResponse(1, ModbusFunctionCode.ReadHoldingRegisters, 163, 1,
                AddCrc([0x01, ModbusFunctionCode.ReadHoldingRegisters, 0x04, 0x12, 0x34])), "Modbus 길이 오류 주입");
        ExpectException<ModbusExceptionResponseException>(() =>
            ModbusRtuCodec.ParseReadResponse(1, ModbusFunctionCode.ReadHoldingRegisters, 163, 1,
                AddCrc([0x01, (byte)(ModbusFunctionCode.ReadHoldingRegisters | 0x80), 0x02])), "Modbus Exception 응답 주입");

        var registers = new ushort[] { 0x1234, 0xABCD };
        var validGst = GstFrameCodec.Build(GstFunctionCode.Write, 10, registers);
        var parsedGst = GstFrameCodec.Parse(validGst);
        Equal((ushort)0x1234, parsedGst.Registers[0], "GST 정상 프레임 파싱");
        ExpectException<GstHeaderMismatchException>(() => GstFrameCodec.Parse(GstFrameMutator.Mutate(GstFunctionCode.Write, 10, registers, GstFaultKind.Header)), "GST Header 오류 주입");
        ExpectException<GstTailMismatchException>(() => GstFrameCodec.Parse(GstFrameMutator.Mutate(GstFunctionCode.Write, 10, registers, GstFaultKind.Tail)), "GST Tail 오류 주입");
        ExpectException<GstCrcMismatchException>(() => GstFrameCodec.Parse(GstFrameMutator.Mutate(GstFunctionCode.Write, 10, registers, GstFaultKind.Crc)), "GST CRC 오류 주입");
        ExpectException<GstFunctionCodeInvalidException>(() => GstFrameCodec.Parse(GstFrameMutator.Mutate(GstFunctionCode.Write, 10, registers, GstFaultKind.FunctionCode)), "GST Function 오류 주입");
        ExpectException<GstLengthMismatchException>(() => GstFrameCodec.Parse(GstFrameMutator.Mutate(GstFunctionCode.Write, 10, registers, GstFaultKind.Length)), "GST Length 오류 주입");
        ExpectException<GstLengthMismatchException>(() => GstFrameCodec.Parse(GstFrameMutator.Mutate(GstFunctionCode.Write, 10, registers, GstFaultKind.PartialFrame)), "GST 부분 프레임 주입");

        Equal("FILE_PERMISSION_DENIED", InspectionErrorClassifier.Classify(new UnauthorizedAccessException()), "파일 권한 오류 코드");
        Equal("FILE_NOT_FOUND", InspectionErrorClassifier.Classify(new FileNotFoundException()), "파일 없음 오류 코드");
        Equal("DATA_INVALID", InspectionErrorClassifier.Classify(new InvalidDataException()), "손상 데이터 오류 코드");
        Equal("FILE_OR_PORT_IO", InspectionErrorClassifier.Classify(new IOException()), "파일·포트 IO 오류 코드");
        Equal("DISK_FULL", InspectionErrorClassifier.Classify(new DiskFullTestException()), "디스크 공간 부족 오류 코드");
    }

    private void VerifySafetyOperationRules()
    {
        var completed = SafetyOperationService.RunWithTimeoutAsync(
            "즉시 완료",
            TimeSpan.FromSeconds(1),
            _ => Task.CompletedTask);
        completed.GetAwaiter().GetResult();
        True(completed.IsCompletedSuccessfully, "안전 정리 정상 완료");

        try
        {
            SafetyOperationService.RunWithTimeoutAsync(
                "응답 없는 정리",
                TimeSpan.FromMilliseconds(30),
                ct => Task.Delay(TimeSpan.FromSeconds(2), ct)).GetAwaiter().GetResult();
            throw new InvalidOperationException("제한시간 초과가 발생하지 않았습니다.");
        }
        catch (TimeoutException)
        {
            _passed.Add("안전 정리 제한시간 초과 구분");
        }

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try
        {
            SafetyOperationService.RunWithTimeoutAsync(
                "사용자 취소",
                TimeSpan.FromSeconds(1),
                ct => Task.Delay(100, ct),
                canceled.Token).GetAwaiter().GetResult();
            throw new InvalidOperationException("사용자 취소가 전달되지 않았습니다.");
        }
        catch (OperationCanceledException)
        {
            _passed.Add("사용자 취소와 제한시간 초과 분리");
        }

        Equal("DEVICE_TIMEOUT", InspectionErrorClassifier.Classify(new TimeoutException()), "안전 정리 시간 초과 오류 코드");
        True(SafetyOperationService.DioOutputOffTimeout < SafetyOperationService.FaultRestoreTimeout,
            "DIO OFF와 FAULT 복구 제한시간 독립 설정");

        var appSource = Path.Combine(Directory.GetCurrentDirectory(), "OperateTest.App");
        var productionSource = string.Join(Environment.NewLine,
            Directory.EnumerateFiles(appSource, "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));
        True(!productionSource.Contains("GetAwaiter().GetResult", StringComparison.Ordinal),
            "운영 UI 동기 대기 제거");
        True(!productionSource.Contains(".Wait()", StringComparison.Ordinal),
            "운영 UI Task.Wait 제거");

        var startupSource = File.ReadAllText(Path.Combine(appSource, "Program.cs"));
        True(startupSource.Contains("[STAThread]", StringComparison.Ordinal),
            "Windows 파일 대화상자 STA 진입점 지정");
        True(startupSource.Contains("private static void Main()", StringComparison.Ordinal),
            "STA 동기 진입점 유지");
        True(!startupSource.Contains("async Task Main", StringComparison.Ordinal),
            "파일 대화상자 STA 손실 가능 비동기 진입점 제거");
        True(startupSource.Contains("Application.ThreadException", StringComparison.Ordinal),
            "UI 예외 전역 복구 처리 등록");

        var applicationEntryPoint = typeof(HomeForm).Assembly.EntryPoint
            ?? throw new InvalidOperationException("검사 프로그램 진입점을 찾을 수 없습니다.");
        Equal(typeof(void), applicationEntryPoint.ReturnType,
            "컴파일된 프로그램 동기 진입점");
        True(applicationEntryPoint.IsDefined(typeof(STAThreadAttribute), inherit: false),
            "컴파일된 프로그램 STA 특성 유지");
    }

    private void VerifyResponsiveUiContract()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState(),
                    "UI 검증 스레드 STA 상태");
                ApplicationConfiguration.Initialize();
                using var connections = new SerialConnectionManager(new SerialPortCatalog());
                var protocols = new InspectionProtocolServices(connections);
                var memory = new StLinkMemoryReader(Path.Combine(_root, "missing-st-link.exe"));
                var dioReader = new StLinkDioIdSwitchReader(memory);
                var rotaryReader = new StLinkCommRotaryReader(memory);

                var session = _workbooks.CreateSession(
                    _dioTemplate, Directory.CreateDirectory(Path.Combine(_root, "ui")).FullName,
                    InspectionBoardType.DigitalIo, new DateTime(2026, 10, 12),
                    "화면검증", Serial("DIO", 901), true);
                MarkWorkbookSaved(session);
                var commSession = _workbooks.CreateSession(
                    _commTemplate, Directory.CreateDirectory(Path.Combine(_root, "ui-comm")).FullName,
                    InspectionBoardType.Communication, new DateTime(2026, 10, 12),
                    "화면검증", Serial("COM", 901), true);

                using var home = new HomeForm(connections, protocols, dioReader, rotaryReader);
                using var setup = new InspectionSetupForm(InspectionBoardType.DigitalIo);
                using var completion = new InspectionCompletionForm(session, new WorkbookUsage(1, 30));
                using var next = new NextBoardSetupForm(session, new WorkbookUsage(1, 30));
                using var comm = new CommInspectionForm(connections, protocols, rotaryReader, commSession);
                using var dio = new DioInspectionForm(connections, protocols, dioReader, session);
                using var rotary = new RotaryInspectionForm(rotaryReader, session);
                using var dip = new DioIdSwitchInspectionForm(dioReader, session);
                using var fault = new FaultInspectionForm(connections, protocols, session: session);

                var forms = new Form[] { home, setup, completion, next, comm, dio, rotary, dip, fault };
                foreach (var form in forms)
                    Equal(AutoScaleMode.Dpi, form.AutoScaleMode, $"{form.GetType().Name} DPI 자동 스케일");
                var advanceDip = typeof(DioIdSwitchInspectionForm).GetMethod("AdvanceIfStable",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                foreach (var id in new[] { 2, 0, 3, 1 })
                {
                    advanceDip.Invoke(dip, [id]);
                    advanceDip.Invoke(dip, [id]);
                }
                True(session.Results.DipSwitchPassed == true,
                    "DIP 스위치 ID 0~3을 변경하는 순서와 관계없이 모두 확인");
                var commWorkflow = Descendants<WorkflowProgressBar>(comm).Single();
                var dioWorkflow = Descendants<WorkflowProgressBar>(dio).Single();
                Equal(5, commWorkflow.Steps.Count, "COMM 공통 5단계 진행 표시");
                Equal(5, dioWorkflow.Steps.Count, "DIO 공통 5단계 진행 표시");
                True(commWorkflow.Height >= 78 && dioWorkflow.Height >= 78,
                    "진행 표시 단계명과 상세문구 높이 분리");

                comm.CreateControl();
                comm.PerformLayout();
                dio.CreateControl();
                dio.PerformLayout();
                True(commWorkflow.Right <= commWorkflow.Parent!.ClientSize.Width - 40,
                    "COMM 진행 표시가 화면 폭을 넘지 않음");
                True(dioWorkflow.Right <= dioWorkflow.Parent!.ClientSize.Width - 40,
                    "DIO 진행 표시가 화면 폭을 넘지 않음");

                typeof(CommInspectionForm).GetMethod("Navigate",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(comm, ["dashboard"]);
                comm.ClientSize = new Size(1900, 1000);
                comm.PerformLayout();
                var dashboardHost = Descendants<Panel>(comm)
                    .Single(panel => panel.AutoScrollMinSize.Width == 1252);
                dashboardHost.PerformLayout();
                var dashboardCanvas = dashboardHost.Controls.Cast<Control>().Single();
                True(dashboardHost.ClientSize.Width > dashboardCanvas.Width,
                    "전체 화면에서 COMM 대시보드 중앙 배치 공간 확보");
                Equal((dashboardHost.ClientSize.Width - dashboardCanvas.Width) / 2,
                    dashboardCanvas.Left, "전체 화면에서 COMM 대시보드 가로 중앙 배치");
                var dashboardTitle = Descendants<Label>(comm)
                    .Single(label => label.Text == "COMM 보드 검사 대시보드");
                True(TextRenderer.MeasureText(dashboardTitle.Text, dashboardTitle.Font).Height
                     <= dashboardTitle.ClientSize.Height,
                    "COMM 대시보드 제목 글자 높이 확보");

                using (var buttonHost = new Panel { Size = new Size(180, 70), BackColor = Color.White })
                using (var dangerButton = new AppButton
                {
                    Size = new Size(150, 44),
                    Location = new Point(10, 10),
                    Variant = AppButtonVariant.Danger,
                    ForcedState = VisualState.Focused,
                    Text = "FAIL 판정",
                    IconGlyph = AppIcon.Warning
                })
                using (var bitmap = new Bitmap(dangerButton.Width, dangerButton.Height))
                {
                    buttonHost.Controls.Add(dangerButton);
                    dangerButton.DrawToBitmap(bitmap, dangerButton.ClientRectangle);
                    True(bitmap.GetPixel(dangerButton.Width / 2, dangerButton.Height - 3) != buttonHost.BackColor,
                        "FAIL 버튼 하단 배경 끊김 방지");
                }

                var choiceCards = home.Controls.OfType<AppPanel>().ToArray();
                Equal(2, choiceCards.Length, "작은 화면 보드 선택 카드 2개 유지");
                foreach (var size in new[] { new Size(980, 650), new Size(1280, 720), new Size(1366, 768), new Size(1920, 1080) })
                {
                    home.ClientSize = size;
                    home.PerformLayout();
                    True(choiceCards.All(card => home.ClientRectangle.Contains(card.Bounds)), $"{size.Width}×{size.Height} 보드 선택 카드 화면 내 배치");
                }
                True(Descendants<AppButton>(home).Any(button => button.Text == "버전·진단 정보"), "홈 화면 버전·진단 정보 접근 버튼");
                True(ApplicationBuildInfo.Version.Contains("1.0.0", StringComparison.Ordinal), "프로그램 표시 버전 설정");

                True(setup.AutoScroll && setup.AutoScrollMinSize.Width >= 900, "검사 준비 창 축소 시 스크롤 접근");
                True(completion.AutoScroll && completion.AutoScrollMinSize.Height >= 520, "검사 완료 창 축소 시 스크롤 접근");
                True(next.AutoScroll && next.AutoScrollMinSize.Width >= 650, "다음 보드 창 축소 시 스크롤 접근");
                True(rotary.AutoScroll && dip.AutoScroll && fault.AutoScroll, "전용 검사창 축소 시 스크롤 접근");
                foreach (var inspectionForm in new Form[] { comm, dio, rotary, dip, fault })
                {
                    inspectionForm.CreateControl();
                    foreach (var label in Descendants<Label>(inspectionForm)
                                 .Where(label => label.Text.Contains('\n') && !label.AutoEllipsis))
                    {
                        var measured = TextRenderer.MeasureText(label.Text, label.Font,
                            new Size(Math.Max(1, label.ClientSize.Width), int.MaxValue),
                            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                        True(measured.Height <= label.ClientSize.Height,
                            $"{inspectionForm.GetType().Name} 여러 줄 안내 높이: {label.Text.Split('\n')[0]}");
                    }
                }
                True(Descendants<SideMenuButton>(dio).Any(button => button.Text == "UART4 통신 연결"),
                    "DIO 육안 검사 다음 UART4 통신 연결 메뉴 표시");
                var faultStage = Descendants<AppCard>(fault)
                    .Single(card => card.CardLabel == "현재 단계");
                faultStage.CardValue = "OPEN 확인 대기";
                var stageValue = faultStage.Controls.OfType<Label>()
                    .Single(label => label.Text == "OPEN 확인 대기");
                var stageTextSize = TextRenderer.MeasureText(stageValue.Text, stageValue.Font);
                True(stageTextSize.Width <= stageValue.ClientSize.Width &&
                     stageTextSize.Height <= stageValue.ClientSize.Height,
                    "FAULT 단계 카드 글자 전체 표시");

                foreach (var inspectionForm in new Form[] { rotary, dip })
                {
                    var failButton = Descendants<AppButton>(inspectionForm)
                        .Single(button => button.Text == "FAIL 판정");
                    var overlappingLabels = failButton.Parent!.Controls.OfType<Label>()
                        .Where(label => label.Bounds.IntersectsWith(failButton.Bounds))
                        .ToArray();
                    Equal(0, overlappingLabels.Length,
                        $"{inspectionForm.GetType().Name} FAIL 버튼과 헤더 문구 비중첩");
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw new InvalidOperationException("반응형 UI 계약 검증 실패", failure);
    }

    private void VerifyProtocolContract()
    {
        var expected = new[]
        {
            (ChannelDefinitions.Uart4, "comm-uart4", 115200, Parity.None),
            (ChannelDefinitions.DioSlave, "comm-lpuart1", 38400, Parity.None),
            (ChannelDefinitions.Heating, "comm-usart2", 19200, Parity.None),
            (ChannelDefinitions.Gas, "comm-usart3", 9600, Parity.Even),
            (ChannelDefinitions.Backup, "comm-usart1", 115200, Parity.None)
        };

        foreach (var item in expected)
        {
            Equal(item.Item2, item.Item1.Key, $"{item.Item2} 채널 키 고정");
            Equal(item.Item3, item.Item1.Options.BaudRate, $"{item.Item2} 통신 속도 고정");
            Equal(item.Item4, item.Item1.Options.Parity, $"{item.Item2} Parity 고정");
            Equal(8, item.Item1.Options.DataBits, $"{item.Item2} 데이터 비트 고정");
            Equal(StopBits.One, item.Item1.Options.StopBits, $"{item.Item2} Stop bit 고정");
        }
        Equal(4, ChannelDefinitions.CommPortChannels.Count, "COMM 물리 COM 포트 4개 고정");
        Equal(4, ChannelDefinitions.CommChannels.Count, "COMM 검사 경로 4개 고정");
        True(!ChannelDefinitions.CommChannels.Any(channel => channel.Key == ChannelDefinitions.DioSlave.Key),
            "DIO 내부 통신을 COMM 검사에서 제외");
        var dioReadAddress = typeof(DioLoopbackService).GetMethod("ReadAddress",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var dioWriteAddress = typeof(DioLoopbackService).GetMethod("WriteAddress",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        Equal((ushort)1, (ushort)dioReadAddress.Invoke(null, [(byte)0])!, "ID 0 DIO 입력 맵 HR1 사용");
        Equal((ushort)201, (ushort)dioWriteAddress.Invoke(null, [(byte)0])!, "ID 0 DIO 출력 맵 HR201 사용");
        var dioMaster = new DioIdZeroMasterStub();
        var dioService = new DioLoopbackService(new DioIdZeroProtocolStub(dioMaster));
        Equal((byte)0, dioService.FindSlaveIdAsync().GetAwaiter().GetResult(),
            "DIO 한 대 연결 시 HR163 조건 없이 ID 0 확인");
        dioService.SetAllOutputsOffAsync(0).GetAwaiter().GetResult();
        True(dioMaster.LastWrite.HasValue, "ID 0 출력 명령 전송");
        Equal((byte)5, dioMaster.LastWrite!.Value.Group, "ID 0 출력 그룹 사용");
        Equal((ushort)201, dioMaster.LastWrite.Value.Address, "ID 0 출력 주소 사용");
        var delayedLoopback = dioService.RunAsync().GetAwaiter().GetResult();
        True(delayedLoopback.Passed, "DO 변경 뒤 이전 DI가 세 차례 읽혀도 16채널 루프백 PASS");
        Equal(16, delayedLoopback.Channels.Count, "지연된 DI의 전 채널 검사 완료");
        var droppedMaster = new DioIdZeroMasterStub { DropFirstOnWrite = true };
        var retriedLoopback = new DioLoopbackService(new DioIdZeroProtocolStub(droppedMaster))
            .RunAsync().GetAwaiter().GetResult();
        True(retriedLoopback.Passed, "MAIN ACK 후 첫 DIO 전달이 누락되어도 재전송으로 루프백 완료");
        var silentMaster = new DioIdZeroMasterStub { NeverApplyOutput = true };
        var silentLoopback = new DioLoopbackService(new DioIdZeroProtocolStub(silentMaster))
            .RunAsync().GetAwaiter().GetResult();
        True(!silentLoopback.Passed && silentLoopback.Channels.Count == 1,
            "MAIN ACK만 있고 LED·DI 출력 반영이 없으면 첫 채널에서 중단");
    }

    private void VerifyWorkbookProfiles()
    {
        var dio = _workbooks.Validate(_dioTemplate, InspectionBoardType.DigitalIo);
        True(dio.IsValid, "DIO R01 구조 프로필 승인");
        Equal(InspectionWorkbookService.SupportedProfileVersion, dio.ProfileVersion, "DIO 양식 버전 R01 판별");

        var comm = _workbooks.Validate(_commTemplate, InspectionBoardType.Communication);
        True(comm.IsValid, "COMM R01 구조 프로필 승인");
        Equal(InspectionWorkbookService.SupportedProfileVersion, comm.ProfileVersion, "COMM 양식 버전 R01 판별");

        var invalid = Path.Combine(_root, "invalid-dio-template.xlsx");
        WorkbookFixture.Create(invalid, InspectionBoardType.DigitalIo, corruptPowerHeader: true);
        True(!_workbooks.Validate(invalid, InspectionBoardType.DigitalIo).IsValid, "변경된 전원검사 셀 매핑 차단");
        True(!_workbooks.Validate(_dioTemplate, InspectionBoardType.Communication).IsValid, "다른 보드 기준서 선택 차단");

        var repositoryRoot = Directory.GetCurrentDirectory();
        var documentRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "..", "..", "..", "docs", "수입검사기준서"));
        var approvedComm = Path.Combine(documentRoot, "수입검사기준서_COMMUNICATION_BOARD_R01.xlsx");
        var approvedDio = Path.Combine(documentRoot, "수입검사기준서_DIGITAL_IO_BOARD_R01.xlsx");
        if (File.Exists(approvedComm) && File.Exists(approvedDio))
        {
            True(_workbooks.Validate(approvedComm, InspectionBoardType.Communication).IsValid, "승인 COMM 기준서 실파일 검증");
            True(_workbooks.Validate(approvedDio, InspectionBoardType.DigitalIo).IsValid, "승인 DIO 기준서 실파일 검증");
        }
        else
        {
            Console.WriteLine($"INFO  승인 기준서 실파일 경로가 없어 구조 Fixture 검증만 수행: {documentRoot}");
        }
    }

    private void VerifyTenBoardsAndThirtyRowLimit()
    {
        var output = Directory.CreateDirectory(Path.Combine(_root, "sequential")).FullName;
        var current = _workbooks.CreateSession(
            _dioTemplate, output, InspectionBoardType.DigitalIo,
            new DateTime(2026, 10, 12), "연속검사", Serial("DIO", 1), true);
        Equal(6, current.RowNumber, "첫 보드는 Excel 6행에 배정");

        for (var number = 2; number <= 10; number++)
        {
            var previousSessionId = current.SessionId;
            MarkWorkbookSaved(current);
            current = _workbooks.CreateNextSession(current, Serial("DIO", number));
            Equal(number + 5, current.RowNumber, $"연속 보드 {number}번 행 배정");
            Equal(previousSessionId, current.PreviousSessionId!.Value, $"연속 보드 {number}번 이전 세션 연결");
        }

        var usage = _workbooks.GetUsage(current.ResultPath, InspectionBoardType.DigitalIo);
        Equal(10, usage.UsedRows, "동일 Excel에 보드 10개 누적");
        Equal(20, usage.RemainingRows, "10개 기록 후 남은 행 계산");

        MarkWorkbookSaved(current);
        ExpectFailure(
            () => _workbooks.CreateNextSession(current, Serial("DIO", 1)),
            "이미 등록되어 있습니다",
            "동일 시리얼번호 중복 차단");

        for (var number = 11; number <= 30; number++)
        {
            MarkWorkbookSaved(current);
            current = _workbooks.CreateNextSession(current, Serial("DIO", number));
        }

        usage = _workbooks.GetUsage(current.ResultPath, InspectionBoardType.DigitalIo);
        Equal(30, usage.UsedRows, "30번째 보드까지 기록");
        True(usage.IsFull, "30행 사용 완료 상태 감지");
        MarkWorkbookSaved(current);
        ExpectFailure(
            () => _workbooks.CreateNextSession(current, Serial("DIO", 31)),
            "모두 사용되었습니다",
            "31번째 보드 같은 문서 기록 차단");
    }

    private void VerifyCheckpointResume()
    {
        var output = Directory.CreateDirectory(Path.Combine(_root, "checkpoint")).FullName;
        var session = _workbooks.CreateSession(
            _dioTemplate, output, InspectionBoardType.DigitalIo,
            new DateTime(2026, 10, 12), "재개검사", Serial("DIO", 101), true);
        session.Results.ManualStatus = InspectionExecutionStatus.Running;
        session.Results.DioLoopbackStatus = InspectionExecutionStatus.Running;
        session.Results.DioSafetyStatus = DioSafetyStatus.Restoring;
        session.Results.VisualItems["missing"] = new InspectionItemResult { Passed = true };
        DeviceResponseTracker.Update(session.Results, ChannelDefinitions.Uart4.Key, DeviceResponseStatus.Responding, "응답 정상");

        var checkpointPath = _checkpoints.Save(session);
        True(File.Exists(checkpointPath), "중단 검사 체크포인트 생성");
        var resumed = _checkpoints.Load(checkpointPath, InspectionBoardType.DigitalIo);
        Equal(session.SerialNumber, resumed.SerialNumber, "체크포인트 시리얼 복구");
        Equal(InspectionExecutionStatus.Stopped, resumed.Results.ManualStatus, "실행 중 육안검사를 중단 상태로 복구");
        Equal(InspectionExecutionStatus.Stopped, resumed.Results.DioLoopbackStatus, "실행 중 DIO 검사를 중단 상태로 복구");
        Equal(DioSafetyStatus.Failed, resumed.Results.DioSafetyStatus, "복구 중이던 DO 안전 상태를 실패로 복구");
        True(resumed.Results.VisualItems["missing"].Passed == true, "저장된 세부 판정 복구");
        Equal(DeviceResponseStatus.Responding, resumed.Results.DeviceResponses[ChannelDefinitions.Uart4.Key].Status, "장치 응답 상태 복구");
        _checkpoints.Delete(resumed);
    }

    private void VerifyTraceabilityAndCheckpointRecovery()
    {
        var output = Directory.CreateDirectory(Path.Combine(_root, "traceability")).FullName;
        var previousId = Guid.NewGuid();
        var session = _workbooks.CreateSession(
            _dioTemplate, output, InspectionBoardType.DigitalIo,
            new DateTime(2026, 10, 12), "추적성검사자", Serial("DIO", 151), true, previousId);

        True(session.SessionId != Guid.Empty, "검사 세션 고유 ID 생성");
        Equal(previousId, session.PreviousSessionId!.Value, "이전 문서 검사 세션 연결");
        True(!string.IsNullOrWhiteSpace(session.ApplicationVersion), "프로그램 버전 기록");
        Equal(InspectionWorkbookService.SupportedProfileVersion, session.TemplateProfileVersion, "양식 프로필 버전 기록");
        Equal(64, session.TemplateSha256.Length, "기준서 SHA-256 기록");

        var audit = new InspectionAuditService();
        var firstAttempt = audit.BeginAttempt(session, "dio_loopback", "첫 검사");
        audit.CompleteAttempt(session, "dio_loopback", firstAttempt, InspectionExecutionStatus.Fail, "채널 불일치");
        var secondAttempt = audit.BeginAttempt(session, "dio_loopback", "재검사");
        audit.CompleteAttempt(session, "dio_loopback", secondAttempt, InspectionExecutionStatus.Pass, "정상");
        Equal(1, firstAttempt, "첫 검사 시도 번호");
        Equal(2, secondAttempt, "재검사 시도 번호 증가");

        var checkpoint = _checkpoints.Save(session);
        session.Results.DipStatus = InspectionExecutionStatus.Running;
        _checkpoints.Save(session);
        True(File.Exists(InspectionCheckpointService.BackupPath(checkpoint)), "체크포인트 복구용 백업 생성");
        File.WriteAllText(checkpoint, "{ damaged checkpoint");

        var recovered = _checkpoints.Load(checkpoint, InspectionBoardType.DigitalIo);
        Equal(session.SessionId, recovered.SessionId, "백업에서 세션 ID 복구");
        Equal(2, recovered.AttemptCounts["dio_loopback"], "백업에서 재검사 횟수 복구");
        True(Directory.GetFiles(output, Path.GetFileName(checkpoint) + ".corrupt.*").Length == 1, "손상 체크포인트 원본 보존");

        var auditPath = InspectionAuditService.AuditPath(session);
        True(File.Exists(auditPath), "외부 감사 JSONL 생성");
        var auditText = File.ReadAllText(auditPath);
        True(auditText.Contains(session.SessionId.ToString(), StringComparison.OrdinalIgnoreCase), "감사 로그 세션 ID 기록");
        True(auditText.Contains("attempt_completed", StringComparison.Ordinal), "감사 로그 검사 완료 이벤트 기록");
        True(auditText.Contains("checkpoint_saved", StringComparison.Ordinal), "감사 로그 자동 저장 이벤트 기록");

        _checkpoints.Save(recovered);
        var unsupported = File.ReadAllText(checkpoint).Replace("\"Version\": 1", "\"Version\": 999", StringComparison.Ordinal);
        File.WriteAllText(checkpoint, unsupported);
        ExpectFailure(() => _checkpoints.Load(checkpoint, InspectionBoardType.DigitalIo), "지원하지 않는 체크포인트 버전", "체크포인트 버전 불일치 차단");
        _checkpoints.Delete(recovered);
    }

    private void VerifyNewDocumentAndBoardSeparation()
    {
        var output = Directory.CreateDirectory(Path.Combine(_root, "new-documents")).FullName;
        var first = _workbooks.CreateSession(
            _dioTemplate, output, InspectionBoardType.DigitalIo,
            new DateTime(2026, 10, 12), "문서검사", Serial("DIO", 201), true);
        var second = _workbooks.CreateSession(
            _dioTemplate, output, InspectionBoardType.DigitalIo,
            new DateTime(2026, 10, 12), "문서검사", Serial("DIO", 202), true);
        True(!string.Equals(first.ResultPath, second.ResultPath, StringComparison.OrdinalIgnoreCase), "새 문서가 기존 결과를 덮어쓰지 않음");
        True(Path.GetFileNameWithoutExtension(second.ResultPath).EndsWith("_02", StringComparison.Ordinal), "두 번째 새 문서에 _02 순번 부여");

        var comm = _workbooks.CreateSession(
            _commTemplate, output, InspectionBoardType.Communication,
            new DateTime(2026, 10, 12), "문서검사", Serial("COM", 201), true);
        True(Path.GetFileName(comm.ResultPath).Contains("COMMUNICATION_BOARD", StringComparison.Ordinal), "COMM 결과 파일명 분리");
        True(Path.GetFileName(first.ResultPath).Contains("DIGITAL_IO_BOARD", StringComparison.Ordinal), "DIO 결과 파일명 분리");
    }

    private void VerifyLockedExcelIsNotDamaged()
    {
        var output = Directory.CreateDirectory(Path.Combine(_root, "locked-file")).FullName;
        var session = _workbooks.CreateSession(
            _dioTemplate, output, InspectionBoardType.DigitalIo,
            new DateTime(2026, 10, 12), "잠금검사", Serial("DIO", 301), true);
        PopulatePassingDioResults(session);
        var checkpoint = _checkpoints.Save(session);
        var before = Sha256(session.ResultPath);

        using (var locked = new FileStream(session.ResultPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            ExpectFailure(
                () => _workbooks.SaveFinalResults(session),
                string.Empty,
                "Excel 잠금 상태에서 최종 저장 실패 처리");
        }

        var after = Sha256(session.ResultPath);
        Equal(before, after, "저장 실패 후 기존 Excel 바이트 무손상");
        True(File.Exists(checkpoint), "저장 실패 후 체크포인트 유지");

        File.SetAttributes(session.ResultPath, File.GetAttributes(session.ResultPath) | FileAttributes.ReadOnly);
        try
        {
            ExpectFailure(
                () => _workbooks.SaveFinalResults(session),
                string.Empty,
                "Excel 읽기 전용 상태에서 최종 저장 실패 처리");
            Equal(before, Sha256(session.ResultPath), "읽기 전용 저장 실패 후 Excel 무손상");
            True(File.Exists(checkpoint), "읽기 전용 저장 실패 후 체크포인트 유지");
        }
        finally
        {
            File.SetAttributes(session.ResultPath, File.GetAttributes(session.ResultPath) & ~FileAttributes.ReadOnly);
        }

        _workbooks.SaveFinalResults(session);
        True(session.Results.WorkbookSavedAt.HasValue, "잠금 해제 후 최종 Excel 저장 성공");
        True(!File.Exists(checkpoint), "최종 저장 성공 후 체크포인트 삭제");
        Equal("■ P / □ F", WorkbookFixture.ReadCell(session.ResultPath, 1, "D6"), "육안검사 PASS 셀 매핑");
        Equal("■ P / □ F", WorkbookFixture.ReadCell(session.ResultPath, 2, "E6"), "전원검사 PASS 셀 매핑");
        Equal("■ P / □ F", WorkbookFixture.ReadCell(session.ResultPath, 3, "D6"), "Digital Input PASS 셀 매핑");
        Equal("■ P / □ F", WorkbookFixture.ReadCell(session.ResultPath, 3, "F6"), "Digital Output PASS 셀 매핑");
        Equal("■ P / □ F", WorkbookFixture.ReadCell(session.ResultPath, 3, "H6"), "DIP Switch PASS 셀 매핑");
    }

    private static void PopulatePassingDioResults(InspectionSession session)
    {
        foreach (var key in new[] { "missing", "reversed", "cold_joint", "wrong_part", "foreign", "solder_short", "cleaning" })
            session.Results.VisualItems[key] = new InspectionItemResult { Passed = true };
        foreach (var key in new[] { "power_led", "input_24v", "c30_5v", "tp3_3v3" })
            session.Results.PowerItems[key] = new InspectionItemResult { Passed = true, Measurement = key == "power_led" ? string.Empty : "5.0" };
        session.Results.DioInputPassed = true;
        session.Results.DioOutputPassed = true;
        session.Results.DipSwitchPassed = true;
        session.Results.IsFinalized = true;
        session.Results.OverallPassed = true;
        session.Results.FinalizedAt = DateTime.Now;
    }

    private static void MarkWorkbookSaved(InspectionSession session)
    {
        session.Results.IsFinalized = true;
        session.Results.OverallPassed = true;
        session.Results.WorkbookSavedAt = DateTime.Now;
    }

    private static string Serial(string prefix, int number) => $"{prefix}-2610-{number:000}";

    private void ExpectFailure(Action action, string messagePart, string name)
    {
        try
        {
            action();
            throw new InvalidOperationException($"예상한 실패가 발생하지 않았습니다: {name}");
        }
        catch (Exception ex) when (ex is not InvalidOperationException || ex.Message != $"예상한 실패가 발생하지 않았습니다: {name}")
        {
            if (messagePart.Length > 0 && !ex.Message.Contains(messagePart, StringComparison.Ordinal))
                throw new InvalidOperationException($"{name}: 예상 메시지 '{messagePart}'가 없습니다. 실제: {ex.Message}", ex);
            _passed.Add(name);
        }
    }

    private void ExpectException<TException>(Action action, string name) where TException : Exception
    {
        try
        {
            action();
            throw new InvalidOperationException($"예상한 {typeof(TException).Name} 예외가 발생하지 않았습니다: {name}");
        }
        catch (TException)
        {
            _passed.Add(name);
        }
    }

    private void Equal<T>(T expected, T actual, string name) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected={expected}, actual={actual}");
        _passed.Add(name);
    }

    private void True(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        _passed.Add(name);
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static byte[] AddCrc(byte[] payload)
    {
        var crc = ModbusRtuCodec.ComputeCrc16(payload);
        return [.. payload, (byte)(crc & 0xFF), (byte)(crc >> 8)];
    }

    private static IEnumerable<T> Descendants<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T typed) yield return typed;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private sealed class DiskFullTestException : IOException
    {
        public DiskFullTestException() => HResult = unchecked((int)0x80070070);
    }
}

internal sealed class DioIdZeroProtocolStub(IModbusMasterService master) : IInspectionProtocolServices
{
    public IModbusMasterService GetModbusMaster(string channelKey) => master;
    public IModbusSlaveEmulatorService CreateModbusSlaveEmulator(string channelKey,
        ModbusSlaveRegisterStore primaryStore, IEnumerable<ModbusSlaveRegisterStore> busStores) =>
        throw new NotSupportedException();
    public IGstPeerService GetGstPeer(string channelKey) => throw new NotSupportedException();
}

internal sealed class DioIdZeroMasterStub : IModbusMasterService
{
    private ushort _visibleInput;
    private ushort _pendingInput;
    private int _staleReads;
    private bool _droppedOnWrite;
    public bool DropFirstOnWrite { get; init; }
    public bool NeverApplyOutput { get; init; }
    public (byte Group, ushort Address)? LastWrite { get; private set; }
    public IReadOnlyList<ModbusTransaction> Transactions => [];
    public event EventHandler<ModbusTransaction>? TransactionRecorded { add { } remove { } }
    public void ClearTransactions() { }

    public Task<ModbusReadResult> ReadHoldingRegistersAsync(byte slaveId, ushort startAddress,
        ushort quantity, int timeoutMs, CancellationToken ct = default)
    {
        if (slaveId != 1 || startAddress != 1 || quantity != 1)
            throw new InvalidOperationException($"ID 0 경로 이외의 읽기 요청: {slaveId}/{startAddress}");
        if (_staleReads > 0) _staleReads--;
        else _visibleInput = _pendingInput;
        return Task.FromResult(new ModbusReadResult(slaveId, 3, startAddress, [_visibleInput], []));
    }

    public Task<ModbusWriteResult> WriteMultipleRegistersAsync(byte slaveId, ushort startAddress,
        ushort[] values, int timeoutMs, CancellationToken ct = default)
    {
        LastWrite = (slaveId, startAddress);
        if (NeverApplyOutput)
            return Task.FromResult(new ModbusWriteResult(slaveId, 0x10, startAddress, (ushort)values.Length, []));
        if (DropFirstOnWrite && !_droppedOnWrite && values[0] != 0)
        {
            _droppedOnWrite = true;
            return Task.FromResult(new ModbusWriteResult(slaveId, 0x10, startAddress, (ushort)values.Length, []));
        }
        _pendingInput = values[0];
        _staleReads = 3;
        return Task.FromResult(new ModbusWriteResult(slaveId, 0x10, startAddress, (ushort)values.Length, []));
    }

    public Task<ModbusBitReadResult> ReadCoilsAsync(byte slaveId, ushort startAddress, ushort quantity,
        int timeoutMs, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ModbusBitReadResult> ReadDiscreteInputsAsync(byte slaveId, ushort startAddress, ushort quantity,
        int timeoutMs, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ModbusReadResult> ReadInputRegistersAsync(byte slaveId, ushort startAddress, ushort quantity,
        int timeoutMs, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ModbusWriteResult> WriteSingleCoilAsync(byte slaveId, ushort address, bool value,
        int timeoutMs, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ModbusWriteResult> WriteSingleRegisterAsync(byte slaveId, ushort address, ushort value,
        int timeoutMs, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<ModbusWriteResult> WriteMultipleCoilsAsync(byte slaveId, ushort startAddress, bool[] values,
        int timeoutMs, CancellationToken ct = default) => throw new NotSupportedException();
}

internal static class WorkbookFixture
{
    private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace OfficeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";

    public static void Create(string path, InspectionBoardType boardType, bool corruptPowerHeader = false)
    {
        var sheets = boardType == InspectionBoardType.Communication
            ? new[] { "육안 검사", "전원 검사", "통신 및 기타 검사" }
            : new[] { "육안 검사", "전원 검사", "DIO&스위치 검사" };

        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        var types = new XDocument(new XElement(ContentTypes + "Types",
            new XElement(ContentTypes + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(ContentTypes + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            new XElement(ContentTypes + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
            sheets.Select((_, index) => new XElement(ContentTypes + "Override",
                new XAttribute("PartName", $"/xl/worksheets/sheet{index + 1}.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))));
        Write(archive, "[Content_Types].xml", types);

        Write(archive, "_rels/.rels", new XDocument(new XElement(PackageRelationships + "Relationships",
            new XElement(PackageRelationships + "Relationship",
                new XAttribute("Id", "rId1"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                new XAttribute("Target", "xl/workbook.xml")))));

        var workbook = new XDocument(new XElement(Spreadsheet + "workbook",
            new XAttribute(XNamespace.Xmlns + "r", OfficeRelationships),
            new XElement(Spreadsheet + "sheets", sheets.Select((name, index) => new XElement(Spreadsheet + "sheet",
                new XAttribute("name", name),
                new XAttribute("sheetId", index + 1),
                new XAttribute(OfficeRelationships + "id", $"rId{index + 1}"))))));
        Write(archive, "xl/workbook.xml", workbook);

        var relationshipElements = sheets.Select((_, index) =>
            new XElement(PackageRelationships + "Relationship",
                new XAttribute("Id", $"rId{index + 1}"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                new XAttribute("Target", $"worksheets/sheet{index + 1}.xml")))
            .ToArray();
        var workbookRelationships = new XDocument(
            new XElement(PackageRelationships + "Relationships", relationshipElements));
        Write(archive, "xl/_rels/workbook.xml.rels", workbookRelationships);

        for (var index = 0; index < sheets.Length; index++)
            Write(archive, $"xl/worksheets/sheet{index + 1}.xml", Worksheet(sheets[index], boardType, corruptPowerHeader));
    }

    private static XDocument Worksheet(string name, InspectionBoardType boardType, bool corruptPowerHeader)
    {
        var row1 = new XElement(Spreadsheet + "row", new XAttribute("r", 1),
            Cell("A1", boardType == InspectionBoardType.Communication
                ? "COMMUNICATION BOARD 검사"
                : "DIGITAL I/O BOARD 검사"),
            Cell("H1", "검사일자"), Cell("J1", "검사자"));
        var row2 = new XElement(Spreadsheet + "row", new XAttribute("r", 2),
            Cell("A2", "번호"), Cell("B2", "시리얼"), Cell("C2", "검사항목"));
        var row3 = new XElement(Spreadsheet + "row", new XAttribute("r", 3));
        var row4 = new XElement(Spreadsheet + "row", new XAttribute("r", 4), Cell("C4", "기준"));

        if (name == "육안 검사")
        {
            row2.Add(Cell("D2", "육안검사"));
            row3.Add(Cell("C3", "측정위치"), Cell("D3", "미삽"), Cell("E3", "역삽"),
                Cell("F3", "냉땜"), Cell("G3", "오삽"), Cell("H3", "이물질"),
                Cell("I3", "납쇼트"), Cell("J3", "세척"));
            foreach (var column in new[] { "D", "E", "F", "G", "H", "I", "J" })
                row4.Add(Cell(column + "4", "■ P / □ F"));
        }
        else if (name == "전원 검사")
        {
            row2.Add(Cell("D2", "전원검사"));
            row3.Add(Cell("C3", "측정위치"));
            if (boardType == InspectionBoardType.Communication)
            {
                row3.Add(Cell("D3", "LED1"), Cell("E3", "C27 양단"), Cell("F3", "TP1 – TP3"),
                    Cell("G3", "TP2 – GND_ISO1"), Cell("H3", "C37 양단"), Cell("I3", "C41 양단"));
                row4.Add(Cell("D4", "녹색 LED 점등"), Cell("E4", "23.5V~24.5V 이내"),
                    Cell("F4", "4.9V~5.1V 이내"), Cell("G4", "3.2V~3.4V 이내"),
                    Cell("H4", "4.9V~5.1V 이내"), Cell("I4", "4.9V~5.1V 이내"));
            }
            else
            {
                row3.Add(Cell("D3", "LED35"), Cell("E3", "C27 양단"),
                    Cell("F3", corruptPowerHeader ? "변경된 측정점" : "C30 양단"), Cell("G3", "TP3-GND_ISO1"));
                row4.Add(Cell("D4", "녹색 LED 점등"), Cell("E4", "23.5V~24.5V 이내"),
                    Cell("F4", "4.9V~5.1V 이내"), Cell("G4", "3.2V~3.4V 이내"));
            }
        }
        else if (name is "통신 및 기타 검사")
        {
            row2.Add(Cell("D2", "통신"), Cell("F2", "FAULT"), Cell("H2", "로터리"));
            row3.Add(Cell("C3", "측정방법"), Cell("D3", "수입검사 프로그램"));
            row4.Add(Cell("D4", "■ P / □ F"), Cell("F4", "■ P / □ F"), Cell("H4", "■ P / □ F"));
        }
        else if (name is "DIO&스위치 검사")
        {
            row2.Add(Cell("D2", "Digital Input"), Cell("F2", "Digital Output"), Cell("H2", "DIP Switch"));
            row3.Add(Cell("C3", "측정방법"), Cell("D3", "수입검사 프로그램"));
            row4.Add(Cell("D4", "■ P / □ F"), Cell("F4", "■ P / □ F"), Cell("H4", "■ P / □ F"));
        }

        return new XDocument(new XElement(Spreadsheet + "worksheet",
            new XElement(Spreadsheet + "sheetData", row1, row2, row3, row4)));
    }

    public static string ReadCell(string path, int sheetNumber, string reference)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry($"xl/worksheets/sheet{sheetNumber}.xml")
            ?? throw new InvalidDataException($"sheet{sheetNumber}.xml이 없습니다.");
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        var cell = document.Descendants(Spreadsheet + "c")
            .FirstOrDefault(item => string.Equals((string?)item.Attribute("r"), reference, StringComparison.OrdinalIgnoreCase));
        return cell is null ? string.Empty : string.Concat(cell.Descendants(Spreadsheet + "t").Select(item => item.Value));
    }

    private static XElement Cell(string reference, string text) =>
        new(Spreadsheet + "c", new XAttribute("r", reference), new XAttribute("t", "inlineStr"),
            new XElement(Spreadsheet + "is", new XElement(Spreadsheet + "t", text)));

    private static void Write(ZipArchive archive, string path, XDocument document)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var stream = entry.Open();
        document.Save(stream, SaveOptions.DisableFormatting);
    }
}
