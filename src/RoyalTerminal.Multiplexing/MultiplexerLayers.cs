// Copyright (c) Royal Apps. Licensed under the MIT license.
namespace RoyalTerminal.Multiplexing;

/// <summary>Validates the snapshot before consuming its stacking order; legacy snapshots use block creation order.</summary>
public static class MultiplexerLayers
{
    /// <summary>Returns the exact set of floating blocks for this window from back to front.</summary>
    public static IReadOnlyList<Guid> Ordered(MultiplexerSessionDescriptor session, MultiplexerWindowDescriptor window)
    {
        var members = session.Blocks.Where(b => b.WindowId == window.Id && b.Placement == MultiplexerPaneKind.Floating)
            .Select(b => b.Id).ToArray();
        if (window.FloatingOrder is not { } order) return members;
        if (order.Count != members.Length || !members.ToHashSet().SetEquals(order))
            throw new InvalidDataException("Floating order must contain each floating block of its window exactly once.");
        return order;
    }
}
public sealed partial class MultiplexerBroker
{
    /// <summary>Replace only the depth order under the structural lock. Foreign, non-floating, duplicate or omitted IDs reject the entire operation.</summary>
    public MultiplexerSessionDescriptor SetLayerOrder(Guid sessionId, Guid windowId, IReadOnlyList<Guid> bottomToTop, long? expectedRevision = null)
    {
        ArgumentNullException.ThrowIfNull(bottomToTop);
        if (bottomToTop.Count > _options.MaxBlocksPerSession) throw new ArgumentException("Too many layers.", nameof(bottomToTop));
        var copy = bottomToTop.ToArray();
        lock (_gate)
        {
            var session = FindSession(sessionId); CheckRevision(session, expectedRevision);
            var window = FindWindow(session, windowId);
            if (copy.Length != window.Floats.Count || !window.Floats.ToHashSet().SetEquals(copy))
                throw new ArgumentException("Expected a permutation of this window's floating blocks.", nameof(bottomToTop));
            if (!window.Floats.SequenceEqual(copy))
            {
                window.Floats.Clear(); window.Floats.AddRange(copy); session.Changed();
            }
            return Describe(session);
        }
    }
}
