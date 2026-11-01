// Copyright (c) Royal Apps. Licensed under the MIT license.
namespace RoyalTerminal.Multiplexing.Server;

internal sealed record ViewActionForm(string Name, string Title, string? RequiredArgument = null, string[]? AcceptedValues = null);
internal sealed record ViewActionCatalog(Guid ClientId, Guid SessionId, string Kind, int NavigationVersion, IReadOnlyList<ViewActionForm> Forms);

/// <summary>Discovery and enqueue admission use the same closed set. None of these commands sends input or mutates the workspace.</summary>
internal static class ViewCapabilities
{
    private static readonly ViewActionForm[] Basic = [new("pane.focus", "Focus a terminal", "blockId"), new("window.focus", "Show a window", "windowId")];
    private static readonly ViewActionForm[] Navigation = [.. Basic,
        new("pane.focus", "Navigate among visible terminals", "direction", ["left", "right", "up", "down", "next", "previous"]),
        new("client.tab.next", "Next window"), new("client.tab.previous", "Previous window")];
    private static readonly ViewActionForm[] Overview = [.. Navigation, new("pane.zoom", "Toggle pane zoom")];
    private static readonly ViewActionForm[] Browser = [.. Overview, new("client.palette", "Open command palette"), new("client.sidebar.toggle", "Toggle sidebar")];

    internal static int Negotiate(string kind, int offered)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(offered,1);
        return kind switch { "native" => Math.Min(offered,3), "browser" or "mobile" => 1, _ => throw new ArgumentException("Unknown presentation profile.") };
    }
    internal static IReadOnlyList<ViewActionForm> Forms(string kind, int version) => (kind, version) switch
    {
        ("browser", 1) => Browser,
        ("native", 3) => Overview,
        ("native", 2) => Navigation,
        ("native" or "mobile", 1) => Basic,
        _ => throw new ArgumentException("Unknown presentation profile."),
    };
    internal static bool Allows(string kind, int version, ViewAction action)
    {
        foreach (var form in Forms(kind, version))
        {
            if (form.Name != action.Name) continue;
            bool match = form.RequiredArgument switch
            {
                null => action is { BlockId: null, WindowId: null, Direction: null },
                "blockId" => action is { BlockId: { } b, WindowId: null, Direction: null } && b != Guid.Empty,
                "windowId" => action is { BlockId: null, WindowId: { } w, Direction: null } && w != Guid.Empty,
                "direction" => action is { BlockId: null, WindowId: null, Direction: { } d } && Array.IndexOf(form.AcceptedValues!, d) >= 0,
                _ => false,
            };
            if (match) return true;
        }
        return false;
    }
}
