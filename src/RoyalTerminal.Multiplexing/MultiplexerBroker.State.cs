// Copyright (c) Royal Apps. Licensed under the MIT license.
namespace RoyalTerminal.Multiplexing;

public sealed partial class MultiplexerBroker
{
    private Session FindSession(Guid id)
    {
        ThrowIfDisposed();
        return _sessions.TryGetValue(id, out Session? session) ? session : throw new MultiplexerException(MultiplexerErrorCode.NotFound, "Session not found.");
    }
    private static Window FindWindow(Session s, Guid id) => s.Windows.Find(w => w.Id == id) ?? throw new MultiplexerException(MultiplexerErrorCode.NotFound,"Window not found.");
    private static MultiplexerBlock FindBlock(Session s, Guid id) => s.Blocks.TryGetValue(id, out var block) ? block : throw new MultiplexerException(MultiplexerErrorCode.NotFound,"Block not found.");
    private static void CheckRevision(Session session, long? expected)
    {
        if (expected is { } revision && revision != session.Revision) throw Conflict($"Expected revision {revision}, but the current session revision is {session.Revision}.");
    }
    private void ValidateLabel(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        if (label.Length > _options.MaxLabelLength) throw new ArgumentException("The label exceeds the configured length limit.", nameof(label));
        foreach (char c in label) if (char.IsControl(c)) throw new ArgumentException("Labels cannot contain control characters.", nameof(label));
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
                ValidateSplit(orientation, node.Ratio); pending.Push(node.Second); pending.Push(node.First);
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
        if (node.BlockId == target) return MultiplexerLayoutNode.Split(orientation,node,newLeaf,ratio);
        if (node.BlockId is not null) return node;
        return node with { First = SplitLeaf(node.First!,target,newLeaf,orientation,ratio), Second = SplitLeaf(node.Second!,target,newLeaf,orientation,ratio) };
    }
    private static MultiplexerLayoutNode? RemoveLeaf(MultiplexerLayoutNode? node,Guid blockId)
    {
        if (node is null || node.BlockId == blockId) return null;
        if (node.BlockId is not null) return node;
        var first = RemoveLeaf(node.First,blockId); var second = RemoveLeaf(node.Second,blockId);
        return first is null ? second : second is null ? first : node with { First=first,Second=second };
    }
    private static void RemoveBlock(Session session,MultiplexerBlock block)
    {
        Placement p = session.Placements[block.Id];
        if (p.Kind == MultiplexerPaneKind.Tiled)
        {
            Window window = FindWindow(session,p.Window); window.Layout = RemoveLeaf(window.Layout,block.Id);
        }
        session.Placements.Remove(block.Id); session.Blocks.Remove(block.Id); session.BlockOrder.Remove(block.Id);
        session.Changed("block_closed",p.Window,block.Id);
    }
    private static MultiplexerBlockDescriptor DescribeBlock(Session session,MultiplexerBlock block)
    {
        var p=session.Placements[block.Id]; return block.Describe() with { WindowId=p.Window,Placement=p.Kind,FloatingBounds=p.Bounds };
    }
    private static MultiplexerSessionDescriptor Describe(Session session)
    {
        var windows=new MultiplexerWindowDescriptor[session.Windows.Count];
        for(int i=0;i<windows.Length;i++)windows[i]=Describe(session.Windows[i]);
        var blocks=new MultiplexerBlockDescriptor[session.Blocks.Count]; int index=0;
        foreach(Guid id in session.BlockOrder) blocks[index++]=DescribeBlock(session,session.Blocks[id]);
        return new(session.Id,session.Label,session.Revision,session.CreatedUtc,windows,blocks);
    }
    private static MultiplexerWindowDescriptor Describe(Window window) => new(window.Id,window.Label,window.Layout);
    private static MultiplexerException Capacity(string message) => new(MultiplexerErrorCode.CapacityExceeded,message);
    private static MultiplexerException Conflict(string message) => new(MultiplexerErrorCode.Conflict,message);
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed,this);

    private sealed class Session(string label,WorkspaceActivityHub activity)
    {
        internal Guid Id {get;}=Guid.NewGuid();
        internal string Label {get;set;}=label;
        internal long Revision {get;private set;}=1;
        internal DateTimeOffset CreatedUtc {get;}=DateTimeOffset.UtcNow;
        internal List<Window> Windows {get;}=[];
        internal Dictionary<Guid,MultiplexerBlock> Blocks {get;}=[];
        internal Dictionary<Guid,Placement> Placements {get;}=[];
        internal List<Guid> BlockOrder {get;}=[];
        internal TaskCompletionSource? PendingChange {get;set;}
        internal int WatcherCount {get;set;}
        internal void Changed(string? type=null,Guid? window=null,Guid? block=null,string? label=null)
        {
            activity.Change(Id,++Revision,type,window,block,label);
            PendingChange?.TrySetResult(); PendingChange=null;
        }
        internal void End() { activity.Close(Id); ++Revision; PendingChange?.TrySetResult(); PendingChange=null; }
    }
    private sealed class Window(string label)
    {
        internal Guid Id {get;}=Guid.NewGuid();
        internal string Label {get;set;}=label;
        internal MultiplexerLayoutNode? Layout {get;set;}
    }
}
