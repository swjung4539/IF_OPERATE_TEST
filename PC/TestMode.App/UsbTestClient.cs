using System.Globalization;
using System.IO.Ports;
using System.Text;

namespace TestMode.App;

internal sealed class UsbTestClient : IDisposable
{
    private readonly SemaphoreSlim commandGate = new(1, 1);
    private SerialPort? port;
    private Board? connectedBoard;

    public bool IsConnected => port?.IsOpen == true;

    public async Task<bool> EnsureConnectedAsync(Board expectedBoard = Board.Main)
    {
        if (IsConnected && connectedBoard == expectedBoard)
            return true;
        if (IsConnected)
            Disconnect();

        foreach (var portName in SerialPort.GetPortNames().OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            SerialPort? candidate = null;
            try
            {
                candidate = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
                {
                    Handshake = Handshake.None,
                    ReadTimeout = 180,
                    WriteTimeout = 180,
                    NewLine = "\n",
                    Encoding = Encoding.ASCII,
                    DtrEnable = false,
                    RtsEnable = false
                };
                candidate.Open();
                port = candidate;
                var response = await SendAsync("IDENTIFY");
                if (response == (expectedBoard == Board.Main ? "DEVICE=MAIN" : "DEVICE=DIO"))
                {
                    connectedBoard = expectedBoard;
                    return true;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (InvalidOperationException) { }
            catch (TimeoutException) { }
            finally
            {
                if (port != candidate || candidate?.IsOpen != true)
                    candidate?.Dispose();
            }

            port?.Dispose();
            port = null;
        }

        return false;
    }

    public async Task<int?> ReadRotaryAsync()
    {
        var response = await SendAsync("GET_ROTARY");
        if (response is null || !response.StartsWith("ROTARY=", StringComparison.Ordinal))
            return null;

        return int.TryParse(response[7..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
            ? value & 0x0F
            : null;
    }

    public async Task<(bool Switch1, bool Switch2)?> ReadDioSwitchAsync()
    {
        var response = await SendAsync("GET_SWITCH");
        if (response is null || !response.StartsWith("SW1=", StringComparison.Ordinal))
            return null;

        var fields = response.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var values = fields.Select(field => field.Split('=', 2)).Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);
        if (!int.TryParse(values.GetValueOrDefault("SW1"), out var switch1)
            || !int.TryParse(values.GetValueOrDefault("SW2"), out var switch2)
            || switch1 is < 0 or > 1 || switch2 is < 0 or > 1)
            return null;
        return (switch1 != 0, switch2 != 0);
    }

    public async Task<DioFirmwareInfo?> ReadDioFirmwareInfoAsync()
    {
        var response = await SendAsync("GET_FIRMWARE_INFO", 2000);
        if (response is null || !response.StartsWith("DEVICE=DIO", StringComparison.Ordinal))
            return null;

        var values = response.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(field => field.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);
        if (!values.TryGetValue("VERSION", out var version)
            || !uint.TryParse(values.GetValueOrDefault("SIZE"), NumberStyles.None, CultureInfo.InvariantCulture, out var size)
            || !uint.TryParse(values.GetValueOrDefault("CRC"), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var crc))
            return null;
        return new DioFirmwareInfo(version, size, crc);
    }

    public async Task<bool> SetFaultAsync(bool normal)
    {
        var response = await SendAsync(normal ? "FAULT CLOSE" : "FAULT OPEN");
        return response == (normal ? "FAULT=CLOSE" : "FAULT=OPEN");
    }

    private async Task<string?> SendAsync(string command, int readTimeout = 180)
    {
        await commandGate.WaitAsync();
        try
        {
            if (!IsConnected)
                return null;

            var current = port!;
            current.ReadTimeout = readTimeout;
            current.DiscardInBuffer();
            current.Write(command + "\n");
            return await Task.Run(() => current.ReadLine().Trim());
        }
        catch (IOException) { Disconnect(); return null; }
        catch (InvalidOperationException) { Disconnect(); return null; }
        catch (TimeoutException) { return null; }
        finally
        {
            commandGate.Release();
        }
    }

    private void Disconnect()
    {
        port?.Dispose();
        port = null;
        connectedBoard = null;
    }

    public void Dispose()
    {
        Disconnect();
        commandGate.Dispose();
    }
}

internal sealed record DioFirmwareInfo(string Version, uint Size, uint Crc);
