// Licensed under the MIT License.
// RoyalTerminal.Avalonia.Controls - Shared event args for terminal controls.

using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.Avalonia.Controls;

/// <summary>
/// Event args for terminal data events.
/// </summary>
public class TerminalDataEventArgs : EventArgs
{
    /// <summary>The raw terminal data.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>The raw terminal data as a span.</summary>
    public ReadOnlySpan<byte> DataSpan => Data.Span;

    public TerminalDataEventArgs(ReadOnlyMemory<byte> data) => Data = data;
}

/// <summary>
/// Event args for terminal resize events.
/// </summary>
public class TerminalSizeEventArgs : EventArgs
{
    /// <summary>New column count.</summary>
    public int Columns { get; }

    /// <summary>New row count.</summary>
    public int Rows { get; }

    public TerminalSizeEventArgs(int columns, int rows)
    {
        Columns = columns;
        Rows = rows;
    }
}

/// <summary>
/// Event args for Ghostty action dispatches.
/// </summary>
public class GhosttyActionEventArgs : EventArgs
{
    public GhosttyAction Action { get; }
    public GhosttyActionEventArgs(GhosttyAction action) => Action = action;
}
