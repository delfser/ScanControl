using ScanControl.Core;

namespace ScanControl.Tests;

public class OfflineDevelopmentTests
{
    private static Project Demo(string name, bool simulation = true) => new() { Simulation = simulation, Channels = [new Channel { Number = 125, Name = name, FrequencyMHz = 149.125m }] };
    [Fact] public void HistoryRestoresDetachedValuesAndOrigin()
    {
        var history = new ProjectHistory(); var before = Demo("Before"); history.Record(before);
        before.Channels[0].Name = "Mutated outside history";
        var current = Demo("After", false); var restored = history.Undo(current);
        Assert.Equal("Before", restored.Channels[0].Name); Assert.True(restored.Simulation);
        restored.Channels[0].Name = "Mutated returned snapshot";
        var repeated = history.Redo(current);
        Assert.Equal("After", repeated.Channels[0].Name); Assert.False(repeated.Simulation);
    }
    [Fact] public void NewEditAfterUndoClearsRedo()
    {
        var history = new ProjectHistory(); history.Record(Demo("A")); history.Undo(Demo("B")); Assert.True(history.CanRedo);
        history.Record(Demo("C")); Assert.False(history.CanRedo);
    }
    [Fact] public void HistoryHasBoundedCapacity()
    {
        var history = new ProjectHistory(2); history.Record(Demo("A")); history.Record(Demo("B")); history.Record(Demo("C"));
        Assert.Equal("C", history.Undo(Demo("D")).Channels[0].Name);
        Assert.Equal("B", history.Undo(Demo("C")).Channels[0].Name); Assert.False(history.CanUndo);
    }
    [Fact] public void OpenNewProjectCanClearHistory()
    {
        var history = new ProjectHistory(); history.Record(Demo("A")); history.Undo(Demo("B")); history.Clear();
        Assert.False(history.CanUndo); Assert.False(history.CanRedo);
    }
    [Fact] public async Task SimulatorSelectsChannelThroughKeypad()
    {
        using var scanner = new SimulatedScanner();
        await scanner.KeyAsync("02 5"); await scanner.KeyAsync("02 0"); await scanner.KeyAsync("02 0"); await scanner.KeyAsync("04");
        Assert.Equal("500", (await scanner.DisplayAsync()).Channel);
        await scanner.KeyAsync("07"); Assert.Equal("001", (await scanner.DisplayAsync()).Channel);
        await scanner.KeyAsync("08"); Assert.Equal("500", (await scanner.DisplayAsync()).Channel);
    }
    [Fact] public async Task SimulatorRejectsInvalidSelection()
    {
        using var scanner = new SimulatedScanner(); await scanner.KeyAsync("02 0");
        await Assert.ThrowsAsync<InvalidOperationException>(() => scanner.KeyAsync("04"));
        Assert.Equal("125", (await scanner.DisplayAsync()).Channel);
    }
    [Fact] public async Task ScanSkipsEmptyAndLockedMemoriesAcrossAllBanks()
    {
        using var scanner = new SimulatedScanner();
        foreach (int n in Enumerable.Range(121, 10)) { var c = await scanner.ReadAsync(n); c.Lockout = true; await scanner.WriteAsync(c); }
        await scanner.WriteAsync(new Channel { Number = 499, FrequencyMHz = 145.5m, Name = "Only active" }); await scanner.KeyAsync("00");
        Assert.Equal("499", (await scanner.DisplayAsync()).Channel);
        Assert.Equal("499", (await scanner.DisplayAsync()).Channel);
    }
    [Fact] public async Task ScanWithoutEligibleChannelsReportsThatState()
    {
        using var scanner = new SimulatedScanner();
        foreach (int n in Enumerable.Range(121, 10)) { var c = await scanner.ReadAsync(n); c.Lockout = true; await scanner.WriteAsync(c); }
        await scanner.KeyAsync("00"); Assert.Equal("KEINE SCAN-KANAELE", (await scanner.DisplayAsync()).Line1);
    }
    [Fact] public async Task ManualModeHoldsChannel()
    {
        using var scanner = new SimulatedScanner(); await scanner.KeyAsync("00"); await scanner.DisplayAsync(); await scanner.KeyAsync("01");
        Assert.Equal((await scanner.DisplayAsync()).Channel, (await scanner.DisplayAsync()).Channel);
    }
}
