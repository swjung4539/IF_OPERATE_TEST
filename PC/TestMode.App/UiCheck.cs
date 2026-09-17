using System.Text.Json;

namespace TestMode.App;

// Explicit, opt-in UI verification. This harness has no device or flashing access.
internal static class UiCheck
{
    internal static async Task RunAsync(MainForm form, string outputPath)
    {
        var results = new List<object>();
        var output = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(output);
        try
        {
            await Task.Delay(200);
            await Capture("01-home");
            Click("selectTest");
            Require(form.CurrentPage == Page.Boards, "TEST entry opens board selection");
            await Capture("02-boards");
            Click("selectMain");
            Require(form.CurrentPage == Page.Switch && form.CurrentBoard == Board.Main, "MAIN navigation");
            Require(Find<Label>("switchValue").Text == "—", "No fabricated switch value before receive");
            Require(Find<Label>("portSelection").Text == "USB 자동 인식", "USB is detected internally without a port control");
            await Capture("03-main-switch-waiting");
            var rotary = Find<SwitchView>("activeView");
            rotary.SetPreview(10, "입력 비트   0 1 0 1  ·  예시");
            Require(Find<Label>("switchValue").Text.Contains("10"), "Rotary example is displayed");
            form.PreviewConnection("MAIN 수신 상태 표현");
            await Capture("04-main-switch-example");
            rotary.SetPreview(null, "입력 비트   — — — —");
            form.PreviewConnection("연결 끊김 · 수신값 없음");
            Require(Find<Label>("switchValue").Text == "—", "Disconnected display clears old reading");
            await Capture("05-main-switch-disconnected");
            Click("navFault");
            Require(!Find<Button>("faultOpen").Enabled && !Find<Button>("faultClose").Enabled, "Relay commands wait for USB connection");
            await Capture("06-fault-waiting");
            Find<FaultView>("activeView").SetPreview("CLOSE");
            Require(Find<Label>("faultResponse").Text == "CLOSE (NORMAL) 적용 응답 수신", "Relay response is not a continuity verdict");
            await Capture("07-fault-close-example");
            Find<FaultView>("activeView").SetPreview("OPEN");
            Require(Find<Label>("faultResponse").Text == "OPEN (FAULT) 적용 응답 수신", "OPEN response");
            Find<FaultView>("activeView").SetPreview("timeout");
            await Capture("08-fault-no-response-example");
            Click("navFirmware");
            var updater = Find<FirmwareView>("activeView");
            Require(!Find<Button>("updateFirmware").Enabled, "No update without transport");
            await Capture("09-main-firmware");
            var fixture = Path.Combine(output, "ui-only-fixtures");
            Directory.CreateDirectory(fixture);
            File.WriteAllText(Path.Combine(fixture, "main_slot_a.hex"), ":00000001FF\n");
            File.WriteAllText(Path.Combine(fixture, "main_slot_b.HEX"), ":00000001FF\n");
            File.WriteAllText(Path.Combine(fixture, "ignored.txt"), "UI fixture; not firmware.");
            updater.SelectFolder(fixture);
            Require(Find<ListBox>("hexCandidates").Items.Count == 2 && updater.SelectedPath is null, "Multiple HEX files require explicit selection");
            Find<ListBox>("hexCandidates").SelectedIndex = 0;
            Require(updater.SelectedPath?.EndsWith("main_slot_a.hex") == true, "Folder selection updates selected HEX");
            await Capture("10-firmware-selected");
            updater.SetDisplay(FirmwareDisplay.Uploading);
            Require(!Find<Button>("chooseHex").Enabled && !Find<Button>("chooseFolder").Enabled && !Find<ListBox>("hexCandidates").Enabled, "Busy state locks file selection");
            await Capture("11-firmware-upload-example");
            updater.SetDisplay(FirmwareDisplay.Verifying);
            Require(Find<ProgressBar>("updateProgress").Value == 92, "Verification progress state");
            updater.SetDisplay(FirmwareDisplay.Latest);
            Require(Find<Label>("updateResult").Text == "최신 버전입니다.", "Exact latest-version wording");
            await Capture("12-firmware-latest-example");
            updater.SetDisplay(FirmwareDisplay.Success);
            Require(Find<Label>("updateResult").Text == "펌웨어 업데이트 성공", "Exact success wording");
            await Capture("13-firmware-success-example");
            updater.SetDisplay(FirmwareDisplay.Failed);
            await Capture("14-firmware-failure-example");
            updater.SelectFile(Path.Combine(fixture, "ignored.txt"));
            Require(updater.SelectedPath is null, "Non-HEX selection rejected");
            updater.SelectFile(Path.Combine(fixture, "missing.hex"));
            Require(updater.SelectedPath is null, "Missing selection rejected");
            var empty = Path.Combine(fixture, "empty");
            Directory.CreateDirectory(empty);
            updater.SelectFolder(empty);
            Require(Find<ListBox>("hexCandidates").Items.Count == 0 && updater.SelectedPath is null, "Empty folder clears prior selection");
            Click("chooseBoard");
            Click("selectDio");
            Require(form.CurrentBoard == Board.Dio && form.CurrentPage == Page.Switch, "DIO navigation");
            Require(!form.Controls.Find("navFault", true).Any(), "DIO excludes FAULT");
            await Capture("15-dio-switch-waiting");
            Find<SwitchView>("activeView").SetPreview(4, "스위치 1   ON     스위치 2   ON", true, true);
            await Capture("16-dio-switch-example");
            Click("navFirmware");
            Require(Find<FirmwareView>("activeView").SelectedPath is null, "MAIN file does not leak into DIO");
            await Capture("17-dio-firmware");
            Click("goHome");
            Click("selectBootloader");
            Require(form.CurrentPage == Page.Bootloader, "Bootloader navigation");
            await Capture("18-bootloader");
            Click("targetMain");
            Require(form.CurrentBoard == Board.Main, "Bootloader board selection");
            Find<FirmwareView>("activeView").SelectFile(Path.Combine(fixture, "main_slot_a.hex"));
            Click("targetDio");
            Require(form.CurrentBoard == Board.Dio && Find<FirmwareView>("activeView").SelectedPath is null, "Board change clears update selection");
            form.Size = form.MinimumSize;
            await Capture("19-minimum-bootloader");
            Click("goHome");
            await Capture("20-minimum-home");
            Click("selectTest");
            Click("selectMain");
            await Capture("21-minimum-main-switch");
            Click("navFault");
            await Capture("22-minimum-fault");
            for (var i = 0; i < 12; i++)
            {
                Click("goHome"); Click("selectTest"); Click("selectDio"); Click("navFirmware");
            }
            Require(Application.OpenForms.Count == 1, "Navigation does not accumulate windows");
            var forbidden = All(form).OfType<Label>().Any(c => c.Text.Contains("SW8") || c.Text.Contains("PASS") || c.Text.Contains("선택한 펌웨어와 다릅니다"));
            Require(!forbidden, "Excluded inspection labels absent");
            File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { Passed = true, DeviceAccess = false, Checks = results }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { Passed = false, Error = ex.ToString(), Checks = results }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { form.Close(); }

        T Find<T>(string name) where T : Control => form.Controls.Find(name, true).OfType<T>().FirstOrDefault() ?? throw new InvalidOperationException($"Control missing: {name}");
        void Click(string name) => Find<Button>(name).PerformClick();
        void Require(bool condition, string check)
        {
            if (!condition) throw new InvalidOperationException(check);
            results.Add(new { Check = check, Passed = true });
        }
        async Task Capture(string name)
        {
            await Task.Delay(100);
            form.PerformLayout();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(output, name + ".png"));
            results.Add(new { Screenshot = name, Width = form.Width, Height = form.Height });
        }
    }

    private static IEnumerable<Control> All(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in All(child)) yield return descendant;
        }
    }
}
