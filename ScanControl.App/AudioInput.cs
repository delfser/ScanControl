using NAudio.CoreAudioApi;

namespace ScanControl.App;

public record InputDevice(string Id, string Name)
{
    public override string ToString() => Name;
}
public sealed class AudioInput : IDisposable
{
    private readonly MMDeviceEnumerator enumerator = new();
    private MMDevice? device;
    public List<InputDevice> Devices()
    {
        var result = new List<InputDevice>();
        foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            result.Add(new(d.ID, d.FriendlyName)); d.Dispose();
        }
        return result.OrderByDescending(d => d.Name.Contains("line", StringComparison.OrdinalIgnoreCase)).ToList();
    }
    public void Select(string id)
    {
        device?.Dispose(); device = null;
        var candidate = enumerator.GetDevice(id);
        try
        {
            if (candidate.DataFlow != DataFlow.Capture) throw new InvalidOperationException("Nur Windows-Aufnahmegeräte sind zulässig.");
            _ = candidate.AudioEndpointVolume.MasterVolumeLevelScalar;
            device = candidate;
        }
        catch { candidate.Dispose(); throw new InvalidOperationException("Der Audiotreiber bietet keine verwendbare Eingangspegelsteuerung an."); }
    }
    public (int Level, bool Mute) Read()
    {
        if (device is null) throw new InvalidOperationException("Bitte ein Aufnahmegerät auswählen.");
        return ((int)Math.Round(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100), device.AudioEndpointVolume.Mute);
    }
    public void Set(int level, bool mute)
    {
        if (device is null) throw new InvalidOperationException("Bitte ein Aufnahmegerät auswählen.");
        try
        {
            device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(level, 0, 100) / 100f;
            device.AudioEndpointVolume.Mute = mute;
            var actual = Read();
            if (Math.Abs(actual.Level - level) > 1 || actual.Mute != mute) throw new InvalidOperationException("Treiber übernimmt Eingangspegel oder Mute nicht.");
        }
        catch (Exception e) { throw new InvalidOperationException("Eingangspegel/Mute wird vom Aufnahmegerät nicht unterstützt: " + e.Message, e); }
    }
    public void Dispose() { device?.Dispose(); enumerator.Dispose(); }
}
