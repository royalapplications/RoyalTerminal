// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>Requested or available extra rows outside a terminal viewport.</summary>
/// <param name="Above">Rows above viewport row zero.</param>
/// <param name="Below">Rows below the last viewport row.</param>
public readonly record struct TerminalRenderOverscan(ushort Above, ushort Below);

/// <summary>
/// Opaque managed row-storage identity. Default is invalid; compare only for
/// equality. An identity is not a content revision: in-place writes keep it,
/// while COW detachment or allocation replacement changes it. Retaining this
/// value retains that storage. Shared COW rows have the same identity.
/// </summary>
public readonly struct TerminalRenderRowId : IEquatable<TerminalRenderRowId>
{
    private readonly object? _storage;
    internal TerminalRenderRowId(object storage) => _storage = storage;
    /// <summary>Whether this value identifies allocated row storage.</summary>
    public bool IsValid => _storage is not null;
    /// <inheritdoc />
    public bool Equals(TerminalRenderRowId other) => ReferenceEquals(_storage, other._storage);
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TerminalRenderRowId other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => _storage is null ? 0 : RuntimeHelpers.GetHashCode(_storage);
    /// <summary>Compares storage identity, not cell contents.</summary>
    public static bool operator ==(TerminalRenderRowId left, TerminalRenderRowId right) => left.Equals(right);
    /// <summary>Compares storage identity, not cell contents.</summary>
    public static bool operator !=(TerminalRenderRowId left, TerminalRenderRowId right) => !left.Equals(right);
}

/// <summary>A borrowed row and its signed viewport location.</summary>
/// <param name="Row">Live row; callers must keep its screen stable while reading.</param>
/// <param name="ViewportY">Negative above the viewport, viewport height or greater below it.</param>
/// <param name="StorageIndex">Slot in the requested layout, including missing overscan slots.</param>
public readonly record struct TerminalRenderRow(TerminalRow Row, int ViewportY, int StorageIndex)
{
    /// <summary>Gets this row's current storage identity.</summary>
    public TerminalRenderRowId Id => Row.RenderId;
}

/// <summary>
/// Zero-copy row view over one screen's viewport and available overscan. This is
/// not a snapshot: keep the screen lock held and do not resize, scroll, mutate,
/// or publish another screen state until done. Only the extracted row IDs may
/// be retained independently. Default is an empty view.
/// </summary>
public readonly ref struct TerminalRenderViewport
{
    private readonly TerminalScreen? _screen;
    private readonly int _firstAbsoluteRow;

    internal TerminalRenderViewport(TerminalScreen screen, TerminalRenderOverscan request)
    {
        _screen = screen;
        Columns = screen.Columns;
        Rows = screen.ViewportRows;
        RequestedOverscan = request;
        int top = screen.ViewportTopAbsoluteRow;
        int firstAccessible = Math.Max(0, screen.TotalRows - Rows - screen.MaxScrollOffset);
        int above = Math.Min(request.Above, Math.Max(0, top - firstAccessible));
        int below = Math.Min(request.Below, Math.Max(0, screen.TotalRows - top - Rows));
        CapturedOverscan = new((ushort)above, (ushort)below);
        _firstAbsoluteRow = top - above;
        Count = checked(Rows + above + below);
    }

    /// <summary>Viewport columns, excluding any overscan.</summary>
    public int Columns { get; }
    /// <summary>Viewport rows, excluding any overscan.</summary>
    public int Rows { get; }
    /// <summary>The requested layout; missing rows do not consume storage.</summary>
    public TerminalRenderOverscan RequestedOverscan { get; }
    /// <summary>Rows actually available outside this viewport.</summary>
    public TerminalRenderOverscan CapturedOverscan { get; }
    /// <summary>Number of iterable rows, including available overscan.</summary>
    public int Count { get; }
    /// <summary>Index of viewport row zero in the compact iterable rows.</summary>
    public int ViewportStart => CapturedOverscan.Above;

    /// <summary>Gets a row by compact capture index (not viewport Y when overscan is present).</summary>
    /// <param name="index">Index in the available row range.</param>
    public TerminalRenderRow this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            int viewportY = index - CapturedOverscan.Above;
            return new(_screen!.GetRow(_firstAbsoluteRow + index), viewportY, RequestedOverscan.Above + viewportY);
        }
    }
}

public sealed partial class TerminalScreen
{
    /// <summary>
    /// Borrows the viewport plus existing adjacent history rows without allocating
    /// or copying cells. The caller must keep this screen stable for the view's lifetime.
    /// Native VT mirrors contain only rows mirrored by their adapter; this method
    /// does not fetch native history or change any engine's overscan request.
    /// </summary>
    /// <param name="overscan">Requested rows outside the viewport; zero preserves ordinary rendering.</param>
    /// <returns>A lock-scoped view with actual counts, signed row positions and storage identities.</returns>
    public TerminalRenderViewport GetRenderViewport(TerminalRenderOverscan overscan = default) => new(this, overscan);
}
