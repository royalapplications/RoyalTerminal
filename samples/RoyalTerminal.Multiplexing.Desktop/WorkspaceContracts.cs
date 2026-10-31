using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Desktop;

/// <summary>One selectable durable session returned by the host.</summary>
public sealed record WorkspaceSession(Guid Id, string Label, IReadOnlyList<WorkspaceBlock> Blocks)
{
    /// <summary>Gets a compact label that disambiguates equal session names.</summary>
    public string Display => $"{Label} · {Id.ToString("N")[..8]}";
}

/// <summary>One selectable process block returned by the host.</summary>
public sealed record WorkspaceBlock(Guid Id, string Label, string State, TerminalSessionDimensions Dimensions)
{
    /// <summary>Gets the label and live process state.</summary>
    public string Display => $"{Label} · {State}";
}

internal interface IWorkspaceClient
{
    ValueTask<IReadOnlyList<WorkspaceSession>> ListAsync(Uri endpoint, string token, CancellationToken cancellationToken);
    ValueTask<WorkspaceSession> CreateSessionAsync(Uri endpoint, string token, string label, CancellationToken cancellationToken);
    ValueTask<WorkspaceBlock> CreateShellAsync(Uri endpoint, string token, Guid sessionId, string label, CancellationToken cancellationToken);
}

internal interface IWorkspaceTerminal
{
    event Action<string>? StatusChanged;
    bool IsAttached { get; }
    bool HasControl { get; }
    ValueTask AttachAsync(MultiplexingTransportOptions options, CancellationToken cancellationToken);
    ValueTask DetachAsync();
    void RequestDimensions(TerminalSessionDimensions dimensions);
}
