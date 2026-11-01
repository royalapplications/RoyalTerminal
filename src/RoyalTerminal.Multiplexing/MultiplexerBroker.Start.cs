// Copyright (c) Royal Apps. Licensed under the MIT license.
using RoyalTerminal.Terminal;
namespace RoyalTerminal.Multiplexing;

public sealed partial class MultiplexerBroker
{
    /// <summary>Starts a block directly in its intended placement. No temporary tile is ever observable.</summary>
    public ValueTask<MultiplexerBlockDescriptor> CreatePlacedBlockAsync(Guid sessionId, Guid? windowId,
        MultiplexerPaneKind placement, MultiplexerFloatBounds? bounds, string label, ITerminalTransportOptions options,
        long? expectedRevision = null, CancellationToken cancellationToken = default) =>
        StartBlockAsync(sessionId, windowId, null, MultiplexerSplitOrientation.Horizontal, .5, label, options,
            expectedRevision, cancellationToken, placement, bounds);

    private async ValueTask<MultiplexerBlockDescriptor> StartBlockAsync(Guid sessionId, Guid? windowId,
        Guid? targetBlockId, MultiplexerSplitOrientation orientation, double ratio, string label,
        ITerminalTransportOptions options, long? expectedRevision, CancellationToken cancellationToken,
        MultiplexerPaneKind placement = MultiplexerPaneKind.Tiled, MultiplexerFloatBounds? bounds = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(placement)) throw new ArgumentOutOfRangeException(nameof(placement));
        if (bounds is not null && placement != MultiplexerPaneKind.Floating) throw new ArgumentException("Bounds require a floating layer.", nameof(bounds));
        if (placement == MultiplexerPaneKind.Floating) (bounds ??= MultiplexerFloatBounds.Default).Validate();
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
                if (p.Kind != MultiplexerPaneKind.Tiled || placement != MultiplexerPaneKind.Tiled) throw Conflict("Only tiled blocks can be split.");
                windowId = p.Window;
            }
            Window window = windowId is { } id ? FindWindow(session, id) : session.Windows[0];
            if (session.Blocks.Count >= _options.MaxBlocksPerSession) throw Capacity("The block limit has been reached.");
            block = new(sessionId, window.Id, label, _factory.Create(options), options.Dimensions, _options);
            session.Blocks.Add(block.Id, block);
            session.Placements.Add(block.Id, new(window.Id, placement, bounds));
            session.BlockOrder.Add(block.Id);
            if (placement == MultiplexerPaneKind.Tiled)
            {
                var leaf = MultiplexerLayoutNode.Leaf(block.Id);
                window.Layout = targetBlockId is { } anchor ? SplitLeaf(window.Layout!, anchor, leaf, orientation, ratio)
                    : window.Layout is null ? leaf : MultiplexerLayoutNode.Split(orientation, window.Layout, leaf, ratio);
            }
            else if (placement == MultiplexerPaneKind.Floating) window.Floats.Add(block.Id);
            session.Changed("block_created", window.Id, block.Id);
        }
        try
        {
            await block.StartAsync(options, cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (_sessions.TryGetValue(sessionId, out var session) && session.Placements.ContainsKey(block.Id)) return DescribeBlock(session, block);
                throw new MultiplexerException(MultiplexerErrorCode.Disconnected, "Block closed while starting.");
            }
        }
        catch
        {
            lock (_gate)
            {
                if (_sessions.TryGetValue(sessionId, out var session) && session.Blocks.ContainsKey(block.Id))
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
}
