// Copyright (c) Royal Apps. Licensed under the MIT license.
namespace RoyalTerminal.Multiplexing;

/// <summary>Validates a snapshot's bottom-to-top floating order; legacy snapshots use creation order.</summary>
public static class MultiplexerLayers
{
    /// <summary>Returns every floating block exactly once, with the members of each frame contiguous.</summary>
    public static IReadOnlyList<Guid> Ordered(MultiplexerSessionDescriptor session,MultiplexerWindowDescriptor window)
    {
        var members=session.Blocks.Where(b=>b.WindowId==window.Id&&b.Placement==MultiplexerPaneKind.Floating).Select(b=>b.Id).ToArray();
        if(window.FloatingOrder is not {} order)return members;
        if(order.Count!=members.Length||!members.ToHashSet().SetEquals(order))throw new InvalidDataException("Invalid floating order.");return order;
    }
}
public sealed partial class MultiplexerBroker
{
    /// <summary>Accepts either a permutation of frame IDs or the compatible flat order with complete, contiguous frames.</summary>
    public MultiplexerSessionDescriptor SetLayerOrder(Guid sessionId,Guid windowId,IReadOnlyList<Guid> bottomToTop,long? expectedRevision=null)
    {
        ArgumentNullException.ThrowIfNull(bottomToTop);if(bottomToTop.Count>_options.MaxBlocksPerSession)throw new ArgumentException("Too many layers.");
        var supplied=bottomToTop.ToArray();
        lock(_gate)
        {
            var s=FindSession(sessionId);CheckRevision(s,expectedRevision);var w=FindWindow(s,windowId);
            Guid Frame(Guid id)=>s.Placements[id].Layer??id;
            var groups=w.Floats.ToLookup(Frame);var keys=groups.Select(g=>g.Key).ToHashSet();
            var flat=supplied.Length==keys.Count&&keys.SetEquals(supplied)?supplied.SelectMany(k=>groups[k]).ToArray():supplied;
            if(flat.Length!=w.Floats.Count||!w.Floats.ToHashSet().SetEquals(flat)||!flat.SequenceEqual(flat.Select(Frame).Distinct().SelectMany(k=>groups[k])))throw new ArgumentException("Expected a permutation of complete floating frames.");
            if(!w.Floats.SequenceEqual(flat)){w.Floats.Clear();w.Floats.AddRange(flat);s.Changed();}return Describe(s);
        }
    }
}
