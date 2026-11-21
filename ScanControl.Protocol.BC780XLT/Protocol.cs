using System.Globalization;
using System.Text.RegularExpressions;
using ScanControl.Core;

namespace ScanControl.Protocol.BC780XLT;

public record ScannerIdentity(string Model, string SerialNumber, string ProtocolVersion);

public static partial class Parser
{
    [GeneratedRegex(@"\ASI (BC780XLT|UBC780XLT),([A-Za-z0-9]{1,32}),(\d{3})\z")]
    private static partial Regex IdentityPattern();
    public static ScannerIdentity Identity(string response)
    {
        var match = IdentityPattern().Match(response);
        if (!match.Success) throw new InvalidDataException("Kein bestätigter BC780XLT/UBC780XLT oder unvollständige Geräteantwort: " + response);
        return new(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value);
    }
    [GeneratedRegex(@"^C(\d{3}) F(\d{8}) T([NF]) D([NF]) L([NF]) A([NF]) R([NF]) N(\d{3})(L?)\s*$")]
    private static partial Regex ChannelPattern();
    public static Channel Channel(string response, int expected)
    {
        var m = ChannelPattern().Match(response);
        if (!m.Success || int.Parse(m.Groups[1].Value) != expected) throw new InvalidDataException("Unvollständige oder unerwartete Kanalantwort: " + response);
        return new Channel { Number = expected, FrequencyMHz = decimal.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) / 10000m,
            Trunk = m.Groups[3].Value == "N", Delay = m.Groups[4].Value == "N" ? "+2" : "OFF", Lockout = m.Groups[5].Value == "N",
            Attenuator = m.Groups[6].Value == "N", Record = m.Groups[7].Value == "N", Tone = int.Parse(m.Groups[8].Value), ToneLock = m.Groups[9].Value == "L" };
    }
    public static string Value(string response, string prefix)
    {
        if (!response.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("Unerwartete Scannerantwort: " + response);
        return response[prefix.Length..].TrimEnd();
    }
    public static string Lcd(string response, string prefix)
    {
        Value(response, prefix + " ");
        var start = response.IndexOf('['); var end = response.IndexOf(']', start + 1);
        if (start < 0 || end < 0) throw new InvalidDataException("Ungültige LCD-Antwort.");
        return response[(start + 1)..end].Trim();
    }
}

// Independently implemented from Uniden PCi_bc780_v104, printed pages 1, 8, 28–30, 34, 38, 42–47.
public class Bc780Scanner(ICommandTransport transport) : IScanner
{
    private readonly Dictionary<int, (DateTimeOffset At, decimal Frequency, string Name)> displayMemory = [];
    public bool Simulation => false;
    private Task<string> Send(string command, CancellationToken ct) => transport.ExchangeAsync(command, ct);
    private async Task Ok(string command, CancellationToken ct)
    {
        if (await Send(command, ct) != "OK") throw new InvalidDataException("Keine Bestätigung für " + command);
    }
    public async Task<string> IdentifyAsync(CancellationToken ct = default)
    {
        var s = await Send("SI", ct);
        _ = Parser.Identity(s);
        return s;
    }
    public async Task<Channel> ReadAsync(int number, CancellationToken ct = default)
    {
        if (number is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(number));
        var c = Parser.Channel(await Send($"PM{number:000}", ct), number);
        c.Name = Parser.Value(await Send($"TA C {number:000}", ct), $"TA C {number:000} ");
        c.BeepAlert = (await Send($"BA C {number:000}", ct)) switch
        {
            var r when r == $"BAN C {number:000}" => true,
            var r when r == $"BAF C {number:000}" => false,
            _ => throw new InvalidDataException("Ungültige Beep-Alert-Antwort.")
        };
        // These are current-channel commands: explicitly select before reading.
        Parser.Channel(await Send($"MA{number:000}", ct), number);
        var rm = Parser.Value(await Send("RM", ct), "RM ");
        c.Modulation = rm == "---" && c.FrequencyMHz == 0 ? "NFM" : rm;
        c.Step = Parser.Value(await Send("ST", ct), "ST ");
        var delay = await Send("DL", ct);
        c.Delay = delay == "DLF" ? "OFF" : Parser.Value(delay, "DL ");
        var tone = await Send("CS", ct);
        if (!Regex.IsMatch(tone, @"^CS\d{3}L?$")) throw new InvalidDataException("Ungültige Tonantwort.");
        c.Tone = int.Parse(tone.Substring(2, 3)); c.ToneLock = tone.EndsWith('L');
        Validation.Channel(c);
        return c;
    }
    public async Task WriteAsync(Channel c, CancellationToken ct = default)
    {
        Validation.Channel(c);
        displayMemory.Remove(c.Number);
        // Trunk systems require bank/talkgroup configuration not covered by this channel editor.
        if (c.Trunk) throw new NotSupportedException("Trunking-Upload ist nicht freigegeben; Banksystem und Talkgroups werden nicht vollständig verwaltet.");
        Parser.Channel(await Send($"MA{c.Number:000}", ct), c.Number);
        await Ok("ST " + c.Step, ct);
        Parser.Channel(await Send($"PM{c.Number:000} {(c.FrequencyMHz * 10000).ToString("00000000", CultureInfo.InvariantCulture)}", ct), c.Number);
        Parser.Channel(await Send($"MA{c.Number:000}", ct), c.Number);
        await Ok("RM " + c.Modulation, ct);
        await Ok(c.Delay == "OFF" ? "DLF" : "DLN " + c.Delay, ct);
        await Ok("AT" + (c.Attenuator ? "N" : "F"), ct);
        await Ok("AR" + (c.Record ? "N" : "F"), ct);
        await Ok("LO" + (c.Lockout ? "N" : "F"), ct);
        await Ok($"CS{c.Tone:000}" + (c.ToneLock ? "L" : ""), ct);
        await Ok($"BA{(c.BeepAlert ? "N" : "F")} C {c.Number:000}", ct);
        await Ok($"TA C {c.Number:000} {c.Name}", ct);
    }
    public async Task<ScannerDisplay> DisplayAsync(CancellationToken ct = default)
    {
        var channel = Parser.Lcd(await Send("LCD CHN", ct), "CHN");
        var frequency = Parser.Lcd(await Send("LCD FRQ", ct), "FRQ");
        var line1 = Parser.Lcd(await Send("LCD LINE1", ct), "LINE1");
        var line2 = Parser.Lcd(await Send("LCD LINE2", ct), "LINE2");
        var mode = Parser.Value(await Send("MD", ct), "MD");
        var modeText = mode switch { "00" => "SCAN", "01" => "MAN / HOLD", "02" => "SEARCH", "03" => "SEARCH HOLD", _ => "MODUS " + mode };
        // LCD FRQ/LINE1 can be blank while scanning. PMnnn and TA only query memory;
        // never use MAnnn here, since that would interrupt the running scan.
        if ((string.IsNullOrWhiteSpace(frequency) || string.IsNullOrWhiteSpace(line1)) &&
            int.TryParse(channel, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number is >= 1 and <= 500)
        {
            if (!displayMemory.TryGetValue(number, out var memory) || DateTimeOffset.UtcNow - memory.At > TimeSpan.FromSeconds(10))
            {
                var stored = Parser.Channel(await Send($"PM{number:000}", ct), number);
                var name = Parser.Value(await Send($"TA C {number:000}", ct), $"TA C {number:000} ");
                memory = (DateTimeOffset.UtcNow, stored.FrequencyMHz, name);
                displayMemory[number] = memory;
            }
            var frequencyFromMemory = string.IsNullOrWhiteSpace(frequency);
            if (frequencyFromMemory) frequency = memory.Frequency == 0 ? "" : memory.Frequency.ToString("F4", CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(line1)) line1 = string.IsNullOrWhiteSpace(memory.Name) ? "KANAL " + number : memory.Name;
            // The LCD channel is only a sampled scan position, not proof of a received signal.
            line2 = frequencyFromMemory ? "KANALSPEICHER · " + modeText : line2;
        }
        return new(channel, frequency, line1, line2, modeText);
    }
    public Task KeyAsync(string code, CancellationToken ct = default)
    {
        if (!Regex.IsMatch(code, @"^(0[0-9]|1[0-4])( [0-9])?$")) throw new ArgumentException("Ungültiger Tastencode.");
        displayMemory.Clear();
        return Ok("KEY" + code, ct);
    }
    public void Dispose() => transport.Dispose();
}
