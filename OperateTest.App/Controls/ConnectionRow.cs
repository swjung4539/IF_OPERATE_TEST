using System.IO.Ports;
using OperateTest.App.Models;
using OperateTest.App.Services;
using TestEmulator.Controls;
using TestEmulator.Theme;

namespace OperateTest.App.Controls;

public sealed class ConnectionRow : UserControl
{
    private readonly ISerialConnectionManager _manager;
    private readonly SerialChannelDefinition _definition;
    private readonly SerialConnection _connection;
    private readonly AppComboBox _ports;
    private readonly AppComboBox _baudRates;
    private readonly AppComboBox _uartFormats;
    private readonly AppButton _connectButton;
    private readonly Label _status;
    private readonly StatusDot _dot;

    public ConnectionRow(ISerialConnectionManager manager, SerialChannelDefinition definition)
    {
        _manager = manager;
        _definition = definition;
        _connection = manager.Get(definition.Key);
        Height = 66;
        BackColor = Colors.Surface;

        Controls.Add(new Label { Location = new Point(12, 10), Size = new Size(220, 22), Font = Fonts.ButtonText, ForeColor = Colors.TextPrimary, Text = definition.DisplayName });
        Controls.Add(new Label { Location = new Point(12, 35), Size = new Size(220, 20), Font = Fonts.Caption, ForeColor = Colors.TextSecondary, Text = InterfaceOnly(definition.InterfaceName) });

        _ports = new AppComboBox { Location = new Point(240, 16), Size = new Size(165, Metrics.InputHeight) };
        _baudRates = new AppComboBox { Location = new Point(420, 16), Size = new Size(125, Metrics.InputHeight) };
        _uartFormats = new AppComboBox { Location = new Point(560, 16), Size = new Size(125, Metrics.InputHeight) };
        _connectButton = new AppButton { Variant = AppButtonVariant.OutlinePrimary, Location = new Point(706, 13), Size = new Size(118, 40), Text = "연결" };
        _dot = new StatusDot { Location = new Point(850, 28), Size = new Size(9, 9) };
        _status = new Label { Location = new Point(870, 17), Size = new Size(240, 32), Font = Fonts.Caption, ForeColor = Colors.TextMuted, TextAlign = ContentAlignment.MiddleLeft, Text = "연결 안 됨" };
        Controls.Add(_ports);
        Controls.Add(_baudRates);
        Controls.Add(_uartFormats);
        Controls.Add(_connectButton);
        Controls.Add(_dot);
        Controls.Add(_status);

        var baudRates = new[] { "9600", "19200", "38400", "57600", "115200" };
        _baudRates.SetItems(baudRates, Array.IndexOf(baudRates, definition.Options.BaudRate.ToString()));
        var formats = new[] { "8-N-1", "8-E-1", "8-O-1" };
        _uartFormats.SetItems(formats, Array.IndexOf(formats, ToFormat(definition.Options)));

        _connectButton.Click += ConnectButtonOnClick;
        _connection.StateChanged += ConnectionOnStateChanged;
        RefreshPorts();
        RenderState(new SerialConnectionSnapshot(definition.Key, _connection.State, _connection.PortName, _connection.Message));
    }

    public string ChannelKey => _definition.Key;

    public void RefreshPorts()
    {
        var selected = _ports.SelectedItem;
        var ports = _manager.GetPortNames();
        var index = ports.ToList().FindIndex(p => string.Equals(p, _connection.PortName ?? selected, StringComparison.OrdinalIgnoreCase));
        _ports.SetItems(ports, index >= 0 ? index : 0);
        _connectButton.Enabled = _connection.IsConnected || ports.Count > 0;
    }

    private async void ConnectButtonOnClick(object? sender, EventArgs e)
    {
        _connectButton.Enabled = false;
        try
        {
            if (_connection.IsConnected)
            {
                await _manager.DisconnectAsync(_definition.Key);
                return;
            }

            if (_ports.SelectedItem is not string portName)
                return;

            var selectedDefinition = _definition with { Options = ReadOptions() };
            await _manager.ConnectAsync(selectedDefinition, portName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "포트 연결", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _connectButton.Enabled = _connection.IsConnected || _ports.Items.Count > 0;
        }
    }

    private void ConnectionOnStateChanged(object? sender, SerialConnectionSnapshot snapshot)
    {
        if (IsDisposed)
            return;
        if (InvokeRequired)
        {
            BeginInvoke(() => RenderState(snapshot));
            return;
        }
        RenderState(snapshot);
    }

    private void RenderState(SerialConnectionSnapshot snapshot)
    {
        _status.Text = snapshot.Message;
        (_status.ForeColor, _dot.DotColor) = snapshot.State switch
        {
            SerialConnectionState.Connected => (Colors.Success, Colors.Success),
            SerialConnectionState.Error => (Colors.Danger, Colors.Danger),
            _ => (Colors.TextMuted, Colors.TextMuted)
        };
        _connectButton.Text = snapshot.State == SerialConnectionState.Connected ? "연결 해제" : "연결";
        _connectButton.Variant = snapshot.State == SerialConnectionState.Connected ? AppButtonVariant.Secondary : AppButtonVariant.OutlinePrimary;
        var canEdit = snapshot.State != SerialConnectionState.Connected;
        _ports.Enabled = canEdit;
        _baudRates.Enabled = canEdit;
        _uartFormats.Enabled = canEdit;
        _connectButton.Enabled = snapshot.State == SerialConnectionState.Connected || _ports.Items.Count > 0;
        Invalidate();
    }

    private SerialConnectionOptions ReadOptions()
    {
        if (!int.TryParse(_baudRates.SelectedItem?.ToString(), out var baudRate))
            baudRate = _definition.Options.BaudRate;

        var parity = _uartFormats.SelectedItem?.ToString() switch
        {
            "8-E-1" => Parity.Even,
            "8-O-1" => Parity.Odd,
            _ => Parity.None
        };

        return _definition.Options with
        {
            BaudRate = baudRate,
            DataBits = 8,
            Parity = parity,
            StopBits = StopBits.One
        };
    }

    private static string ToFormat(SerialConnectionOptions options) =>
        options.Parity switch
        {
            Parity.Even => "8-E-1",
            Parity.Odd => "8-O-1",
            _ => "8-N-1"
        };

    private static string InterfaceOnly(string value)
    {
        var separator = value.IndexOf('·');
        return separator < 0 ? value : value[..separator].Trim();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _connection.StateChanged -= ConnectionOnStateChanged;
        base.Dispose(disposing);
    }

    private sealed class StatusDot : Control
    {
        public Color DotColor { get; set; } = Colors.TextMuted;

        protected override void OnPaint(PaintEventArgs e)
        {
            using var brush = new SolidBrush(DotColor);
            e.Graphics.FillEllipse(brush, ClientRectangle);
        }
    }
}
