// Copyright (c) Royal Apps. Licensed under the MIT license.
using RoyalTerminal.Multiplexing.Desktop.WorkspaceOverview;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Mobile;

/// <summary>A valid empty window is distinct from a stale target. Never substitute an arbitrary pane for a stale anchor.</summary>
internal sealed record PhoneDestination(Guid WindowId, MultiplexerBlockDescriptor? Block);

internal static class PhoneNavigation
{
    internal static PhoneDestination? Resolve(MultiplexerSessionDescriptor s, Guid? window, Guid? block, MultiplexingViewAction action)
    {
        if (s.Windows.Count is 0 or > 64 || s.Blocks.Count > 128) return null;
        var current = block is null ? null : s.Blocks.FirstOrDefault(b => b.Id == block && b.SessionId == s.Id && b.Placement != MultiplexerPaneKind.Unplaced);
        // Follow the identity across a shared move. A missing identity is not the first terminal in a new window.
        var active = current?.WindowId ?? window ?? s.Windows[0].Id;
        int index = -1;
        for (int i=0; i<s.Windows.Count; i++) if (s.Windows[i].Id == active) { index=i; break; }
        Guid target;
        switch (action.Name)
        {
            case "pane.focus" when action.BlockId is Guid absolute:
                var destination = s.Blocks.FirstOrDefault(b => b.Id == absolute && b.SessionId == s.Id && b.Placement != MultiplexerPaneKind.Unplaced);
                return destination is null ? null : new(destination.WindowId, destination);
            case "pane.focus" when action.Direction is string dir && index >= 0:
                var slots = LayoutProjection.Build(s, active).Slots;
                var id = PaneNavigation.Resolve(slots, block, dir);
                return id is null ? null : new(active, slots.Single(p => p.Block.Id == id).Block);
            case "window.focus" when action.WindowId is Guid w && s.Windows.Any(x => x.Id == w): target=w; break;
            case "client.tab.next" or "client.tab.previous" when index >= 0:
                target=s.Windows[(index+s.Windows.Count+(action.Name=="client.tab.next"?1:-1)) % s.Windows.Count].Id; break;
            default: return null;
        }
        var visible=LayoutProjection.Build(s,target).Slots;
        return new(target, current?.WindowId==target ? current : visible.Count==0 ? null : visible[0].Block);
    }
}
