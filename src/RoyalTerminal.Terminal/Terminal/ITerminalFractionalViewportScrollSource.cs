// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>Optional native-adapter capability for atomic row/phase publication.</summary>
public interface ITerminalFractionalViewportScrollSource : ITerminalViewportScrollSource
{
    /// <summary>Position represented by the last completed render capture, including during output holds.</summary>
    TerminalViewportScrollPosition PublishedViewportPosition { get; }

    /// <summary>
    /// Requests a top row and fractional row offset together. Captures one row
    /// below when needed; synchronized output defers publication of both values.
    /// The caller holds the screen lock. Positions are clamped to accessible history.
    /// </summary>
    void SetViewportScrollPosition(TerminalViewportScrollPosition position);
}

/// <summary>Top-anchored viewport row and subrow displacement in [0,1).</summary>
public readonly record struct TerminalViewportScrollPosition
{
    /// <summary>Creates a position; the fractional component must be finite and in [0,1).</summary>
    public TerminalViewportScrollPosition(ulong topRow, double fractionalRow)
    {
        if (!double.IsFinite(fractionalRow) || fractionalRow < 0 || fractionalRow >= 1)
            throw new ArgumentOutOfRangeException(nameof(fractionalRow));
        TopRow = topRow;
        FractionalRow = fractionalRow;
    }

    /// <summary>Top row in the accessible buffer.</summary>
    public ulong TopRow { get; }
    /// <summary>Fraction of the top row scrolled above the viewport.</summary>
    public double FractionalRow { get; }

    /// <summary>Maps a pixel scrollbar range to a bounded row/phase position, preserving both endpoints.</summary>
    public static TerminalViewportScrollPosition FromPixels(double offset, double maximumOffset, ulong maximumRow)
    {
        if (!double.IsFinite(offset) || !double.IsFinite(maximumOffset) || maximumOffset <= 0 || maximumRow == 0 || offset <= 0)
            return default;
        if (offset >= maximumOffset) return new(maximumRow, 0);
        double rows = offset / maximumOffset * maximumRow;
        double nearest = Math.Round(rows);
        if (Math.Abs(rows - nearest) < 1e-9) rows = nearest;
        if (rows >= maximumRow) return new(maximumRow, 0);
        double whole = Math.Floor(rows);
        return new((ulong)whole, rows - whole);
    }
}
