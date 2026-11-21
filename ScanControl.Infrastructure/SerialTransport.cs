using System.IO.Ports;
using System.Text;
using ScanControl.Core;

namespace ScanControl.Infrastructure;

public sealed class SerialTransport : ICommandTransport
{
    private readonly SerialPort port;
    private readonly SemaphoreSlim queue = new(1, 1);
    private readonly Action<string>? log;
    private bool faulted;
    public SerialTransport(Settings settings, Action<string>? log = null)
    {
        if (!new[] { 2400, 4800, 9600, 19200 }.Contains(settings.Baud)) throw new ArgumentException("Nicht unterstützte Baudrate.");
        this.log = log;
        port = new SerialPort(settings.Port, settings.Baud, Parity.None, 8, StopBits.One)
        {
            Handshake = Handshake.None, Encoding = Encoding.ASCII, NewLine = "\r",
            ReadTimeout = Math.Clamp(settings.TimeoutMs, 250, 30000), WriteTimeout = Math.Clamp(settings.TimeoutMs, 250, 30000),
            DtrEnable = false, RtsEnable = false
        };
        try { port.Open(); port.DiscardInBuffer(); } catch { port.Dispose(); throw; }
    }
    public async Task<string> ExchangeAsync(string command, CancellationToken cancellationToken = default)
    {
        if (command.Any(c => c < 32 || c > 126)) throw new ArgumentException("Befehl enthält unzulässige Zeichen.");
        await queue.WaitAsync(cancellationToken);
        try
        {
            if (faulted || !port.IsOpen) throw new IOException("Verbindung unterbrochen. Bitte neu verbinden.");
            cancellationToken.ThrowIfCancellationRequested();
            // SerialPort synchronous reads are bounded by ReadTimeout and run off the UI thread.
            var response = await Task.Run(() =>
            {
                log?.Invoke("> " + command);
                var timeout = port.WriteTimeout;
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                port.Write(command + "\r");
                var result = new StringBuilder();
                while (true)
                {
                    var remaining = timeout - (int)elapsed.ElapsedMilliseconds;
                    if (remaining <= 0) throw new TimeoutException("Scannerantwort unvollständig; Zeitlimit überschritten.");
                    port.ReadTimeout = remaining;
                    var b = port.ReadByte();
                    if (b == 13) break;
                    if (b < 32 || b > 126 || result.Length >= 1024) throw new InvalidDataException("Beschädigter serieller Datenrahmen.");
                    result.Append((char)b);
                }
                log?.Invoke("< " + result);
                return result.ToString();
            });
            await Task.Delay(60); // documented inter-command wait; hold queue until elapsed
            cancellationToken.ThrowIfCancellationRequested();
            if (response is "ERR" or "NG" or "FER" or "ORER") throw new IOException("Scanner meldet " + response + " für " + command);
            return response;
        }
        catch
        {
            // A late response must never be associated with the next command.
            faulted = true;
            port.Close();
            throw;
        }
        finally { queue.Release(); }
    }
    public void Dispose() => port.Dispose();
}
