using ScanControl.Core;
using ScanControl.Infrastructure;
using ScanControl.Protocol.BC780XLT;

namespace ScanControl.Tests;

public class FunctionalTests
{
    [Fact] public void ParseDocumentedChannel()
    {
        var c = Parser.Channel("C015 F04060125 TF DN LF AF RF N000", 15);
        Assert.Equal(406.0125m, c.FrequencyMHz); Assert.Equal("+2", c.Delay); Assert.False(c.Trunk);
    }
    [Theory]
    [InlineData("C015 F04060125 TF DN LF AF N000")]
    [InlineData("C016 F04060125 TF DN LF AF RF N000")]
    [InlineData("ERR")]
    [InlineData("C015 F0406012 TF DN LF AF RF N000")]
    public void RejectIncompleteOrWrongChannel(string frame) => Assert.Throws<InvalidDataException>(() => Parser.Channel(frame, 15));
    [Fact] public void LcdRetainsActualDisplayText() => Assert.Equal("25cm Amateur", Parser.Lcd("LINE1 [25cm Amateur ][ _ ]", "LINE1"));
    [Theory] [InlineData(24.9)] [InlineData(824.0)] [InlineData(849.0)] [InlineData(869.0)] [InlineData(894.0)] [InlineData(1000.0)] [InlineData(1300.1)] [InlineData(149.12501)]
    public void RejectInvalidFrequencies(double f) => Assert.Throws<InvalidDataException>(() => Validation.Channel(new Channel { Number = 1, FrequencyMHz = (decimal)f }));
    [Fact] public void RejectUnsupportedTone() => Assert.Throws<InvalidDataException>(() => Validation.Channel(new Channel { Number = 1, Tone = 143 }));
    [Fact] public void RejectDuplicateChannels() => Assert.Throws<InvalidDataException>(() => Validation.Project(new Project { Channels = [new() { Number = 1 }, new() { Number = 1 }] }));
    [Fact] public void CsvRoundTripPreservesAllProperties()
    {
        var c = new Channel { Number = 125, Name = "A; \"B\"", FrequencyMHz = 149.125m, Attenuator = true, Record = true, BeepAlert = true, Tone = 142, ToneLock = true, Delay = "-10", Modulation = "AM", Step = "12.5K", Lockout = true, Trunk = true };
        Assert.Equal(c, Assert.Single(ProjectFiles.ImportCsv(ProjectFiles.ExportCsv([c]))));
    }
    [Fact] public void CsvRejectsMalformedQuote() => Assert.Throws<InvalidDataException>(() => ProjectFiles.ImportCsv("Number;\"abc"));
    [Fact] public void CsvValidatesBeforeReturning()
    {
        var csv = ProjectFiles.ExportCsv([new Channel { Number = 501 }]);
        Assert.Throws<InvalidDataException>(() => ProjectFiles.ImportCsv(csv));
    }
    [Fact] public async Task JsonAndBackupRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ScanControlTests-" + Guid.NewGuid());
        try
        {
            var p = new Project { Simulation = true, Channels = [new() { Number = 125, FrequencyMHz = 149.125m, Name = "Demo" }] };
            var path = await ProjectFiles.BackupAsync(dir, p, "test"); var loaded = await ProjectFiles.LoadAsync(path);
            Assert.True(loaded.Simulation); Assert.Equal(p.Channels, loaded.Channels);
            p.Channels[0].Name = "Changed"; await ProjectFiles.SaveAsync(path, p); Assert.Equal("Changed", (await ProjectFiles.LoadAsync(path)).Channels[0].Name);
            Assert.Empty(Directory.GetFiles(dir, "*.tmp", SearchOption.AllDirectories));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    [Fact] public async Task BadProjectDoesNotOverwriteExistingFile()
    {
        var path = Path.GetTempFileName();
        try { await File.WriteAllTextAsync(path, "original"); await Assert.ThrowsAsync<InvalidDataException>(() => ProjectFiles.SaveAsync(path, new Project { Version = 2 })); Assert.Equal("original", await File.ReadAllTextAsync(path)); }
        finally { File.Delete(path); }
    }
    [Fact] public async Task SimulationUploadIsReadBack()
    {
        using var scanner = new SimulatedScanner(); var service = new TransferService(scanner);
        var c = await scanner.ReadAsync(125); c.Name = "Test";
        await service.WriteAndVerifyAsync([c], null, default); Assert.Equal(c, await scanner.ReadAsync(125)); Assert.True(scanner.Simulation);
    }
    [Fact] public async Task VerificationMismatchFails() => await Assert.ThrowsAsync<IOException>(() => new TransferService(new FaultScanner()).WriteAndVerifyAsync([new Channel { Number = 1, Name = "Wanted" }], null, default));
    [Fact] public async Task ReadFailureReturnsNoPartialSuccess() => await Assert.ThrowsAsync<IOException>(() => new TransferService(new FaultScanner { FailRead = true }).ReadAsync([1, 2], null, default));
    [Fact] public async Task CancellationStopsTransfer()
    {
        using var scanner = new SimulatedScanner(); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new TransferService(scanner).ReadAsync([1, 2], null, cancel.Token));
    }
    [Fact] public async Task ProtocolTimeoutPropagates() => await Assert.ThrowsAsync<TimeoutException>(() => new Bc780Scanner(new FailingTransport()).IdentifyAsync());
    [Fact] public async Task TrunkingWriteDoesNotSendCommands()
    {
        using var transport = new FailingTransport(); using var scanner = new Bc780Scanner(transport);
        await Assert.ThrowsAsync<NotSupportedException>(() => scanner.WriteAsync(new Channel { Number = 1, Trunk = true }));
    }
    [Fact] public async Task WriterRestoresPropertiesAfterFrequencyProgramming()
    {
        var sent = new List<string>();
        using var scanner = new Bc780Scanner(new ScriptTransport(command =>
        {
            sent.Add(command);
            return command.StartsWith("MA") || command.StartsWith("PM") ? "C001 F01491250 TF DN LF AF RF N000" : "OK";
        }));
        await scanner.WriteAsync(new Channel { Number = 1, FrequencyMHz = 149.125m, Attenuator = true, Record = true, Delay = "-5", Name = "Test" });
        int program = sent.FindIndex(c => c.StartsWith("PM"));
        Assert.True(sent.IndexOf("ATN") > program); Assert.True(sent.IndexOf("ARN") > program); Assert.True(sent.IndexOf("DLN -5") > program);
        Assert.Contains("PM001 01491250", sent); Assert.Contains("TA C 001 Test", sent);
    }
    [Fact] public async Task ReaderRejectsWrongExtendedResponse()
    {
        using var scanner = new Bc780Scanner(new ScriptTransport(command => command.StartsWith("PM") ? "C001 F01491250 TF DN LF AF RF N000" : "TA C 002 wrong channel"));
        await Assert.ThrowsAsync<InvalidDataException>(() => scanner.ReadAsync(1));
    }
    [Fact] public async Task WriterStopsOnUnacknowledgedSetting()
    {
        var sent = new List<string>();
        using var scanner = new Bc780Scanner(new ScriptTransport(command => { sent.Add(command); return command.StartsWith("MA") ? "C001 F01491250 TF DN LF AF RF N000" : "NG"; }));
        await Assert.ThrowsAsync<InvalidDataException>(() => scanner.WriteAsync(new Channel { Number = 1, FrequencyMHz = 149.125m }));
        Assert.DoesNotContain(sent, c => c.StartsWith("PM"));
    }
    [Fact] public async Task RejectOtherScannerModel()
    {
        using var scanner = new Bc780Scanner(new ScriptTransport(_ => "SI BC785D,0000,103"));
        await Assert.ThrowsAsync<InvalidDataException>(() => scanner.IdentifyAsync());
    }
    private sealed class ScriptTransport(Func<string, string> respond) : ICommandTransport
    {
        public Task<string> ExchangeAsync(string command, CancellationToken cancellationToken = default) => Task.FromResult(respond(command));
        public void Dispose() { }
    }
    private class FailingTransport : ICommandTransport
    {
        public Task<string> ExchangeAsync(string command, CancellationToken cancellationToken = default) => throw new TimeoutException("No response");
        public void Dispose() { }
    }
    private class FaultScanner : IScanner
    {
        public bool FailRead { get; set; }
        public bool Simulation => true;
        public Task<string> IdentifyAsync(CancellationToken ct = default) => Task.FromResult("test");
        public Task<Channel> ReadAsync(int n, CancellationToken ct = default) => FailRead && n == 2 ? throw new IOException("Disconnected") : Task.FromResult(new Channel { Number = n });
        public Task WriteAsync(Channel c, CancellationToken ct = default) => Task.CompletedTask;
        public Task<ScannerDisplay> DisplayAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task KeyAsync(string code, CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose() { }
    }
}
