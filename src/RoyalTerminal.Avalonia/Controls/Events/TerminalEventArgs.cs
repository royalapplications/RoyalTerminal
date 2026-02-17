// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.Controls - Shared event args for terminal controls.

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
