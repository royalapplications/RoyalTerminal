// Copyright (c) Royal Apps. Licensed under the MIT license.
namespace RoyalTerminal.Multiplexing;
/// <summary>How a terminal is placed inside its home window.</summary>
public enum MultiplexerPaneKind
{
    /// <summary>A leaf in the split tree.</summary>
    Tiled,
    /// <summary>A rectangle over the tiled content.</summary>
    Floating,
    /// <summary>No visible rectangle; process and attachments are unchanged.</summary>
    Unplaced,
}
/// <summary>Coordinates in ten-thousandths of the window, independent of client DPI.</summary>
public sealed record MultiplexerFloatBounds(int X,int Y,int Width,int Height)
{
    /// <summary>Default rectangle for a newly floated pane.</summary>
    public static MultiplexerFloatBounds Default {get;}=new(1500,1500,7000,7000);
    internal void Validate(){if(Width is <1 or >10000||Height is <1 or >10000||X<0||Y<0||X>10000-Width||Y>10000-Height)throw new ArgumentOutOfRangeException(nameof(MultiplexerFloatBounds),"The rectangle must fit within [0,10000] on both axes.");}
}
public sealed partial class MultiplexerBroker
{
    private readonly record struct Placement(Guid Window,MultiplexerPaneKind Kind,MultiplexerFloatBounds? Bounds=null);
    private static Placement GetPlacement(Session s,Guid id)=>s.Placements.TryGetValue(id,out var p)?p:throw new MultiplexerException(MultiplexerErrorCode.NotFound,"Block not found.");
    /// <summary>Move an existing process without replacing its transport, cursor or control lease. A newly floated block is on top; a geometry edit preserves depth.</summary>
    public MultiplexerSessionDescriptor PlaceBlock(Guid sessionId,Guid blockId,Guid windowId,MultiplexerPaneKind kind,Guid? relativeTo=null,MultiplexerSplitOrientation orientation=MultiplexerSplitOrientation.Horizontal,double ratio=.5,bool before=false,MultiplexerFloatBounds? bounds=null,long? expectedRevision=null)
    {
        if(!Enum.IsDefined(kind))throw new ArgumentOutOfRangeException(nameof(kind));ValidateSplit(orientation,ratio);
        if(kind!=MultiplexerPaneKind.Tiled&&(relativeTo.HasValue||before))throw new ArgumentException("Only a tile can have a split target or ordering.");
        if(kind!=MultiplexerPaneKind.Floating&&bounds is not null)throw new ArgumentException("Only a floating block accepts bounds.");bounds?.Validate();
        lock(_gate){var s=FindSession(sessionId);CheckRevision(s,expectedRevision);var old=GetPlacement(s,blockId);var source=FindWindow(s,old.Window);var dest=FindWindow(s,windowId);
            if(relativeTo is {} anchor){if(anchor==blockId)throw new ArgumentException("Cannot split a block against itself.");var target=GetPlacement(s,anchor);if(target.Window!=dest.Id||target.Kind!=MultiplexerPaneKind.Tiled)throw Conflict("Split target is not tiled in the destination window.");}
            Placement next=new(windowId,kind,kind==MultiplexerPaneKind.Floating?bounds??old.Bounds??MultiplexerFloatBounds.Default:null);if(old==next&&kind!=MultiplexerPaneKind.Tiled)return Describe(s);
            var sourceTree=old.Kind==MultiplexerPaneKind.Tiled?RemoveLeaf(source.Layout,blockId):source.Layout;var destTree=source==dest?sourceTree:dest.Layout;
            if(kind==MultiplexerPaneKind.Tiled){var leaf=MultiplexerLayoutNode.Leaf(blockId);MultiplexerLayoutNode Join(MultiplexerLayoutNode other)=>before?MultiplexerLayoutNode.Split(orientation,leaf,other,ratio):MultiplexerLayoutNode.Split(orientation,other,leaf,ratio);destTree=relativeTo is {} id?Rewrite(destTree,n=>n.BlockId==id?Join(n):n):destTree is null?leaf:Join(destTree);}
            if(source!=dest||old.Kind!=kind){if(old.Kind==MultiplexerPaneKind.Floating)source.Floats.Remove(blockId);if(kind==MultiplexerPaneKind.Floating)dest.Floats.Add(blockId);}
            source.Layout=sourceTree;dest.Layout=destTree;s.Placements[blockId]=next;s.Changed();return Describe(s);}
    }
    /// <summary>Reorder a single float by one position or to an edge. A no-op does not advance the revision.</summary>
    public MultiplexerSessionDescriptor StackLayer(Guid sessionId,Guid blockId,bool raise,bool toEdge=false,long? expectedRevision=null)
    {
        lock(_gate){var s=FindSession(sessionId);CheckRevision(s,expectedRevision);var p=GetPlacement(s,blockId);if(p.Kind!=MultiplexerPaneKind.Floating)throw Conflict("Only floating blocks have a stacking order.");
            var floats=FindWindow(s,p.Window).Floats;int i=floats.IndexOf(blockId);if(i<0)throw new InvalidOperationException("Floating layer is missing.");int end=raise?floats.Count-1:0,to=toEdge?end:Math.Clamp(i+(raise?1:-1),0,floats.Count-1);
            if(i!=to){floats.RemoveAt(i);floats.Insert(to,blockId);s.Changed();}return Describe(s);}
    }
    /// <summary>Exchange complete placements (including bounds and stacking slots), without swapping process identities.</summary>
    public MultiplexerSessionDescriptor SwapBlocks(Guid sessionId,Guid firstId,Guid secondId,long? expectedRevision=null)
    {
        lock(_gate){var s=FindSession(sessionId);CheckRevision(s,expectedRevision);var a=GetPlacement(s,firstId);var b=GetPlacement(s,secondId);if(firstId==secondId)return Describe(s);
            var first=FindWindow(s,a.Window);var second=FindWindow(s,b.Window);Guid Exchange(Guid id)=>id==firstId?secondId:id==secondId?firstId:id;
            MultiplexerLayoutNode Leaf(MultiplexerLayoutNode n)=>Exchange(n.BlockId!.Value) is var id&&id!=n.BlockId?n with{BlockId=id}:n;
            var t1=Rewrite(first.Layout,Leaf);var t2=first==second?t1:Rewrite(second.Layout,Leaf);void Replace(List<Guid> ids){for(int i=0;i<ids.Count;i++)ids[i]=Exchange(ids[i]);}
            Replace(first.Floats);if(first!=second)Replace(second.Floats);first.Layout=t1;second.Layout=t2;s.Placements[firstId]=b;s.Placements[secondId]=a;s.Changed();return Describe(s);}
    }
    private static MultiplexerLayoutNode? Rewrite(MultiplexerLayoutNode? n,Func<MultiplexerLayoutNode,MultiplexerLayoutNode> leaf)
    {if(n is null)return null;if(n.BlockId.HasValue)return leaf(n);var a=Rewrite(n.First,leaf);var b=Rewrite(n.Second,leaf);return ReferenceEquals(a,n.First)&&ReferenceEquals(b,n.Second)?n:n with{First=a,Second=b};}
    /// <summary>Move a window to a one-based display position.</summary>
    public MultiplexerSessionDescriptor ReorderWindow(Guid sessionId,Guid windowId,int position,long? expectedRevision=null)
    {lock(_gate){var s=FindSession(sessionId);CheckRevision(s,expectedRevision);var w=FindWindow(s,windowId);if(position<1||position>s.Windows.Count)throw new ArgumentOutOfRangeException(nameof(position));if(s.Windows[position-1]!=w){s.Windows.Remove(w);s.Windows.Insert(position-1,w);s.Changed();}return Describe(s);}}
    /// <summary>Close a window, preserving its blocks on an explicitly selected shelf.</summary>
    public MultiplexerSessionDescriptor CloseWindow(Guid sessionId,Guid windowId,Guid? preserveIn=null,long? expectedRevision=null)
    {lock(_gate){var s=FindSession(sessionId);CheckRevision(s,expectedRevision);var w=FindWindow(s,windowId);if(s.Windows.Count==1)throw Conflict("Cannot remove the last window; close the session explicitly instead.");if(preserveIn==windowId)throw new ArgumentException("The destination must be a different window.",nameof(preserveIn));var dest=preserveIn is {} id?FindWindow(s,id):null;var owned=s.Placements.Where(x=>x.Value.Window==windowId).Select(x=>x.Key).ToArray();if(owned.Length!=0&&dest is null)throw Conflict("Specify a destination for this window's running or exited blocks.");foreach(var b in owned)s.Placements[b]=new(dest!.Id,MultiplexerPaneKind.Unplaced);s.Windows.Remove(w);s.Changed("window_closed",windowId);return Describe(s);}}
    /// <summary>Wait for a structural revision without polling processes. A removed session raises NotFound.</summary>
    public async ValueTask<MultiplexerSessionDescriptor> WaitForSessionChangeAsync(Guid sessionId,long afterRevision,CancellationToken cancellationToken=default)
    {ArgumentOutOfRangeException.ThrowIfNegative(afterRevision);Session s;Task signal;lock(_gate){s=FindSession(sessionId);if(afterRevision>s.Revision)throw Conflict("Revision is ahead of this session.");if(afterRevision<s.Revision)return Describe(s);if(s.WatcherCount>=64)throw Capacity("Too many structural change waiters for this session.");signal=(s.PendingChange??=new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;++s.WatcherCount;}try{await signal.WaitAsync(cancellationToken).ConfigureAwait(false);return GetSession(sessionId);}finally{lock(_gate)--s.WatcherCount;}}
}
