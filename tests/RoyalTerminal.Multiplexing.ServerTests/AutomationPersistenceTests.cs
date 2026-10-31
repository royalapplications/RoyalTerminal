using System.Text;
using RoyalTerminal.Multiplexing.Automation;
using RoyalTerminal.Multiplexing.Inspection;
using RoyalTerminal.Multiplexing.Server;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Pipe;
using Xunit;

namespace RoyalTerminal.Multiplexing.ServerTests;

public sealed class AutomationPersistenceTests
{
    [Fact]
    public async Task ValidatedConfigurationReplacesFileAndReloadsWhileFailedUpdatePreservesIt()
    {
        string directory = Path.Combine(Path.GetTempPath(), "royalterminal-config-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "automation.json");
        InspectingTerminalTransportFactory inspections = new(new CompositeTerminalTransportFactory([new PipeTerminalTransportProvider()]));
        await using MultiplexerBroker broker = new(inspections);
        ApiCatalog catalog = new(new(broker, inspections), new("configuration-test-token-01234567890123456789"), new());
        AutomationService service = new(catalog, path);
        byte[] valid = Encoding.UTF8.GetBytes("""
            {"version":1,"actions":[{"id":"discover","steps":[{"method":"server.version","arguments":{}}]}]}
            """);
        try
        {
            await service.UpdateAsync(valid, CancellationToken.None);
            Assert.Equal(valid, await File.ReadAllBytesAsync(path));
            Assert.Equal("discover", Assert.Single(new AutomationService(catalog, path).Current.Configuration.Actions).Id);
            Assert.Single(Directory.GetFiles(directory));
            if (!OperatingSystem.IsWindows())
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            await Assert.ThrowsAsync<MultiplexerAutomationException>(() => service.UpdateAsync("{\"version\":2}"u8.ToArray(), CancellationToken.None).AsTask());
            Assert.Equal(valid, await File.ReadAllBytesAsync(path));
            Assert.Equal("discover", Assert.Single(service.Current.Configuration.Actions).Id);
            using CancellationTokenSource cancelled = new();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.UpdateAsync("{\"version\":1}"u8.ToArray(), cancelled.Token).AsTask());
            Assert.Equal(valid, await File.ReadAllBytesAsync(path));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
