// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Multiplexing;

/// <summary>How a terminal is placed inside its home window.</summary>
public enum MultiplexerPaneKind
{
    /// <summary>Exactly one leaf in the window's split tree.</summary>
    Tiled,
    /// <summary>A separate rectangle over the tiled content.</summary>
    Floating,
    /// <summary>No visible rectangle. The process and attachments are unchanged.</summary>
    Unplaced,
}

/// <summary>Coordinates in ten-thousandths of the window, independent of client DPI.</summary>
public sealed record MultiplexerFloatBounds(int X, int Y, int Width, int Height)
{
    /// <summary>Centered rectangle for a newly floated pane.</summary>
    public static MultiplexerFloatBounds Default { get; } = new(1500, 1500, 7000, 7000);
    internal void Validate()
    {
        if (Width is < 1 or > 10000 || Height is < 1 or > 10000 || X < 0 || Y < 0 || X > 10000 - Width || Y > 10000 - Height)
            throw new ArgumentOutOfRangeException(nameof(MultiplexerFloatBounds), "The rectangle must fit within [0,10000] on both axes.");
    }
}

public sealed partial class MultiplexerBroker
{
    private readonly record struct Placement(Guid Window, MultiplexerPaneKind Kind, MultiplexerFloatBounds? Bounds = null);
    private static Placement GetPlacement(Session session, Guid blockId) => session.Placements.TryGetValue(blockId, out Placement p)
        ? p : throw new MultiplexerException(MultiplexerErrorCode.NotFound, "Block not found.");

    /// <summary>Moves a live or exited terminal between tiled, floating and unplaced positions. With a target leaf,
    /// splits that leaf; otherwise appends at the root. Neither operation touches the transport or stream cursor.</summary>
    public MultiplexerSessionDescriptor PlaceBlock(Guid sessionId, Guid blockId, Guid windowId, MultiplexerPaneKind kind,
        Guid? relativeTo = null, MultiplexerSplitOrientation orientation = MultiplexerSplitOrientation.Horizontal,
        double ratio = 0.5, bool before = false, MultiplexerFloatBounds? bounds = null, long? expectedRevision = null)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        ValidateSplit(orientation, ratio);
        if (kind != MultiplexerPaneKind.Tiled && (relativeTo.HasValue || before)) throw new ArgumentException("Only a tile can have a split target or ordering.");
        if (kind != MultiplexerPaneKind.Floating && bounds is not null) throw new ArgumentException("Only a floating block accepts bounds.");
        bounds?.Validate();
        lock (_gate)
        {
            Session session = FindSession(sessionId);
            CheckRevision(session, expectedRevision);
            Placement old = GetPlacement(session, blockId);
            Window source = FindWindow(session, old.Window), dest = FindWindow(session, windowId);
            if (relativeTo is { } anchor)
            {
                if (anchor == blockId) throw new ArgumentException("Cannot split a block against itself.");
                Placement target = GetPlacement(session, anchor);
                if (target.Window != dest.Id || target.Kind != MultiplexerPaneKind.Tiled) throw Conflict("Split target is not tiled in the destination window.");
            }
            Placement next = new(windowId, kind, kind == MultiplexerPaneKind.Floating ? bounds ?? old.Bounds ?? MultiplexerFloatBounds.Default : null);
            if (old == next && kind != MultiplexerPaneKind.Tiled) return Describe(session);
            MultiplexerLayoutNode? sourceTree = old.Kind == MultiplexerPaneKind.Tiled ? RemoveLeaf(source.Layout, blockId) : source.Layout;
            MultiplexerLayoutNode? destTree = ReferenceEquals(source, dest) ? sourceTree : dest.Layout;
            if (kind == MultiplexerPaneKind.Tiled)
            {
                MultiplexerLayoutNode leaf = MultiplexerLayoutNode.Leaf(blockId);
                MultiplexerLayoutNode Join(MultiplexerLayoutNode other) => before
                    ? MultiplexerLayoutNode.Split(orientation, leaf, other, ratio) : MultiplexerLayoutNode.Split(orientation, other, leaf, ratio);
                destTree = relativeTo is { } id ? Rewrite(destTree, n => n.BlockId == id ? Join(n) : n) : destTree is null ? leaf : Join(destTree);
            }
            // No visible partial edit: target validation and all tree allocations precede the commit.
            source.Layout = sourceTree;
            dest.Layout = destTree;
            session.Placements[blockId] = next;
            session.Changed();
            return Describe(session);
        }
    }

    /// <summary>Exchanges complete placements, including floating bounds, without swapping terminal identities or processes.</summary>
    public MultiplexerSessionDescriptor SwapBlocks(Guid sessionId, Guid firstId, Guid secondId, long? expectedRevision = null)
    {
        lock (_gate)
        {
            Session session = FindSession(sessionId);
            CheckRevision(session, expectedRevision);
            Placement a = GetPlacement(session, firstId), b = GetPlacement(session, secondId);
            if (firstId == secondId) return Describe(session);
            Window first = FindWindow(session, a.Window), second = FindWindow(session, b.Window);
            MultiplexerLayoutNode Exchange(MultiplexerLayoutNode leaf) => leaf.BlockId == firstId ? leaf with { BlockId = secondId }
                : leaf.BlockId == secondId ? leaf with { BlockId = firstId } : leaf;
            MultiplexerLayoutNode? firstTree = Rewrite(first.Layout, Exchange);
            MultiplexerLayoutNode? secondTree = ReferenceEquals(first, second) ? firstTree : Rewrite(second.Layout, Exchange);
            first.Layout = firstTree; second.Layout = secondTree;
            session.Placements[firstId] = b; session.Placements[secondId] = a;
            session.Changed(); return Describe(session);
        }
    }

    private static MultiplexerLayoutNode? Rewrite(MultiplexerLayoutNode? node, Func<MultiplexerLayoutNode, MultiplexerLayoutNode> leaf)
    {
        if (node is null) return null;
        if (node.BlockId.HasValue) return leaf(node);
        MultiplexerLayoutNode? first = Rewrite(node.First, leaf), second = Rewrite(node.Second, leaf);
        return ReferenceEquals(first, node.First) && ReferenceEquals(second, node.Second) ? node : node with { First = first, Second = second };
    }

    /// <summary>Moves an existing window to a one-based position, retaining its identity.</summary>
    public MultiplexerSessionDescriptor ReorderWindow(Guid sessionId, Guid windowId, int position, long? expectedRevision = null)
    {
        lock (_gate)
        {
            Session session = FindSession(sessionId); CheckRevision(session, expectedRevision);
            Window window = FindWindow(session, windowId);
            if (position < 1 || position > session.Windows.Count) throw new ArgumentOutOfRangeException(nameof(position));
            if (!ReferenceEquals(session.Windows[position - 1], window))
            {
                session.Windows.Remove(window); session.Windows.Insert(position - 1, window); session.Changed();
            }
            return Describe(session);
        }
    }

    /// <summary>Closes a window, not its processes. A nonempty window requires an explicit survivor;
    /// its terminals become unplaced in that survivor. The last window cannot be removed.</summary>
    public MultiplexerSessionDescriptor CloseWindow(Guid sessionId, Guid windowId, Guid? preserveIn = null, long? expectedRevision = null)
    {
        lock (_gate)
        {
            Session session = FindSession(sessionId); CheckRevision(session, expectedRevision);
            Window window = FindWindow(session, windowId);
            if (session.Windows.Count == 1) throw Conflict("Cannot remove the last window; close the session explicitly instead.");
            if (preserveIn == windowId) throw new ArgumentException("The destination must be a different window.", nameof(preserveIn));
            Window? survivor = preserveIn is { } id ? FindWindow(session, id) : null;
            Guid[] owned = session.Placements.Where(x => x.Value.Window == windowId).Select(x => x.Key).ToArray();
            if (owned.Length != 0 && survivor is null) throw Conflict("Specify a destination for this window's running or exited blocks.");
            foreach (Guid block in owned) session.Placements[block] = new(survivor!.Id, MultiplexerPaneKind.Unplaced);
            session.Windows.Remove(window); session.Changed(); return Describe(session);
        }
    }

    /// <summary>Waits until a structural revision advances and returns the newest descriptor. Intermediate changes may coalesce.
    /// Process bytes and exit are not structural changes. A removed session is reported as NotFound.</summary>
    public async ValueTask<MultiplexerSessionDescriptor> WaitForSessionChangeAsync(Guid sessionId, long afterRevision, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterRevision);
        cancellationToken.ThrowIfCancellationRequested();
        Session session;
        Task signal;
        lock (_gate)
        {
            session = FindSession(sessionId);
            if (afterRevision > session.Revision) throw Conflict("Revision is ahead of this session.");
            if (afterRevision < session.Revision) return Describe(session);
            if (session.WatcherCount >= 64) throw Capacity("Too many structural change waiters for this session.");
            signal = (session.PendingChange ??= new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            ++session.WatcherCount;
        }
        try
        {
            await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
            return GetSession(sessionId);
        }
        finally { lock (_gate) --session.WatcherCount; }
    }
}
