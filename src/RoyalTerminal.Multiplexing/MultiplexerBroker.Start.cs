// Copyright (c) Royal Apps. Licensed under the MIT license.
using RoyalTerminal.Terminal;
namespace RoyalTerminal.Multiplexing;

public sealed partial class MultiplexerBroker
{
    /// <summary>Starts a block directly in its intended placement. No temporary tile is ever observable.</summary>
    public ValueTask<MultiplexerBlockDescriptor> CreatePlacedBlockAsync(Guid sessionId, Guid? windowId,
        MultiplexerPaneKind placement, MultiplexerFloatBounds? bounds, string label, ITerminalTransportOptions options,
        long? expectedRevision = null, CancellationToken cancellationToken = default) =>
        StartBlockAsync(sessionId, windowId, null, MultiplexerSplitOrientation.Horizontal, .5, label, options,expectedRevision,cancellationToken,placement,bounds);

    /// <summary>Split an existing floating leaf with a tree of shelved blocks. Its siblings, z-order and outer frame are preserved.</summary>
    public MultiplexerFloatingLayer SplitFrame(Guid sessionId,Guid anchor,MultiplexerLayoutNode addition,MultiplexerSplitOrientation axis,double ratio,bool before,long expectedRevision)
    {
        ValidateSplit(axis,ratio);
        lock(_gate)
        {
            var s=FindSession(sessionId);CheckRevision(s,expectedRevision);var p=GetPlacement(s,anchor);if(p.Kind!=MultiplexerPaneKind.Floating)throw Conflict("Expected a floating anchor.");
            var w=FindWindow(s,p.Window);var next=InsertFrame(w,p,anchor,addition,axis,ratio,before);var positions=FloatTreeGeometry.Project(next.Layout,next.Bounds);
            foreach(var id in FloatTreeGeometry.Project(addition,new(0,0,10000,10000)).Keys)
                if(GetPlacement(s,id) is not {Kind:MultiplexerPaneKind.Unplaced} candidate||candidate.Window!=w.Id)throw Conflict("New leaves must be shelved in this window.");
            PublishInsertion(s,w,p,anchor,next,positions);s.Changed();return next;
        }
    }
    private static MultiplexerFloatingLayer InsertFrame(Window w,Placement p,Guid anchor,MultiplexerLayoutNode addition,MultiplexerSplitOrientation axis,double ratio,bool before)
    {
        var old=p.Layer is Guid id?w.Groups[id]:new MultiplexerFloatingLayer(Guid.NewGuid(),p.Bounds!,MultiplexerLayoutNode.Leaf(anchor));
        return old with{Layout=Rewrite(old.Layout,n=>n.BlockId!=anchor?n:MultiplexerLayoutNode.Split(axis,before?addition:n,before?n:addition,ratio))!};
    }
    private static void PublishInsertion(Session s,Window w,Placement old,Guid anchor,MultiplexerFloatingLayer next,IReadOnlyDictionary<Guid,MultiplexerFloatBounds> positions)
    {
        var previous=old.Layer is Guid key?w.Floats.Where(id=>s.Placements[id].Layer==key).ToArray():new[]{anchor};
        int start=w.Floats.IndexOf(previous[0]);w.Floats.RemoveRange(start,previous.Length);w.Floats.InsertRange(start,positions.Keys);Install(s,w,next,positions);
    }
    private async ValueTask<MultiplexerBlockDescriptor> StartBlockAsync(Guid sessionId, Guid? windowId,Guid? targetBlockId, MultiplexerSplitOrientation orientation, double ratio, string label,
        ITerminalTransportOptions options,long? expectedRevision,CancellationToken cancellationToken,MultiplexerPaneKind placement=MultiplexerPaneKind.Tiled,MultiplexerFloatBounds? bounds=null)
    {
        ArgumentNullException.ThrowIfNull(options);if(!Enum.IsDefined(placement))throw new ArgumentOutOfRangeException(nameof(placement));
        if(bounds is not null&&placement!=MultiplexerPaneKind.Floating)throw new ArgumentException("Bounds require a floating layer.",nameof(bounds));
        if(placement==MultiplexerPaneKind.Floating)(bounds??=MultiplexerFloatBounds.Default).Validate();
        ValidateLabel(label);ValidateSplit(orientation,ratio);MultiplexerBlock.ValidateDimensions(options.Dimensions,_options);cancellationToken.ThrowIfCancellationRequested();
        MultiplexerBlock block;
        lock(_gate)
        {
            var s=FindSession(sessionId);CheckRevision(s,expectedRevision);Placement? floating=null;
            if(targetBlockId is Guid target){var p=GetPlacement(s,target);if(p.Kind==MultiplexerPaneKind.Unplaced||placement!=MultiplexerPaneKind.Tiled)throw Conflict("Only visible terminals can be split.");windowId=p.Window;if(p.Kind==MultiplexerPaneKind.Floating)floating=p;}
            var w=windowId is Guid id?FindWindow(s,id):s.Windows[0];if(s.Blocks.Count>=_options.MaxBlocksPerSession)throw Capacity("The block limit has been reached.");
            if(floating is {} f){var preview=InsertFrame(w,f,targetBlockId!.Value,MultiplexerLayoutNode.Leaf(Guid.NewGuid()),orientation,ratio,false);FloatTreeGeometry.Project(preview.Layout,preview.Bounds);}
            block=new(sessionId,w.Id,label,_factory.Create(options),options.Dimensions,_options);s.Blocks.Add(block.Id,block);s.BlockOrder.Add(block.Id);
            var leaf=MultiplexerLayoutNode.Leaf(block.Id);
            if(floating is {} original){var next=InsertFrame(w,original,targetBlockId!.Value,leaf,orientation,ratio,false);PublishInsertion(s,w,original,targetBlockId.Value,next,FloatTreeGeometry.Project(next.Layout,next.Bounds));}
            else
            {
                s.Placements.Add(block.Id,new(w.Id,placement,bounds));
                if(placement==MultiplexerPaneKind.Tiled)w.Layout=targetBlockId is Guid anchor?SplitLeaf(w.Layout!,anchor,leaf,orientation,ratio):w.Layout is null?leaf:MultiplexerLayoutNode.Split(orientation,w.Layout,leaf,ratio);
                else if(placement==MultiplexerPaneKind.Floating)w.Floats.Add(block.Id);
            }
            s.Changed("block_created",w.Id,block.Id);
        }
        try{await block.StartAsync(options,cancellationToken).ConfigureAwait(false);lock(_gate){if(_sessions.TryGetValue(sessionId,out var s)&&s.Placements.ContainsKey(block.Id))return DescribeBlock(s,block);throw new MultiplexerException(MultiplexerErrorCode.Disconnected,"Block closed while starting.");}}
        catch{lock(_gate){if(_sessions.TryGetValue(sessionId,out var s)&&s.Blocks.ContainsKey(block.Id)){RemoveBlock(s,block);_retiring.Add(block);}}try{await block.CloseAsync().ConfigureAwait(false);}finally{lock(_gate)_retiring.Remove(block);}throw;}
    }
}
