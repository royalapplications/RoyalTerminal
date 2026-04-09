// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    public enum GhosttyRenderStateDirty : int
    {
        False = 0,
        Partial = 1,
        Full = 2,
    }

    public enum GhosttyRenderStateCursorVisualStyle : int
    {
        Bar = 0,
        Block = 1,
        Underline = 2,
        BlockHollow = 3,
    }

    public enum GhosttyRenderStateData : int
    {
        Invalid = 0,
        Cols = 1,
        Rows = 2,
        Dirty = 3,
        RowIterator = 4,
        ColorBackground = 5,
        ColorForeground = 6,
        ColorCursor = 7,
        ColorCursorHasValue = 8,
        ColorPalette = 9,
        CursorVisualStyle = 10,
        CursorVisible = 11,
        CursorBlinking = 12,
        CursorPasswordInput = 13,
        CursorViewportHasValue = 14,
        CursorViewportX = 15,
        CursorViewportY = 16,
        CursorViewportWideTail = 17,
    }

    public enum GhosttyRenderStateOption : int
    {
        Dirty = 0,
    }

    public enum GhosttyRenderStateRowData : int
    {
        Invalid = 0,
        Dirty = 1,
        Raw = 2,
        Cells = 3,
    }

    public enum GhosttyRenderStateRowOption : int
    {
        Dirty = 0,
    }

    public enum GhosttyRenderStateRowCellsData : int
    {
        Invalid = 0,
        Raw = 1,
        Style = 2,
        GraphemesLength = 3,
        GraphemesBuffer = 4,
        BackgroundColor = 5,
        ForegroundColor = 6,
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct GhosttyRenderStateColors
    {
        public nuint Size;
        public GhosttyColorRgb Background;
        public GhosttyColorRgb Foreground;
        public GhosttyColorRgb Cursor;

        [MarshalAs(UnmanagedType.U1)]
        public bool CursorHasValue;

        private fixed byte _palette[256 * 3];

        public static GhosttyRenderStateColors CreateSized()
        {
            return new GhosttyRenderStateColors
            {
                Size = (nuint)Marshal.SizeOf<GhosttyRenderStateColors>(),
            };
        }

        public void CopyPaletteTo(Span<GhosttyColorRgb> destination)
        {
            if (destination.Length < 256)
            {
                throw new ArgumentException("Destination span must contain at least 256 colors.", nameof(destination));
            }

            fixed (byte* palettePtr = _palette)
            {
                var source = new ReadOnlySpan<GhosttyColorRgb>((GhosttyColorRgb*)palettePtr, 256);
                source.CopyTo(destination);
            }
        }
    }

    [LibraryImport(LibName, EntryPoint = "ghostty_render_state_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult RenderStateNew(nint allocator, out nint state);

    [LibraryImport(LibName, EntryPoint = "ghostty_render_state_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void RenderStateFree(nint state);

    [LibraryImport(LibName, EntryPoint = "ghostty_render_state_update")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult RenderStateUpdate(nint state, nint terminal);

    [LibraryImport(LibName, EntryPoint = "ghostty_render_state_get")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult RenderStateGet(nint state, GhosttyRenderStateData data, void* output);

    [LibraryImport(LibName, EntryPoint = "ghostty_render_state_set")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult RenderStateSet(nint state, GhosttyRenderStateOption option, void* value);

    [LibraryImport(LibName, EntryPoint = "ghostty_render_state_colors_get")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult RenderStateColorsGet(nint state, ref GhosttyRenderStateColors colors);

    [LibraryImport(LibName, EntryPoint = "ghostty_render_state_row_iterator_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult RenderStateRowIteratorNew(nint allocator, out nint iterator);

    [LibraryImport(LibName, EntryPoint = "ghostty_render_state_row_iterator_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void RenderStateRowIteratorFree(nint iterator);

    [LibraryImport(LibName, EntryPoint = "ghostty_render_state_row_iterator_next")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool RenderStateRowIteratorNext(nint iterator);

    [LibraryImport(LibName, EntryPoint = "ghostty_render_state_row_get")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult RenderStateRowGet(nint iterator, GhosttyRenderStateRowData data, void* output);

    [LibraryImport(LibName, EntryPoint = "ghostty_render_state_row_set")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult RenderStateRowSet(nint iterator, GhosttyRenderStateRowOption option, void* value);

    [LibraryImport(LibName, EntryPoint = "ghostty_render_state_row_cells_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult RenderStateRowCellsNew(nint allocator, out nint cells);

    [LibraryImport(LibName, EntryPoint = "ghostty_render_state_row_cells_next")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool RenderStateRowCellsNext(nint cells);

    [LibraryImport(LibName, EntryPoint = "ghostty_render_state_row_cells_select")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult RenderStateRowCellsSelect(nint cells, ushort x);

    [LibraryImport(LibName, EntryPoint = "ghostty_render_state_row_cells_get")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult RenderStateRowCellsGet(nint cells, GhosttyRenderStateRowCellsData data, void* output);

    [LibraryImport(LibName, EntryPoint = "ghostty_render_state_row_cells_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void RenderStateRowCellsFree(nint cells);
}
