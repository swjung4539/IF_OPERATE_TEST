namespace TestMode.App;

internal sealed class FaultView : UserControl
{
    private readonly Label response;
    private readonly Label expected;
    private readonly Button open;
    private readonly Button close;
    internal bool? RequestedNormal { get; private set; }

    public FaultView(Action<bool> command)
    {
        Dock = DockStyle.Fill;
        var card = new Surface { Dock = DockStyle.Fill, Margin = Padding.Empty };
        open = Ui.Button("faultOpen", "OPEN (FAULT)", () => command(false));
        close = Ui.Button("faultClose", "CLOSE (NORMAL)", () => command(true), primary: true);
        open.Enabled = close.Enabled = false;
        open.Width = close.Width = 218;
        response = Ui.Label("—", Ui.Bold);
        response.Name = "faultResponse";
        expected = Ui.Label("명령 대기", color: Ui.Muted);
        var commands = Ui.Row(open, close);
        commands.Padding = new Padding(0, 18, 0, 20);
        var measurement = Ui.Label("TB18-1  EXT_COM    ↔    TB18-2  EXT_FLT", Ui.Bold, Ui.Blue);
        var content = Ui.Stack(
            Ui.Label("FAULT 출력 제어", Ui.Bold),
            Ui.Label("FAULT 또는 NORMAL 상태를 출력하고, 테스터로 직접 도통을 확인하세요.", color: Ui.Muted),
            commands, Ui.Label("보드 제어 응답", color: Ui.Muted), response, expected,
            Ui.Label("측정 위치", Ui.Bold), measurement,
            Ui.Label("OPEN (FAULT) : 개방     /     CLOSE (NORMAL) : 도통 예상", color: Ui.Muted));
        var note = Ui.Note("보드 제어 응답과 실제 도통은 별개입니다. 접점의 외부 전압·부하를 분리하고 측정하세요.");
        note.Dock = DockStyle.Bottom;
        card.Controls.Add(content);
        card.Controls.Add(note);
        Controls.Add(card);
    }

    internal void SetConnected(bool connected)
    {
        open.Enabled = close.Enabled = connected;
    }

    internal void SetCommandResult(bool normal, bool success)
    {
        RequestedNormal = normal;
        response.Text = success
            ? (normal ? "CLOSE (NORMAL) 적용 응답 수신" : "OPEN (FAULT) 적용 응답 수신")
            : "응답 없음 · 적용 여부 미확인";
        response.ForeColor = success ? Ui.Blue : Ui.Red;
        expected.Text = success
            ? (normal ? "예상 접점 상태 : 도통" : "예상 접점 상태 : 개방")
            : "접점 상태를 직접 확인하세요.";
    }

    internal void SetPreview(string state)
    {
        SetCommandResult(state == "CLOSE", state is "OPEN" or "CLOSE");
    }
}
