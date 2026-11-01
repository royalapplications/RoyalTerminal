// Copyright (c) Royal Apps. Licensed under the MIT license.
namespace RoyalTerminal.Multiplexing;
public sealed partial class MultiplexerBroker
{
    private Session FindSession(Guid id){ThrowIfDisposed();return _sessions.TryGetValue(id,out var session)?session:throw new MultiplexerException(MultiplexerErrorCode.NotFound,"Session not found.");}
    private static Window FindWindow(Session s,Guid id)=>s.Windows.Find(w=>w.Id==id)??throw new MultiplexerException(MultiplexerErrorCode.NotFound,"Window not found.");
    private static MultiplexerBlock FindBlock(Session s,Guid id)=>s.Blocks.TryGetValue(id,out var b)?b:throw new MultiplexerException(MultiplexerErrorCode.NotFound,"Block not found.");
    private static void CheckRevision(Session s,long? expected){if(expected is {} r&&r!=s.Revision)throw Conflict($"Expected revision {r}, but the current session revision is {s.Revision}.");}
    private void ValidateLabel(string label){ArgumentException.ThrowIfNullOrWhiteSpace(label);if(label.Length>_options.MaxLabelLength)throw new ArgumentException("The label exceeds the configured length limit.",nameof(label));foreach(char c in label)if(char.IsControl(c))throw new ArgumentException("Labels cannot contain control characters.",nameof(label));}
    private void ValidateLayout(Session s,Window w,MultiplexerLayoutNode? layout)
    {
        HashSet<Guid> expected=[];foreach(var (id,p) in s.Placements)if(p.Window==w.Id&&p.Kind==MultiplexerPaneKind.Tiled)expected.Add(id);
        HashSet<Guid> actual=[];Stack<MultiplexerLayoutNode> pending=new();if(layout!=null)pending.Push(layout);int visited=0;
        while(pending.TryPop(out var node)){
            if(++visited>2L*_options.MaxBlocksPerSession)throw new ArgumentException("The layout exceeds the configured block limit.",nameof(layout));
            if(node.BlockId is {} id){if(node.Orientation!=null||node.First!=null||node.Second!=null||!actual.Add(id))throw new ArgumentException("Each layout leaf must reference one distinct block.",nameof(layout));}
            else {if(node.Orientation is not {} axis||node.First==null||node.Second==null)throw new ArgumentException("A split requires an orientation and two children.",nameof(layout));ValidateSplit(axis,node.Ratio);pending.Push(node.Second);pending.Push(node.First);}}
        if(!expected.SetEquals(actual))throw new ArgumentException("The layout must contain every tiled block in its window exactly once.",nameof(layout));
    }
    private static void ValidateSplit(MultiplexerSplitOrientation orientation,double ratio){if(!Enum.IsDefined(orientation))throw new ArgumentOutOfRangeException(nameof(orientation));if(!double.IsFinite(ratio)||ratio<=0||ratio>=1)throw new ArgumentOutOfRangeException(nameof(ratio));}
    private static MultiplexerLayoutNode SplitLeaf(MultiplexerLayoutNode n,Guid target,MultiplexerLayoutNode leaf,MultiplexerSplitOrientation axis,double ratio)
    {if(n.BlockId==target)return MultiplexerLayoutNode.Split(axis,n,leaf,ratio);return n.BlockId.HasValue?n:n with{First=SplitLeaf(n.First!,target,leaf,axis,ratio),Second=SplitLeaf(n.Second!,target,leaf,axis,ratio)};}
    private static MultiplexerLayoutNode? RemoveLeaf(MultiplexerLayoutNode? n,Guid id)
    {if(n is null||n.BlockId==id)return null;if(n.BlockId.HasValue)return n;var a=RemoveLeaf(n.First,id);var b=RemoveLeaf(n.Second,id);return a is null?b:b is null?a:n with{First=a,Second=b};}
    private static void RemoveBlock(Session s,MultiplexerBlock block)
    {
        var p=s.Placements[block.Id];var w=FindWindow(s,p.Window);
        if(p.Kind==MultiplexerPaneKind.Tiled)w.Layout=RemoveLeaf(w.Layout,block.Id);else if(p.Kind==MultiplexerPaneKind.Floating)w.Floats.Remove(block.Id);
        s.Placements.Remove(block.Id);s.Blocks.Remove(block.Id);s.BlockOrder.Remove(block.Id);s.Changed("block_closed",p.Window,block.Id);
    }
    private static MultiplexerBlockDescriptor DescribeBlock(Session s,MultiplexerBlock b){var p=s.Placements[b.Id];return b.Describe() with{WindowId=p.Window,Placement=p.Kind,FloatingBounds=p.Bounds};}
    private static MultiplexerSessionDescriptor Describe(Session s)
    {var windows=new MultiplexerWindowDescriptor[s.Windows.Count];for(int i=0;i<windows.Length;i++)windows[i]=Describe(s.Windows[i]);var blocks=new MultiplexerBlockDescriptor[s.Blocks.Count];int index=0;foreach(Guid id in s.BlockOrder)blocks[index++]=DescribeBlock(s,s.Blocks[id]);return new(s.Id,s.Label,s.Revision,s.CreatedUtc,windows,blocks);}
    private static MultiplexerWindowDescriptor Describe(Window w)=>new(w.Id,w.Label,w.Layout){FloatingOrder=w.Floats.ToArray()};
    private static MultiplexerException Capacity(string message)=>new(MultiplexerErrorCode.CapacityExceeded,message);
    private static MultiplexerException Conflict(string message)=>new(MultiplexerErrorCode.Conflict,message);
    private void ThrowIfDisposed()=>ObjectDisposedException.ThrowIf(_disposed,this);
    private sealed class Session(string label,WorkspaceActivityHub activity)
    {
        internal Guid Id{get;}=Guid.NewGuid();internal string Label{get;set;}=label;internal long Revision{get;private set;}=1;internal DateTimeOffset CreatedUtc{get;}=DateTimeOffset.UtcNow;
        internal List<Window> Windows{get;}=[];internal Dictionary<Guid,MultiplexerBlock> Blocks{get;}=[];internal Dictionary<Guid,Placement> Placements{get;}=[];internal List<Guid> BlockOrder{get;}=[];
        internal TaskCompletionSource? PendingChange{get;set;}internal int WatcherCount{get;set;}
        internal void Changed(string? type=null,Guid? window=null,Guid? block=null,string? label=null){activity.Change(Id,++Revision,type,window,block,label);PendingChange?.TrySetResult();PendingChange=null;}
        internal void End(){activity.Close(Id);++Revision;PendingChange?.TrySetResult();PendingChange=null;}
    }
    private sealed class Window(string label){internal Guid Id{get;}=Guid.NewGuid();internal string Label{get;set;}=label;internal MultiplexerLayoutNode? Layout{get;set;}internal List<Guid> Floats{get;}=[];}
}
