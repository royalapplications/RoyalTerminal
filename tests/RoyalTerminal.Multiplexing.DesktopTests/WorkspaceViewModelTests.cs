using System.Reactive.Threading.Tasks;
using Avalonia.Headless.XUnit;
using RoyalTerminal.Multiplexing.Desktop;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Multiplexing;
using Xunit;

namespace RoyalTerminal.Multiplexing.DesktopTests;

public sealed class WorkspaceViewModelTests
{
    [AvaloniaFact]
    public async Task RefreshSelectsIdentityAndAttachUsesExplicitObserverPolicy()
    {
        WorkspaceBlock block = new(Guid.NewGuid(), "Build", "Running", new(100, 30, 0, 0));
        WorkspaceSession session = new(Guid.NewGuid(), "Research", [block]);
        Client client = new([session]);
        Terminal terminal = new();
        using WorkspaceViewModel viewModel = new(client, terminal) { Token = "secret", Observer = true };
        await viewModel.RefreshCommand.Execute().ToTask(TestContext.Current.CancellationToken);
        Assert.Equal(session.Id.ToString("D"), viewModel.SessionId);
        Assert.Equal(block.Id.ToString("D"), viewModel.BlockId);
        Assert.Equal(100, viewModel.Columns);
        await viewModel.AttachCommand.Execute().ToTask(TestContext.Current.CancellationToken);
        Assert.True(terminal.Options!.Observer);
        Assert.Equal(session.Id, terminal.Options.SessionId);
        Assert.Equal(block.Id, terminal.Options.BlockId);
        Assert.True(viewModel.IsAttached);
        Assert.False(viewModel.HasControl);
        await viewModel.DetachCommand.Execute().ToTask(TestContext.Current.CancellationToken);
        Assert.False(viewModel.IsAttached);
        Assert.Equal(1, terminal.DetachCount);
    }

    [AvaloniaFact]
    public async Task MissingTokenAndServerFailuresBecomeVisibleErrors()
    {
        Client client = new([]);
        Terminal terminal = new();
        using WorkspaceViewModel viewModel = new(client, terminal);
        await viewModel.RefreshCommand.Execute().ToTask(TestContext.Current.CancellationToken);
        Assert.Contains("token", viewModel.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, client.Calls);
        viewModel.Token = "secret";
        client.Failure = new IOException("Host unavailable");
        await viewModel.RefreshCommand.Execute().ToTask(TestContext.Current.CancellationToken);
        Assert.Equal("Host unavailable", viewModel.Error);
        Assert.False(viewModel.IsBusy);
    }

    [AvaloniaFact]
    public async Task ManualScopedIdentifiersAttachWithoutRequiringSessionEnumeration()
    {
        Terminal terminal = new();
        Guid session = Guid.NewGuid();
        Guid block = Guid.NewGuid();
        using WorkspaceViewModel viewModel = new(new Client([]), terminal)
        { Token = "scoped", SessionId = session.ToString(), BlockId = block.ToString(), Observer = false };
        await viewModel.AttachCommand.Execute().ToTask(TestContext.Current.CancellationToken);
        Assert.Equal(session, terminal.Options!.SessionId);
        Assert.Equal(block, terminal.Options.BlockId);
        Assert.True(viewModel.HasControl);
        viewModel.Columns = 132;
        viewModel.Rows = 43;
        await viewModel.ResizeCommand.Execute().ToTask(TestContext.Current.CancellationToken);
        Assert.Equal(new TerminalSessionDimensions(132, 43, 0, 0), terminal.RequestedDimensions);
    }

    private sealed class Client(IReadOnlyList<WorkspaceSession> sessions) : IWorkspaceClient
    {
        public int Calls { get; private set; }
        public Exception? Failure { get; set; }
        public ValueTask<IReadOnlyList<WorkspaceSession>> ListAsync(Uri endpoint, string token, CancellationToken cancellationToken)
        { Calls++; if (Failure is not null) { throw Failure; } return ValueTask.FromResult(sessions); }
        public ValueTask<WorkspaceSession> CreateSessionAsync(Uri endpoint, string token, string label, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public ValueTask<WorkspaceBlock> CreateShellAsync(Uri endpoint, string token, Guid sessionId, string label, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class Terminal : IWorkspaceTerminal
    {
        public event Action<string>? StatusChanged;
        public bool IsAttached { get; private set; }
        public bool HasControl => IsAttached && Options?.Observer == false;
        public MultiplexingTransportOptions? Options { get; private set; }
        public int DetachCount { get; private set; }
        public TerminalSessionDimensions? RequestedDimensions { get; private set; }
        public ValueTask AttachAsync(MultiplexingTransportOptions options, CancellationToken cancellationToken)
        { Options = options; IsAttached = true; StatusChanged?.Invoke("Attached"); return ValueTask.CompletedTask; }
        public ValueTask DetachAsync()
        { DetachCount++; IsAttached = false; StatusChanged?.Invoke("Detached"); return ValueTask.CompletedTask; }
        public void RequestDimensions(TerminalSessionDimensions dimensions) => RequestedDimensions = dimensions;
    }
}
