using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ScanControl.Core;

public record Channel
{
    public int Number { get; set; }
    public int Bank => (Number - 1) / 50 + 1;
    public decimal FrequencyMHz { get; set; }
    public string Name { get; set; } = "";
    public string Modulation { get; set; } = "NFM";
    public string Step { get; set; } = "5K";
    public bool Trunk { get; set; }
    public bool Lockout { get; set; }
    public bool Attenuator { get; set; }
    public bool Record { get; set; }
    public bool BeepAlert { get; set; }
    public string Delay { get; set; } = "+2";
    public int Tone { get; set; }
    public bool ToneLock { get; set; }
}

public record Project
{
    public int Version { get; set; } = 1;
    public string Model { get; set; } = "BC780XLT";
    public bool Simulation { get; set; }
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<Channel> Channels { get; set; } = [];
}

public class Settings
{
    public string Port { get; set; } = "COM3";
    public int Baud { get; set; } = 9600;
    public int TimeoutMs { get; set; } = 2000;
    public int PollMs { get; set; } = 1500;
    public bool AutoConnect { get; set; }
    public string AudioDeviceId { get; set; } = "";
    public int InputLevel { get; set; } = 50;
    public bool Mute { get; set; }
    public bool AutoBackup { get; set; } = true;
    public bool Logging { get; set; } = true;
    public string Theme { get; set; } = "Dunkel";
    public bool Maximized { get; set; }
    public string Directory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ScanControl");
}

public static class Validation
{
    public static readonly string[] Modes = ["AM", "FM", "NFM", "WFM"];
    public static readonly string[] Steps = ["5K", "7.5K", "10K", "12.5K", "25K", "50K", "100K"];
    public static readonly string[] Delays = ["OFF", "+1", "+2", "+4", "+-", "-2", "-5", "-10"];
    public static void Channel(Channel c)
    {
        if (c.Number is < 1 or > 500) throw new InvalidDataException("Kanalnummer muss zwischen 1 und 500 liegen.");
        // Empty memory is represented by 0. US BC780XLT cellular gaps are excluded.
        var f = c.FrequencyMHz;
        if (f != 0 && !(f >= 25 && f <= 512 || f >= 806 && f <= 823.9875m || f >= 849.0125m && f <= 868.9875m || f >= 894.0125m && f <= 956 || f >= 1240 && f <= 1300))
            throw new InvalidDataException($"Kanal {c.Number}: Frequenz außerhalb des unterstützten Empfangsbereichs.");
        if (f * 10000 != decimal.Truncate(f * 10000)) throw new InvalidDataException("Frequenz muss in 100-Hz-Einheiten darstellbar sein.");
        if (c.Name is null || c.Name.Length > 16 || c.Name.Any(x => x < 32 || x > 126)) throw new InvalidDataException("Kanalname: maximal 16 druckbare ASCII-Zeichen.");
        if (!Modes.Contains(c.Modulation) || !Steps.Contains(c.Step) || !Delays.Contains(c.Delay)) throw new InvalidDataException("Ungültige Modulation, Schrittweite oder Verzögerung.");
        if (c.Tone is < 0 or > 142) throw new InvalidDataException("Ungültige CTCSS/DCS-Tonnummer (0–142).");
    }
    public static void Project(Project p)
    {
        if (p.Version != 1 || p.Model != "BC780XLT" || p.Channels is null || p.Channels.Count > 500) throw new InvalidDataException("Unbekanntes oder beschädigtes Projektformat.");
        if (p.Channels.Select(x => x.Number).Distinct().Count() != p.Channels.Count) throw new InvalidDataException("Doppelte Kanalnummern.");
        foreach (var c in p.Channels) Channel(c);
    }
}

public record ScannerDisplay(string Channel, string Frequency, string Line1, string Line2, string Mode);
public interface ICommandTransport : IDisposable
{
    Task<string> ExchangeAsync(string command, CancellationToken cancellationToken = default);
}
public interface IScanner : IDisposable
{
    bool Simulation { get; }
    Task<string> IdentifyAsync(CancellationToken ct = default);
    Task<Channel> ReadAsync(int number, CancellationToken ct = default);
    Task WriteAsync(Channel channel, CancellationToken ct = default);
    Task<ScannerDisplay> DisplayAsync(CancellationToken ct = default);
    Task KeyAsync(string code, CancellationToken ct = default);
}

public class SimulatedScanner : IScanner
{
    private readonly Dictionary<int, Channel> channels = Enumerable.Range(1, 500).ToDictionary(n => n, n => new Channel { Number = n, FrequencyMHz = n is >= 121 and <= 130 ? 149.125m + (n - 125) * .025m : 0, Name = n is >= 121 and <= 130 ? $"Demo Kanal {n}" : "" });
    private int current = 125;
    private bool scan;
    private string entry = "";
    public bool Simulation => true;
    public Task<string> IdentifyAsync(CancellationToken ct = default) => Task.FromResult("SIMULATION · BC780XLT · kein Hardwaretest");
    public async Task<Channel> ReadAsync(int number, CancellationToken ct = default) { await Task.Delay(3, ct); return channels[number] with { }; }
    public async Task WriteAsync(Channel channel, CancellationToken ct = default) { Validation.Channel(channel); await Task.Delay(10, ct); channels[channel.Number] = channel with { }; }
    public Task<ScannerDisplay> DisplayAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var noChannels = false;
        if (scan)
        {
            var next = Enumerable.Range(1, 500).Select(offset => (current - 1 + offset) % 500 + 1)
                .FirstOrDefault(n => channels[n].FrequencyMHz != 0 && !channels[n].Lockout);
            if (next == 0) noChannels = true; else current = next;
        }
        var c = channels[current];
        return Task.FromResult(new ScannerDisplay(entry.Length > 0 ? entry + "_" : current.ToString("000"), c.FrequencyMHz.ToString("F4", System.Globalization.CultureInfo.InvariantCulture), noChannels ? "KEINE SCAN-KANAELE" : c.Name, "SIMULATION", scan ? "SCAN" : "HOLD"));
    }
    public Task KeyAsync(string code, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (code.StartsWith("02 ") && code.Length == 4 && char.IsAsciiDigit(code[3]))
        {
            if (entry.Length >= 3) entry = "";
            entry += code[3]; scan = false; return Task.CompletedTask;
        }
        switch (code)
        {
            case "00": entry = ""; scan = true; break;
            case "01":
            case "04":
                if (entry.Length > 0)
                {
                    var n = int.Parse(entry); entry = "";
                    if (n is < 1 or > 500) throw new InvalidOperationException("Simulierte Kanalwahl: 1–500 eingeben und E oder MAN drücken.");
                    current = n;
                }
                scan = false; break;
            case "07": entry = ""; if (!scan) current = current >= 500 ? 1 : current + 1; scan = false; break;
            case "08": entry = ""; current = current <= 1 ? 500 : current - 1; scan = false; break;
            case "06": channels[current].Lockout = !channels[current].Lockout; break;
            default: throw new NotSupportedException("Diese Taste ist im Simulationsmodus nicht implementiert.");
        }
        return Task.CompletedTask;
    }
    public void Dispose() { }
}
