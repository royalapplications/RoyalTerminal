// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;

namespace RoyalTerminal.Multiplexing;

/// <summary>Owns terminal sessions independently of client lifetimes.</summary>
/// <remarks>Run this broker in a separate host to survive GUI exit. Process state does not survive
/// termination of the broker host. Authorization of callers belongs to the hosting boundary.</remarks>
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

    /// <summary>Creates a broker from existing RoyalTerminal transport providers and explicit quotas.</summary>
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
            Session session = new(label);
            session.Windows.Add(new("Main"));
            _sessions.Add(session.Id, session);
            _sessionOrder.Add(session.Id);
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
            session.Changed();
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
            session.Changed();
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
            session.Changed();
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
            session.Changed();
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

    /// <summary>Starts a terminal by splitting a tiled target under the same revision check.</summary>
    public ValueTask<MultiplexerBlockDescriptor> SplitBlockAsync(Guid sessionId, Guid targetBlockId,
        MultiplexerSplitOrientation orientation, double ratio, string label, ITerminalTransportOptions options,
        long? expectedRevision = null, CancellationToken cancellationToken = default)
        => StartBlockAsync(sessionId, null, targetBlockId, orientation, ratio, label, options, expectedRevision, cancellationToken);

    /// <summary>Replays events after the applied cursor and subscribes atomically to future events.</summary>
    public MultiplexerAttachment Attach(Guid sessionId, Guid blockId, MultiplexerRole role = MultiplexerRole.Observer, long lastSequence = 0)
    {
        lock (_gate) return FindBlock(FindSession(sessionId), blockId).Attach(role, lastSequence);
    }

    /// <summary>Removes a block from its placement and explicitly stops its transport.</summary>
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
            session.Changed();
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
            foreach (Session session in _sessions.Values) { session.Changed(); all.AddRange(session.Blocks.Values); }
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

    private async ValueTask<MultiplexerBlockDescriptor> StartBlockAsync(Guid sessionId, Guid? windowId,
        Guid? targetBlockId, MultiplexerSplitOrientation orientation, double ratio, string label,
        ITerminalTransportOptions options, long? expectedRevision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateLabel(label);
        ValidateSplit(orientation, ratio);
        MultiplexerBlock.ValidateDimensions(options.Dimensions, _options);
        cancellationToken.ThrowIfCancellationRequested();
        MultiplexerBlock block;
        lock (_gate)
        {
            Session session = FindSession(sessionId);
            CheckRevision(session, expectedRevision);
            if (targetBlockId is { } target)
            {
                Placement p = GetPlacement(session, target);
                if (p.Kind != MultiplexerPaneKind.Tiled) throw Conflict("Only tiled blocks can be split.");
                windowId = p.Window;
            }
            Window window = windowId is { } id ? FindWindow(session, id) : session.Windows[0];
            if (session.Blocks.Count >= _options.MaxBlocksPerSession) throw Capacity("The block limit has been reached.");
            block = new(sessionId, window.Id, label, _factory.Create(options), options.Dimensions, _options);
            session.Blocks.Add(block.Id, block);
            session.Placements.Add(block.Id, new(window.Id, MultiplexerPaneKind.Tiled));
            session.BlockOrder.Add(block.Id);
            MultiplexerLayoutNode leaf = MultiplexerLayoutNode.Leaf(block.Id);
            window.Layout = targetBlockId is { } targetId ? SplitLeaf(window.Layout!, targetId, leaf, orientation, ratio)
                : window.Layout is null ? leaf : MultiplexerLayoutNode.Split(orientation, window.Layout, leaf, ratio);
            session.Changed();
        }
        try
        {
            await block.StartAsync(options, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (_sessions.TryGetValue(sessionId, out Session? session) && session.Placements.ContainsKey(block.Id))
                    return DescribeBlock(session, block);
                throw new MultiplexerException(MultiplexerErrorCode.Disconnected, "Block closed while starting.");
            }
        }
        catch
        {
            lock (_gate)
            {
                if (_sessions.TryGetValue(sessionId, out Session? session) && session.Blocks.ContainsKey(block.Id))
                {
                    RemoveBlock(session, block);
                    _retiring.Add(block);
                }
            }
            try { await block.CloseAsync().ConfigureAwait(false); }
            finally { lock (_gate) _retiring.Remove(block); }
            throw;
        }
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

    private Session FindSession(Guid id)
    {
        ThrowIfDisposed();
        return _sessions.TryGetValue(id, out Session? session) ? session : throw new MultiplexerException(MultiplexerErrorCode.NotFound, "Session not found.");
    }

    private static Window FindWindow(Session session, Guid id)
        => session.Windows.Find(w => w.Id == id) ?? throw new MultiplexerException(MultiplexerErrorCode.NotFound, "Window not found.");
    private static MultiplexerBlock FindBlock(Session session, Guid id)
        => session.Blocks.TryGetValue(id, out MultiplexerBlock? block) ? block : throw new MultiplexerException(MultiplexerErrorCode.NotFound, "Block not found.");
    private static void CheckRevision(Session session, long? expected)
    {
        if (expected is { } revision && revision != session.Revision) throw Conflict($"Expected revision {revision}, but the current session revision is {session.Revision}.");
    }

    private void ValidateLabel(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (label.Length > _options.MaxLabelLength) throw new ArgumentException("The label exceeds the configured length limit.", nameof(label));
        foreach (char character in label) if (char.IsControl(character)) throw new ArgumentException("Labels cannot contain control characters.", nameof(label));
    }

    private void ValidateLayout(Session session, Window window, MultiplexerLayoutNode? layout)
    {
        HashSet<Guid> expected = [];
        foreach ((Guid id, Placement p) in session.Placements) if (p.Window == window.Id && p.Kind == MultiplexerPaneKind.Tiled) expected.Add(id);
        HashSet<Guid> actual = [];
        Stack<MultiplexerLayoutNode> pending = new();
        if (layout is not null) pending.Push(layout);
        int visited = 0;
        while (pending.TryPop(out MultiplexerLayoutNode? node))
        {
            if (++visited > 2L * _options.MaxBlocksPerSession) throw new ArgumentException("The layout exceeds the configured block limit.", nameof(layout));
            if (node.BlockId is { } id)
            {
                if (node.Orientation is not null || node.First is not null || node.Second is not null || !actual.Add(id)) throw new ArgumentException("Each layout leaf must reference one distinct block.", nameof(layout));
            }
            else
            {
                if (node.Orientation is not { } orientation || node.First is null || node.Second is null) throw new ArgumentException("A split requires an orientation and two children.", nameof(layout));
                ValidateSplit(orientation, node.Ratio);
                pending.Push(node.Second); pending.Push(node.First);
            }
        }
        if (!expected.SetEquals(actual)) throw new ArgumentException("The layout must contain every tiled block in its window exactly once.", nameof(layout));
    }

    private static void ValidateSplit(MultiplexerSplitOrientation orientation, double ratio)
    {
        if (!Enum.IsDefined(orientation)) throw new ArgumentOutOfRangeException(nameof(orientation));
        if (!double.IsFinite(ratio) || ratio <= 0 || ratio >= 1) throw new ArgumentOutOfRangeException(nameof(ratio));
    }

    private static MultiplexerLayoutNode SplitLeaf(MultiplexerLayoutNode node, Guid target, MultiplexerLayoutNode newLeaf, MultiplexerSplitOrientation orientation, double ratio)
    {
        if (node.BlockId == target) return MultiplexerLayoutNode.Split(orientation, node, newLeaf, ratio);
        if (node.BlockId is not null) return node;
        return node with { First = SplitLeaf(node.First!, target, newLeaf, orientation, ratio), Second = SplitLeaf(node.Second!, target, newLeaf, orientation, ratio) };
    }

    private static MultiplexerLayoutNode? RemoveLeaf(MultiplexerLayoutNode? node, Guid blockId)
    {
        if (node is null || node.BlockId == blockId) return null;
        if (node.BlockId is not null) return node;
        MultiplexerLayoutNode? first = RemoveLeaf(node.First, blockId), second = RemoveLeaf(node.Second, blockId);
        return first is null ? second : second is null ? first : node with { First = first, Second = second };
    }

    private static void RemoveBlock(Session session, MultiplexerBlock block)
    {
        Placement p = session.Placements[block.Id];
        if (p.Kind == MultiplexerPaneKind.Tiled)
        {
            Window window = FindWindow(session, p.Window);
            window.Layout = RemoveLeaf(window.Layout, block.Id);
        }
        session.Placements.Remove(block.Id); session.Blocks.Remove(block.Id); session.BlockOrder.Remove(block.Id);
        session.Changed();
    }

    private static MultiplexerBlockDescriptor DescribeBlock(Session session, MultiplexerBlock block)
    {
        Placement p = session.Placements[block.Id];
        return block.Describe() with { WindowId = p.Window, Placement = p.Kind, FloatingBounds = p.Bounds };
    }
    private static MultiplexerSessionDescriptor Describe(Session session)
    {
        MultiplexerWindowDescriptor[] windows = new MultiplexerWindowDescriptor[session.Windows.Count];
        for (int i = 0; i < windows.Length; i++) windows[i] = Describe(session.Windows[i]);
        MultiplexerBlockDescriptor[] blocks = new MultiplexerBlockDescriptor[session.Blocks.Count];
        int index = 0;
        foreach (Guid id in session.BlockOrder) blocks[index++] = DescribeBlock(session, session.Blocks[id]);
        return new(session.Id, session.Label, session.Revision, session.CreatedUtc, windows, blocks);
    }
    private static MultiplexerWindowDescriptor Describe(Window window) => new(window.Id, window.Label, window.Layout);
    private static MultiplexerException Capacity(string message) => new(MultiplexerErrorCode.CapacityExceeded, message);
    private static MultiplexerException Conflict(string message) => new(MultiplexerErrorCode.Conflict, message);
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class Session(string label)
    {
        internal Guid Id { get; } = Guid.NewGuid();
        internal string Label { get; set; } = label;
        internal long Revision { get; private set; } = 1;
        internal DateTimeOffset CreatedUtc { get; } = DateTimeOffset.UtcNow;
        internal List<Window> Windows { get; } = [];
        internal Dictionary<Guid, MultiplexerBlock> Blocks { get; } = new();
        internal Dictionary<Guid, Placement> Placements { get; } = [];
        internal List<Guid> BlockOrder { get; } = [];
        internal TaskCompletionSource? PendingChange { get; set; }
        internal int WatcherCount { get; set; }
        internal void Changed() { ++Revision; PendingChange?.TrySetResult(); PendingChange = null; }
    }
    private sealed class Window(string label)
    {
        internal Guid Id { get; } = Guid.NewGuid();
        internal string Label { get; set; } = label;
        internal MultiplexerLayoutNode? Layout { get; set; }
    }
}
