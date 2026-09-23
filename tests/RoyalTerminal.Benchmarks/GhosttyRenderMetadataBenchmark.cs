// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using System.Globalization;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;

internal static class GhosttyRenderMetadataBenchmark
{
    private const int Iterations = 200_000;

    internal static void Run()
    {
        using GhosttyTerminal terminal = new(10, 3);
        using GhosttyRenderState state = new();
        terminal.Write("\u001b[1;31;44mA"u8);
        state.Update(terminal);
        state.BeginRows();
        state.MoveNextRow();
        state.BeginCurrentRowCells();
        state.MoveNextCell();
        using LegacyReader legacy = new();

        Console.WriteLine("Metadata-only: styled ASCII cell, style/grapheme-length/foreground/background; no parsing or rendering.");
        Console.WriteLine("Legacy reader reproduces the pre-change raw calls and eagerly interpolated diagnostic strings.");
        Console.WriteLine("Median of7 samples after warmup; each sample reads200,000 cells.");
        Console.WriteLine("| Path | ns/cell | Bytes/cell | Checksum |");
        Console.WriteLine("|---|---:|---:|---:|");
        Measure("legacy-individual", legacy.Read);
        Measure("individual-no-diagnostics", () => ReadCurrent(state, batched: false));
        Measure("batched-no-diagnostics", () => ReadCurrent(state, batched: true));
    }

    private static uint ReadCurrent(GhosttyRenderState state, bool batched)
    {
        GhosttyVtNative.GhosttyStyle style;
        uint length;
        if (batched)
        {
            state.GetCurrentCellMetadata(out style, out length);
        }
        else
        {
            style = state.GetCurrentCellStyle();
            length = state.GetCurrentCellGraphemeLength();
        }

        state.TryGetCurrentCellForegroundColor(out GhosttyVtNative.GhosttyColorRgb foreground);
        state.TryGetCurrentCellBackgroundColor(out GhosttyVtNative.GhosttyColorRgb background);
        return length + foreground.R + background.B + (style.Bold ? 1u : 0u);
    }

    private static void Measure(string name, Func<uint> read)
    {
        for (int i = 0; i < 20_000; i++) _ = read();
        double[] elapsed = new double[7];
        long[] allocated = new long[7];
        ulong checksum = 0;
        for (int sample = 0; sample < elapsed.Length; sample++)
        {
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < Iterations; i++) checksum += read();
            elapsed[sample] = Stopwatch.GetElapsedTime(started).TotalNanoseconds / Iterations;
            allocated[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        Array.Sort(elapsed);
        Array.Sort(allocated);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"| {name} | {elapsed[3]:F2} | {(double)allocated[3] / Iterations:F2} | {checksum} |"));
    }

    // Intentionally retains the old hot-path shape, including formatting an
    // operation name before discovering that the native read succeeded.
    private sealed unsafe class LegacyReader : IDisposable
    {
        private readonly nint _terminal;
        private readonly nint _state;
        private nint _rows;
        private nint _cells;

        internal LegacyReader()
        {
            Check(GhosttyVtNative.TerminalNew(0, out _terminal, 10, 3), "terminal_new");
            ReadOnlySpan<byte> input = "\u001b[1;31;44mA"u8;
            fixed (byte* pointer = input) GhosttyVtNative.TerminalVtWrite(_terminal, pointer, (nuint)input.Length);
            Check(GhosttyVtNative.RenderStateNew(0, out _state), "state_new");
            Check(GhosttyVtNative.RenderStateUpdate(_state, _terminal), "update");
            Check(GhosttyVtNative.RenderStateRowIteratorNew(0, out _rows), "rows_new");
            Check(GhosttyVtNative.RenderStateRowCellsNew(0, out _cells), "cells_new");
            nint rows = _rows;
            Check(GhosttyVtNative.RenderStateGet(_state, GhosttyVtNative.GhosttyRenderStateData.RowIterator, &rows), "rows");
            GhosttyVtNative.RenderStateRowIteratorNext(_rows);
            nint cells = _cells;
            Check(GhosttyVtNative.RenderStateRowGet(_rows, GhosttyVtNative.GhosttyRenderStateRowData.Cells, &cells), "cells");
            GhosttyVtNative.RenderStateRowCellsNext(_cells);
        }

        internal uint Read()
        {
            GhosttyVtNative.GhosttyStyle style = GhosttyVtNative.GhosttyStyle.CreateSized();
            Check(GhosttyVtNative.RenderStateRowCellsGet(_cells, GhosttyVtNative.GhosttyRenderStateRowCellsData.Style, &style),
                "ghostty_render_state_row_cells_get(style)");
            uint length = GetValue<uint>(GhosttyVtNative.GhosttyRenderStateRowCellsData.GraphemesLength);
            GhosttyVtNative.GhosttyColorRgb foreground = GetValue<GhosttyVtNative.GhosttyColorRgb>(GhosttyVtNative.GhosttyRenderStateRowCellsData.ForegroundColor);
            GhosttyVtNative.GhosttyColorRgb background = GetValue<GhosttyVtNative.GhosttyColorRgb>(GhosttyVtNative.GhosttyRenderStateRowCellsData.BackgroundColor);
            return length + foreground.R + background.B + (style.Bold ? 1u : 0u);
        }

        private T GetValue<T>(GhosttyVtNative.GhosttyRenderStateRowCellsData data) where T : unmanaged
        {
            T value = default;
            Check(GhosttyVtNative.RenderStateRowCellsGet(_cells, data, &value), $"ghostty_render_state_row_cells_get({data})");
            return value;
        }

        public void Dispose()
        {
            GhosttyVtNative.RenderStateRowCellsFree(_cells);
            GhosttyVtNative.RenderStateRowIteratorFree(_rows);
            GhosttyVtNative.RenderStateFree(_state);
            GhosttyVtNative.TerminalFree(_terminal);
        }

        private static void Check(GhosttyVtNative.GhosttyResult result, string operation)
        {
            if (result != GhosttyVtNative.GhosttyResult.Success)
            {
                throw new InvalidOperationException($"{operation} failed with {result}.");
            }
        }
    }
}
