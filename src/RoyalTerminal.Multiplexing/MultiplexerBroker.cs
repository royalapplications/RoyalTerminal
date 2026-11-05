// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;

namespace RoyalTerminal.Multiplexing;

/// <summary>Owns terminal sessions independently of client lifetimes.</summary>
/// <remarks>Run the broker in a separate host to survive GUI exit. The host authorizes access.</remarks>
public sealed partial class MultiplexerBroker : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly ITerminalTransportFactory _factory;
    private readonly MultiplexerOptions _options;
    private readonly Dictionary<Guid, Session> _sessions = new();
    private readonly List<Guid> _sessionOrder = [];
    private readonly HashSet<MultiplexerBlock> _retiring = [];
    private Task? _disposeTask;
    private bool _disposed;

    /// <summary>Structural mutations are journaled while holding the same lock as the mutation.</summary>
    public WorkspaceActivityHub Activity { get; } = new();

    /// <summary>Creates a broker from transport providers and explicit quotas.</summary>
    public MultiplexerBroker(ITerminalTransportFactory factory, MultiplexerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        _options = options ?? new();
        _options.Validate();
    }

    /// <summary>Creates an empty session with one Main window.</summary>
    public MultiplexerSessionDescriptor CreateSession(string label)
    {
        ValidateLabel(label);
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_sessions.Count >= _options.MaxSessions) throw Capacity("The session limit has been reached.");
            Session session = new(label, Activity);
            session.Windows.Add(new("Main"));
            _sessions.Add(session.Id, session);
            _sessionOrder.Add(session.Id);
            Activity.Open(session.Id, session.Windows[0].Id, label);
            return Describe(session);
        }
    }

    /// <summary>Returns owned snapshots of all sessions in creation order.</summary>
    public IReadOnlyList<MultiplexerSessionDescriptor> ListSessions()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            MultiplexerSessionDescriptor[] result = new MultiplexerSessionDescriptor[_sessions.Count];
            int index = 0;
            foreach (Guid id in _sessionOrder) result[index++] = Describe(_sessions[id]);
            return result;
        }
    }

    /// <summary>Returns an owned snapshot of one session.</summary>
    public MultiplexerSessionDescriptor GetSession(Guid sessionId)
    {
        lock (_gate) return Describe(FindSession(sessionId));
    }

    /// <summary>Changes a session label, optionally rejecting a stale structural revision.</summary>
    public MultiplexerSessionDescriptor RenameSession(Guid sessionId, string label, long? expectedRevision = null)
    {
        ValidateLabel(label);
        lock (_gate)
        {
            Session session = FindSession(sessionId);
            CheckRevision(session, expectedRevision);
            session.Label = label;
            session.Changed("session_label_changed", label: label);
            return Describe(session);
        }
    }

    /// <summary>Creates an empty window without starting a process.</summary>
    public MultiplexerWindowDescriptor CreateWindow(Guid sessionId, string label, long? expectedRevision = null)
    {
        ValidateLabel(label);
        lock (_gate)
        {
            Session session = FindSession(sessionId);
            CheckRevision(session, expectedRevision);
            if (session.Windows.Count >= _options.MaxWindowsPerSession) throw Capacity("The window limit has been reached.");
            Window window = new(label);
            session.Windows.Add(window);
            session.Changed("window_created", window.Id, label: label);
            return Describe(window);
        }
    }

    /// <summary>Changes a window label with optional optimistic concurrency.</summary>
    public MultiplexerWindowDescriptor RenameWindow(Guid sessionId, Guid windowId, string label, long? expectedRevision = null)
    {
        ValidateLabel(label);
        lock (_gate)
        {
            Session session = FindSession(sessionId);
            CheckRevision(session, expectedRevision);
            Window window = FindWindow(session, windowId);
            window.Label = label;
            session.Changed("window_label_changed", window.Id, label: label);
            return Describe(window);
        }
    }

    /// <summary>Changes a block label with optional optimistic concurrency.</summary>
    public MultiplexerBlockDescriptor RenameBlock(Guid sessionId, Guid blockId, string label, long? expectedRevision = null)
    {
        ValidateLabel(label);
        lock (_gate)
        {
            Session session = FindSession(sessionId);
            CheckRevision(session, expectedRevision);
            MultiplexerBlock block = FindBlock(session, blockId);
            block.Rename(label);
            session.Changed("block_label_changed", block: blockId, label: label);
            return DescribeBlock(session, block);
        }
    }

    /// <summary>Replaces the tiled split tree; floating and unplaced blocks must not appear in it.</summary>
    public MultiplexerWindowDescriptor SetWindowLayout(Guid sessionId, Guid windowId, MultiplexerLayoutNode? layout, long? expectedRevision = null)
    {
        lock (_gate)
        {
            Session session = FindSession(sessionId);
            CheckRevision(session, expectedRevision);
            Window window = FindWindow(session, windowId);
            ValidateLayout(session, window, layout);
            window.Layout = layout;
            session.Changed();
            return Describe(window);
        }
    }

    /// <summary>Starts a terminal in the first window.</summary>
    public ValueTask<MultiplexerBlockDescriptor> AddBlockAsync(Guid sessionId, string label,
        ITerminalTransportOptions options, CancellationToken cancellationToken = default)
        => StartBlockAsync(sessionId, null, null, MultiplexerSplitOrientation.Horizontal, 0.5, label, options, null, cancellationToken);

    /// <summary>Starts a terminal in the selected window.</summary>
    public ValueTask<MultiplexerBlockDescriptor> AddBlockAsync(Guid sessionId, Guid windowId, string label,
        ITerminalTransportOptions options, CancellationToken cancellationToken = default)
        => StartBlockAsync(sessionId, windowId, null, MultiplexerSplitOrientation.Horizontal, 0.5, label, options, null, cancellationToken);

    /// <summary>Starts a terminal by splitting a visible target within its existing tile tree or floating frame.</summary>
    public ValueTask<MultiplexerBlockDescriptor> SplitBlockAsync(Guid sessionId, Guid targetBlockId,
        MultiplexerSplitOrientation orientation, double ratio, string label, ITerminalTransportOptions options,
        long? expectedRevision = null, CancellationToken cancellationToken = default)
        => StartBlockAsync(sessionId, null, targetBlockId, orientation, ratio, label, options, expectedRevision, cancellationToken);

    /// <summary>Replays events after the applied cursor and subscribes atomically to future events.</summary>
    public MultiplexerAttachment Attach(Guid sessionId, Guid blockId, MultiplexerRole role = MultiplexerRole.Observer, long lastSequence = 0)
    {
        lock (_gate) return FindBlock(FindSession(sessionId), blockId).Attach(role, lastSequence);
    }

    /// <summary>Removes one terminal from its placement and explicitly stops its transport. Its siblings remain running.</summary>
    public async ValueTask CloseBlockAsync(Guid sessionId, Guid blockId)
    {
        MultiplexerBlock block;
        lock (_gate)
        {
            Session session = FindSession(sessionId);
            block = FindBlock(session, blockId);
            RemoveBlock(session, block);
            _retiring.Add(block);
        }
        try { await block.CloseAsync().ConfigureAwait(false); }
        finally { lock (_gate) _retiring.Remove(block); }
    }

    /// <summary>Closes the whole floating frame by its stable handle or a current member. A stale revision rejects before any process is stopped.</summary>
    public ValueTask CloseFloatingLayerAsync(Guid sessionId, Guid blockId, long expectedRevision)
        => CloseFrameAsync(sessionId, blockId, expectedRevision);

    /// <summary>Removes an entire session and explicitly stops every owned transport.</summary>
    public async ValueTask CloseSessionAsync(Guid sessionId)
    {
        MultiplexerBlock[] blocks;
        lock (_gate)
        {
            Session session = FindSession(sessionId);
            blocks = [.. session.Blocks.Values];
            foreach (MultiplexerBlock block in blocks) _retiring.Add(block);
            _sessions.Remove(sessionId);
            _sessionOrder.Remove(sessionId);
            session.End();
        }
        try { await CloseBlocksAsync(blocks).ConfigureAwait(false); }
        finally { lock (_gate) foreach (MultiplexerBlock block in blocks) _retiring.Remove(block); }
    }

    /// <summary>Stops all owned transports; concurrent releases await the same completion.</summary>
    public ValueTask DisposeAsync()
    {
        MultiplexerBlock[] blocks;
        TaskCompletionSource completion;
        lock (_gate)
        {
            if (_disposeTask is not null) return new(_disposeTask);
            _disposed = true;
            List<MultiplexerBlock> all = [];
            foreach (Session session in _sessions.Values) { session.End(); all.AddRange(session.Blocks.Values); }
            Activity.Complete();
            all.AddRange(_retiring);
            blocks = [.. all];
            _sessions.Clear();
            _sessionOrder.Clear();
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
        }
        _ = CompleteDisposeAsync(blocks, completion);
        return new(completion.Task);
    }

    private static async Task CloseBlocksAsync(MultiplexerBlock[] blocks)
    {
        Task[] tasks = new Task[blocks.Length];
        for (int i = 0; i < blocks.Length; i++) tasks[i] = blocks[i].CloseAsync();
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task CompleteDisposeAsync(MultiplexerBlock[] blocks, TaskCompletionSource completion)
    {
        try { await CloseBlocksAsync(blocks).ConfigureAwait(false); completion.TrySetResult(); }
        catch (Exception error) { completion.TrySetException(error); }
    }
}
