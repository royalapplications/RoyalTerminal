// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json;
namespace RoyalTerminal.Multiplexing.Server;
internal sealed partial class ApiCatalog
{
    private void RegisterLayerTools()
    {
        foreach (var raise in new[] { true, false })
            Define(raise ? "session.raise_layer" : "session.lower_layer", "Move a floating terminal one level, or to the edge with toEdge. Does not affect focus or keyboard ownership.",
                ["sessionId:string!", "blockId:string!", "toEdge:boolean", "expectedRevision:integer"], ServerJsonContext.Default.MultiplexerSessionDescriptor, (a,g,ct) =>
                {
                    var s = Mutable(a,g,ct);
                    var edge = a.Value.TryGetProperty("toEdge",out var p) && (p.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => throw new ArgumentException("toEdge must be boolean.") });
                    return Done(_workspaces.Broker.StackLayer(s.Id,a.Id("blockId"),raise,edge,s.Revision),ServerJsonContext.Default.MultiplexerSessionDescriptor);
                });
        Define("session.set_layer_order", "Atomically replace a window's complete bottom-to-top floating order.",
            ["sessionId:string!", "windowId:string!", "blocks:array!", "expectedRevision:integer"], ServerJsonContext.Default.MultiplexerSessionDescriptor, (a,g,ct) =>
            {
                var s=Mutable(a,g,ct);
                if(!a.Value.TryGetProperty("blocks",out var p)||p.ValueKind!=JsonValueKind.Array||p.GetArrayLength()>s.Blocks.Count)throw new ArgumentException("Expected floating block IDs.");
                var ids=p.EnumerateArray().Select(x=>x.ValueKind==JsonValueKind.String&&x.TryGetGuid(out var id)?id:throw new ArgumentException("Expected a block UUID.")).ToArray();
                return Done(_workspaces.Broker.SetLayerOrder(s.Id,a.Id("windowId"),ids,s.Revision),ServerJsonContext.Default.MultiplexerSessionDescriptor);
            });
    }
}
