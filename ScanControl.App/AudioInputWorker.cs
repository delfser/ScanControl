using System.Collections.Concurrent;

namespace ScanControl.App;

/// <summary>All Core Audio COM access stays on one background STA, created only on demand.</summary>
internal sealed class AudioInputWorker : IDisposable
{
    private readonly BlockingCollection<Action> queue = new();
    private AudioInput? input;
    private readonly Lazy<Thread> thread;
    private bool disposed, unavailable;
    internal bool IsStarted => thread.IsValueCreated;
    public AudioInputWorker()
    {
        thread = new(() =>
        {
            var worker = new Thread(() =>
            {
                try
                {
                    foreach (var item in queue.GetConsumingEnumerable())
                    {
                        item();
                    }
                }
                catch (Exception error) { StartupDiagnostics.Write("Audio worker failure: " + error); }
                finally { input?.Dispose(); }
            }) { IsBackground = true, Name = "ScanControl Audio" };
            worker.SetApartmentState(ApartmentState.STA); worker.Start(); return worker;
        });
    }
    private async Task<T> Invoke<T>(Func<AudioInput, T> action)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (unavailable) throw new InvalidOperationException("Audioabfrage nach Zeitüberschreitung deaktiviert. Bitte ScanControl neu starten.");
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = thread.Value;
        queue.Add(() =>
        {
            try
            {
                if (disposed || unavailable) throw new InvalidOperationException("Audioabfrage wurde beendet.");
                completion.TrySetResult(action(input ??= new AudioInput()));
            }
            catch (Exception error) { completion.TrySetException(error); }
        });
        try { return await completion.Task.WaitAsync(TimeSpan.FromSeconds(8)); }
        catch (TimeoutException)
        {
            unavailable = true;
            _ = completion.Task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            throw new TimeoutException("Der Windows-Audiotreiber antwortet nicht. Scannerfunktionen bleiben nutzbar; Audiogeräte werden erst nach einem Programmneustart erneut abgefragt.");
        }
    }
    public Task<List<InputDevice>> DevicesAsync() => Invoke(input => input.Devices());
    public Task<(int Level, bool Mute)> SelectAsync(string id) => Invoke(input => { input.Select(id); return input.Read(); });
    public Task<(int Level, bool Mute)> ReadAsync() => Invoke(input => input.Read());
    public Task<(int Level, bool Mute)> SetAsync(int level, bool mute) => Invoke(input => { input.Set(level, mute); return input.Read(); });
    public void Dispose() { if (disposed) return; disposed = true; queue.CompleteAdding(); /* Never join a potentially blocked driver thread. */ }
}
