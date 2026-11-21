using System.Globalization;
using System.Text;
using System.Text.Json;
using ScanControl.Core;

namespace ScanControl.Infrastructure;

public static class ProjectFiles
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static async Task SaveAsync(string path, Project project)
    {
        Validation.Project(project);
        project.SavedAt = DateTimeOffset.UtcNow;
        await AtomicWriteAsync(path, JsonSerializer.Serialize(project, Json));
    }
    public static async Task<Project> LoadAsync(string path)
    {
        var p = JsonSerializer.Deserialize<Project>(await File.ReadAllTextAsync(path), Json) ?? throw new InvalidDataException("Leeres Projekt.");
        Validation.Project(p); return p;
    }
    public static async Task AtomicWriteAsync(string path, string data)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temp, data, new UTF8Encoding(false)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static async Task<string> BackupAsync(string directory, Project p, string reason)
    {
        var path = Path.Combine(directory, "Sicherungen", $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{(p.Simulation ? "Simulation" : "Hardware")}-{reason}-{Guid.NewGuid().ToString("N")[..6]}.json");
        await SaveAsync(path, p); return path;
    }
    private static readonly string[] Headers = ["Number", "FrequencyMHz", "Name", "Modulation", "Step", "Trunk", "Lockout", "Attenuator", "Record", "BeepAlert", "Delay", "Tone", "ToneLock"];
    public static string ExportCsv(IEnumerable<Channel> channels)
    {
        static string Q(object? v) => "\"" + Convert.ToString(v, CultureInfo.InvariantCulture)!.Replace("\"", "\"\"") + "\"";
        var lines = new List<string> { string.Join(';', Headers) };
        foreach (var c in channels) lines.Add(string.Join(';', new object[] { c.Number, c.FrequencyMHz, c.Name, c.Modulation, c.Step, c.Trunk, c.Lockout, c.Attenuator, c.Record, c.BeepAlert, c.Delay, c.Tone, c.ToneLock }.Select(Q)));
        return string.Join("\r\n", lines);
    }
    public static List<Channel> ImportCsv(string text)
    {
        // RFC-style quoted fields, semicolon delimiter; no silent field truncation.
        var rows = new List<List<string>>(); var row = new List<string>(); var field = new StringBuilder();
        bool quoted = false, closed = false;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (quoted)
            {
                if (ch == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; } else { quoted = false; closed = true; } }
                else field.Append(ch);
            }
            else if (ch == '"' && field.Length == 0 && !closed) quoted = true;
            else if (ch == ';') { row.Add(field.ToString()); field.Clear(); closed = false; }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString()); rows.Add(row); row = []; field.Clear(); closed = false;
            }
            else { if (closed || ch == '"') throw new InvalidDataException("Ungültige CSV-Anführungszeichen."); field.Append(ch); }
        }
        if (quoted) throw new InvalidDataException("Nicht geschlossenes CSV-Feld.");
        if (field.Length > 0 || row.Count > 0 || closed) { row.Add(field.ToString()); rows.Add(row); }
        if (rows.Count == 0 || !rows[0].SequenceEqual(Headers)) throw new InvalidDataException("CSV-Spalten stimmen nicht mit dem ScanControl-Format überein.");
        var result = new List<Channel>();
        try
        {
            foreach (var r in rows.Skip(1))
            {
                if (r.Count != Headers.Length) throw new InvalidDataException("Unvollständige CSV-Zeile.");
                result.Add(new Channel { Number = int.Parse(r[0]), FrequencyMHz = decimal.Parse(r[1], CultureInfo.InvariantCulture), Name = r[2], Modulation = r[3], Step = r[4], Trunk = bool.Parse(r[5]), Lockout = bool.Parse(r[6]), Attenuator = bool.Parse(r[7]), Record = bool.Parse(r[8]), BeepAlert = bool.Parse(r[9]), Delay = r[10], Tone = int.Parse(r[11]), ToneLock = bool.Parse(r[12]) });
            }
        }
        catch (FormatException e) { throw new InvalidDataException("Ungültiger CSV-Wert.", e); }
        Validation.Project(new Project { Channels = result }); return result;
    }
}
