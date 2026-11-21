namespace ScanControl.Core;

public class TransferService(IScanner scanner)
{
    // The owner holds this gate across backup, review, write and verification.
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public async Task<List<Channel>> ReadAsync(IEnumerable<int> numbers, IProgress<string>? progress, CancellationToken ct)
    {
        var result = new List<Channel>();
        var all = numbers.ToArray();
        for (int i = 0; i < all.Length; i++)
        {
            progress?.Report($"Lesen {i + 1}/{all.Length} · Kanal {all[i]}");
            result.Add(await scanner.ReadAsync(all[i], ct));
        }
        return result;
    }
    public async Task WriteAndVerifyAsync(IEnumerable<Channel> channels, IProgress<string>? progress, CancellationToken ct)
    {
        var all = channels.ToArray();
        foreach (var c in all) Validation.Channel(c);
        for (int i = 0; i < all.Length; i++)
        {
            var c = all[i];
            progress?.Report($"Schreiben und prüfen {i + 1}/{all.Length} · Kanal {c.Number}");
            await scanner.WriteAsync(c, ct);
            var actual = await scanner.ReadAsync(c.Number, ct);
            if (actual != c) throw new IOException($"Verifikation fehlgeschlagen: Kanal {c.Number}. Vorgang unvollständig; Sicherung verwenden.");
        }
    }
}
