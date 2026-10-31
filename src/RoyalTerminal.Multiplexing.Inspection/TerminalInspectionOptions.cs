// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;

namespace RoyalTerminal.Multiplexing.Inspection;

/// <summary>Bounds for one authoritative server-side terminal state, independent of output replay.</summary>
public sealed record TerminalInspectionOptions
{
    /// <summary>Maximum retained primary-screen history rows.</summary>
    public int ScrollbackLimit { get; init; } = 10_000;

    /// <summary>Logical history page budget. Active viewport pages are not evicted to meet this budget.</summary>
    public ulong ScrollbackBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>Maximum rows or columns accepted before allocating a screen.</summary>
    public int MaxDimension { get; init; } = 4096;

    /// <summary>Maximum visible cells, bounding the product of rows and columns.</summary>
    public int MaxScreenCells { get; init; } = 1_048_576;

    /// <summary>Maximum queued terminal query replies. Overflow records a response error and stops replying.</summary>
    public int MaxPendingResponseBytes { get; init; } = 64 * 1024;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ScrollbackLimit);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxDimension, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxDimension, ushort.MaxValue);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxScreenCells, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxPendingResponseBytes, 1);
    }

    internal void ValidateDimensions(TerminalSessionDimensions dimensions)
    {
        if (dimensions.Columns < 1 || dimensions.Rows < 1 ||
            dimensions.Columns > MaxDimension || dimensions.Rows > MaxDimension ||
            (long)dimensions.Columns * dimensions.Rows > MaxScreenCells ||
            dimensions.WidthPixels < 0 || dimensions.HeightPixels < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Terminal dimensions exceed the inspection limits.");
        }
    }
}
