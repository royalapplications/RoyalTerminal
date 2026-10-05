// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.IntegrationTests.TestInfrastructure;
using Xunit;

namespace RoyalTerminal.IntegrationTests;

public class GhosttyRenderStateExtendedTests
{
    [GhosttyNativeFact]
    public void OverwritingEitherHalfOfWrappedWideGlyphDirtiesPreviousSpacerRow()
    {
        foreach (int column in new[] { 1, 2 })
        {
            using GhosttyTerminal terminal = new(5, 3);
            using GhosttyRenderState renderState = new();
            terminal.Write("ABCD界"u8);
            renderState.Update(terminal);
            renderState.Clean();
            terminal.Write(Encoding.ASCII.GetBytes($"\u001b[2;{column}HX"));
            renderState.Update(terminal);
            renderState.BeginRows();
            Assert.True(renderState.MoveNextDirtyRow(out ushort row));
            Assert.Equal((ushort)0, row);
            Assert.True(renderState.MoveNextDirtyRow(out row));
            Assert.Equal((ushort)1, row);
            Assert.False(renderState.MoveNextDirtyRow(out _));
        }
    }

    [GhosttyNativeFact]
    public void ModeOffCombiningSuffixDirtiesAndRefreshesACleanRenderSnapshot()
    {
        using GhosttyTerminal terminal = new(10, 3);
        using GhosttyRenderState renderState = new();
        terminal.Write("\u001b[?2027lK"u8);
        renderState.Update(terminal);
        renderState.Clean();

        // The pinned upstream width-zero path appended the suffix to its grid
        // without marking the row dirty. Our hash-guarded overlay fixes this
        // at the mutation, preserving incremental rendering across PTY chunks.
        terminal.Write("\u0301"u8);
        renderState.Update(terminal);

        Assert.NotEqual(GhosttyVtNative.GhosttyRenderStateDirty.False, renderState.GetDirty());
        renderState.BeginRows();
        Assert.True(renderState.MoveNextDirtyRow(out ushort row));
        Assert.Equal((ushort)0, row);
        renderState.BeginCurrentRowCells();
        Assert.True(renderState.MoveNextCell());
        renderState.GetCurrentCellMetadata(out _, out uint graphemeLength);
        Assert.Equal(2u, graphemeLength);
        Assert.Equal("K\u0301", Encoding.UTF8.GetString(renderState.CopyCurrentCellGraphemeUtf8()));
    }

    [GhosttyNativeFact]
    public void BatchedCellMetadataMatchesIndividualQueries()
    {
        using GhosttyTerminal terminal = new(10, 3);
        using GhosttyRenderState renderState = new();
        terminal.Write(Encoding.UTF8.GetBytes("\u001b[1;38;2;1;2;3mAe\u0301界"));
        renderState.Update(terminal);
        renderState.BeginRows();
        Assert.True(renderState.MoveNextRow());
        renderState.BeginCurrentRowCells();
        while (renderState.MoveNextCell())
        {
            renderState.GetCurrentCellMetadata(out GhosttyVtNative.GhosttyStyle style, out uint graphemeLength);

            Assert.Equal(renderState.GetCurrentCellStyle(), style);
            Assert.Equal(renderState.GetCurrentCellGraphemeLength(), graphemeLength);
        }
    }

    [GhosttyNativeFact]
    public void SteadyStateCellAndTerminalReadsDoNotAllocateErrorMessages()
    {
        using GhosttyTerminal terminal = new(10, 3);
        using GhosttyRenderState renderState = new();
        terminal.Write("\u001b[31;44mA"u8);
        renderState.Update(terminal);
        renderState.BeginRows();
        Assert.True(renderState.MoveNextRow());
        renderState.BeginCurrentRowCells();
        Assert.True(renderState.MoveNextCell());
        ReadMetadata(terminal, renderState);
        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 1000; i++)
        {
            ReadMetadata(terminal, renderState);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static void ReadMetadata(GhosttyTerminal terminal, GhosttyRenderState state)
    {
        state.GetCurrentCellMetadata(out _, out _);
        _ = state.GetCurrentCellGraphemeLength();
        _ = state.GetCurrentCellSelected();
        _ = state.GetCurrentCellHasStyling();
        _ = state.TryGetCurrentCellForegroundColor(out _);
        _ = state.TryGetCurrentCellBackgroundColor(out _);
        _ = state.GetCurrentRowWrap();
        _ = state.GetCurrentRowDirty();
        _ = state.GetDirty();
        _ = terminal.GetRows();
        _ = terminal.GetCursorX();
        _ = terminal.TryGetDefaultForegroundColor(out _);
    }

    [GhosttyNativeFact]
    public void TwoPhaseUpdateExposesSelectionStylingAndUtf8()
    {
        using GhosttyTerminal terminal = new(10, 3);
        using GhosttyRenderState renderState = new();

        terminal.Write(Encoding.UTF8.GetBytes("\u001b[31mAe\u0301B"));
        Assert.True(
            terminal.TryGetGridReference(
                GhosttyVtNative.GhosttyPoint.Active(0, 0),
                out GhosttyVtNative.GhosttyGridRef start));
        Assert.True(
            terminal.TryGetGridReference(
                GhosttyVtNative.GhosttyPoint.Active(1, 0),
                out GhosttyVtNative.GhosttyGridRef end));
        terminal.SetSelection(new RoyalTerminal.GhosttySharp.GhosttySelection(start, end));

        renderState.BeginUpdate(terminal);
        renderState.EndUpdate();
        renderState.BeginRows();
        Assert.True(renderState.MoveNextRow());
        Assert.True(renderState.TryGetCurrentRowSelection(out ushort startX, out ushort endX));
        Assert.Equal((ushort)0, startX);
        Assert.Equal((ushort)1, endX);

        renderState.BeginCurrentRowCells();
        Assert.True(renderState.MoveNextCell());
        Assert.True(renderState.GetCurrentCellSelected());
        Assert.True(renderState.GetCurrentCellHasStyling());

        Assert.True(renderState.MoveNextCell());
        Assert.True(renderState.GetCurrentCellSelected());
        Assert.Equal(
            "e\u0301",
            Encoding.UTF8.GetString(renderState.CopyCurrentCellGraphemeUtf8()));

        Assert.True(renderState.MoveNextCell());
        Assert.False(renderState.GetCurrentCellSelected());
    }
}
