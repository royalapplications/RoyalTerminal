// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Durable workspace restore contracts.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Stable render mode identifiers persisted with workspace tabs.
/// </summary>
public static class TerminalWorkspaceRenderModes
{
    /// <summary>
    /// Let the host select the default renderer.
    /// </summary>
    public const string Default = "Default";

    /// <summary>
    /// Managed text renderer.
    /// </summary>
    public const string Text = "Text";

    /// <summary>
    /// Skia renderer.
    /// </summary>
    public const string Skia = "Skia";

    /// <summary>
    /// Ghostty-backed renderer.
    /// </summary>
    public const string Ghostty = "Ghostty";
}

/// <summary>
/// Stable split orientation identifiers persisted with workspace panes.
/// </summary>
public static class TerminalWorkspacePaneSplitOrientations
{
    /// <summary>
    /// Split the available space into side-by-side panes.
    /// </summary>
    public const string Horizontal = "Horizontal";

    /// <summary>
    /// Split the available space into stacked panes.
    /// </summary>
    public const string Vertical = "Vertical";
}

/// <summary>
/// Versioned workspace document used to restore terminal windows, tabs, and panes.
/// </summary>
public sealed record TerminalWorkspaceDocument
{
    /// <summary>
    /// Current supported workspace document format version.
    /// </summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>
    /// Workspace document format version.
    /// </summary>
    public int FormatVersion { get; init; } = CurrentFormatVersion;

    /// <summary>
    /// Optional selected window identifier.
    /// </summary>
    public string? SelectedWindowId { get; init; }

    /// <summary>
    /// Persisted terminal windows.
    /// </summary>
    public List<TerminalWorkspaceWindow> Windows { get; init; } = [];
}

/// <summary>
/// Persisted terminal window state.
/// </summary>
public sealed record TerminalWorkspaceWindow
{
    /// <summary>
    /// Stable window identifier.
    /// </summary>
    public string Id { get; init; } = "window-1";

    /// <summary>
    /// Optional human-readable window title.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Optional selected tab identifier within the window.
    /// </summary>
    public string? SelectedTabId { get; init; }

    /// <summary>
    /// Persisted window width in pixels.
    /// </summary>
    public int WidthPixels { get; init; } = 1200;

    /// <summary>
    /// Persisted window height in pixels.
    /// </summary>
    public int HeightPixels { get; init; } = 800;

    /// <summary>
    /// Whether the window should be restored maximized.
    /// </summary>
    public bool IsMaximized { get; init; }

    /// <summary>
    /// Whether the tab strip is hosted in the client title bar.
    /// </summary>
    public bool TabsInTitleBar { get; init; }

    /// <summary>
    /// Persisted tabs for the window.
    /// </summary>
    public List<TerminalWorkspaceTab> Tabs { get; init; } = [];
}

/// <summary>
/// Persisted terminal tab state.
/// </summary>
public sealed record TerminalWorkspaceTab
{
    /// <summary>
    /// Stable tab identifier.
    /// </summary>
    public string Id { get; init; } = "tab-1";

    /// <summary>
    /// Session profile identifier used to create the tab.
    /// </summary>
    public string ProfileId { get; init; } = "default";

    /// <summary>
    /// Optional human-readable tab title.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Optional working directory restored for local process transports.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Transport identifier captured from the session profile at the time the tab was persisted.
    /// </summary>
    public string TransportId { get; init; } = TerminalTransportIds.Pty;

    /// <summary>
    /// Optional transport-specific profile identifier for future transport profile stores.
    /// </summary>
    public string? TransportProfileId { get; init; }

    /// <summary>
    /// Stable renderer mode identifier.
    /// </summary>
    public string RenderMode { get; init; } = TerminalWorkspaceRenderModes.Default;

    /// <summary>
    /// Root pane layout for the tab.
    /// </summary>
    public TerminalWorkspacePane RootPane { get; init; } = new();
}

/// <summary>
/// Persisted terminal pane state.
/// </summary>
public sealed record TerminalWorkspacePane
{
    /// <summary>
    /// Stable pane identifier.
    /// </summary>
    public string Id { get; init; } = "root";

    /// <summary>
    /// Optional pane title override.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Optional session profile identifier for panes once split restore is enabled.
    /// </summary>
    public string? ProfileId { get; init; }

    /// <summary>
    /// Optional working directory override for panes once split restore is enabled.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Optional transport identifier override for panes once split restore is enabled.
    /// </summary>
    public string? TransportId { get; init; }

    /// <summary>
    /// Optional transport-specific profile identifier for panes once split restore is enabled.
    /// </summary>
    public string? TransportProfileId { get; init; }

    /// <summary>
    /// Optional split model. Null means the pane is a leaf session pane.
    /// </summary>
    public TerminalWorkspacePaneSplit? Split { get; init; }
}

/// <summary>
/// Persisted binary pane split layout.
/// </summary>
public sealed record TerminalWorkspacePaneSplit
{
    /// <summary>
    /// Split orientation identifier.
    /// </summary>
    public string Orientation { get; init; } = TerminalWorkspacePaneSplitOrientations.Horizontal;

    /// <summary>
    /// First pane size ratio. Values are normalized into a durable 0.05 through 0.95 range.
    /// </summary>
    public double Ratio { get; init; } = 0.5;

    /// <summary>
    /// First child pane.
    /// </summary>
    public TerminalWorkspacePane FirstPane { get; init; } = new()
    {
        Id = "first",
    };

    /// <summary>
    /// Second child pane.
    /// </summary>
    public TerminalWorkspacePane SecondPane { get; init; } = new()
    {
        Id = "second",
    };
}
