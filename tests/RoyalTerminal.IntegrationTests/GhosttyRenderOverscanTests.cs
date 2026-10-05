// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp;
using RoyalTerminal.IntegrationTests.TestInfrastructure;
using Xunit;
using static RoyalTerminal.GhosttySharp.Native.GhosttyVtNative;

namespace RoyalTerminal.IntegrationTests;

// Requires rebuilding the b40acce58 native target, not the previous package.
public sealed class GhosttyRenderOverscanTests
{
    [GhosttyNativeFact]
    public void RequestChangesPreserveCompletedRowsUntilNextUpdate()
    {
        using GhosttyTerminal terminal = History();
        using GhosttyRenderState state = new();
        Assert.Equal((ushort)0, state.GetRequestedOverscan().Above);
        Assert.Equal((ushort)0, state.GetRequestedOverscan().Below);
        state.Update(terminal);
        state.BeginRows();
        Assert.True(state.MoveNextRow());
        GhosttyRenderStateRowId original = state.GetCurrentRowId();
        Assert.True(original.IsValid);
        Assert.Equal(0, state.GetCurrentRowViewportY());
        state.SetOverscan(new() { Above = 1, Below = 2 });
        Assert.Equal(original, state.GetCurrentRowId());
        Assert.Equal((ushort)0, state.GetCapturedOverscan().Above);
        Assert.Equal((ushort)1, state.GetRequestedOverscan().Above);
        Assert.Equal((ushort)2, state.GetRequestedOverscan().Below);
        state.BeginUpdate(terminal);
        state.EndUpdate();
        Assert.Equal((ushort)2, state.GetRows());
        Assert.Equal((ushort)1, state.GetCapturedOverscan().Above);
        Assert.Equal((ushort)0, state.GetCapturedOverscan().Below);
        Assert.Equal(new[] { -1, 0, 1 }, Rows(state).Select(row => row.Y).ToArray());
    }

    [GhosttyNativeFact]
    public void OverscanClampsAtBothHistoryEdgesAndKeepsViewportHeight()
    {
        using GhosttyTerminal terminal = History();
        using GhosttyRenderState state = new();
        state.SetOverscan(new() { Above = ushort.MaxValue, Below = ushort.MaxValue });
        foreach (int top in new[] { 0, 1, 3 })
        {
            terminal.ScrollViewport(GhosttyTerminalScrollViewport.AbsoluteRow((nuint)top));
            state.Update(terminal);
            Assert.Equal((ushort)2, state.GetRows());
            Assert.Equal((ushort)top, state.GetCapturedOverscan().Above);
            Assert.Equal((ushort)(3 - top), state.GetCapturedOverscan().Below);
            var rows = Rows(state);
            Assert.Equal(5, rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                Assert.Equal(i - top, rows[i].Y);
                Assert.Equal((uint)('a' + i), rows[i].Codepoint);
                Assert.True(rows[i].Id.IsValid);
            }
            Assert.Equal(5, rows.Select(row => row.Id).Distinct().Count());
        }
    }

    [GhosttyNativeFact]
    public void DirtyIteratorReturnsCaptureIndexWhileRowReportsSignedViewportY()
    {
        using GhosttyTerminal terminal = History();
        using GhosttyRenderState state = new();
        terminal.ScrollViewport(GhosttyTerminalScrollViewport.AbsoluteRow(1));
        state.SetOverscan(new() { Above = 1, Below = 2 });
        state.Update(terminal);
        state.BeginRows();
        int count = 0;
        while (state.MoveNextDirtyRow(out ushort index))
        {
            Assert.Equal(count, index);
            Assert.Equal(count - 1, state.GetCurrentRowViewportY());
            Assert.True(state.GetCurrentRowId().IsValid);
            count++;
        }
        Assert.Equal(5, count);
        state.Clean();
        state.BeginRows();
        Assert.False(state.MoveNextDirtyRow(out _));
        state.SetOverscan(default);
        Assert.Equal(5, Rows(state).Count);
        state.Update(terminal);
        Assert.Equal(GhosttyRenderStateDirty.Full, state.GetDirty());
        Assert.Equal(new[] { 0, 1 }, Rows(state).Select(row => row.Y).ToArray());
    }

    [GhosttyNativeFact]
    public void RowIdsFollowContentAcrossViewportAndOverscanChanges()
    {
        using GhosttyTerminal terminal = History();
        using GhosttyRenderState state = new();
        terminal.ScrollViewport(GhosttyTerminalScrollViewport.AbsoluteRow(1));
        state.SetOverscan(new() { Above = 1, Below = 2 });
        state.Update(terminal);
        Dictionary<uint, GhosttyRenderStateRowId> identities = Rows(state).ToDictionary(row => row.Codepoint, row => row.Id);
        state.Clean();
        terminal.ScrollViewport(GhosttyTerminalScrollViewport.AbsoluteRow(2));
        state.Update(terminal);
        foreach (var row in Rows(state)) Assert.Equal(identities[row.Codepoint], row.Id);
        state.SetOverscan(default);
        state.Update(terminal);
        foreach (var row in Rows(state)) Assert.Equal(identities[row.Codepoint], row.Id);
    }

    [GhosttyNativeFact]
    public void NewQueriesAndOptionsRejectDisposedState()
    {
        GhosttyRenderState state = new();
        state.Dispose();
        Assert.Throws<ObjectDisposedException>(() => state.SetOverscan(default));
        Assert.Throws<ObjectDisposedException>(() => state.GetRequestedOverscan());
        Assert.Throws<ObjectDisposedException>(() => state.GetCapturedOverscan());
        Assert.Throws<ObjectDisposedException>(() => state.GetCurrentRowViewportY());
        Assert.Throws<ObjectDisposedException>(() => state.GetCurrentRowId());
    }

    private static GhosttyTerminal History()
    {
        GhosttyTerminal terminal = new(3, 2);
        terminal.Write("a\r\nb\r\nc\r\nd\r\ne"u8);
        return terminal;
    }

    private static unsafe List<(int Y, GhosttyRenderStateRowId Id, uint Codepoint)> Rows(GhosttyRenderState state)
    {
        List<(int, GhosttyRenderStateRowId, uint)> rows = [];
        state.BeginRows();
        while (state.MoveNextRow())
        {
            state.BeginCurrentRowCells();
            Assert.True(state.MoveNextCell());
            uint codepoint = 0;
            Assert.Equal(GhosttyResult.Success, CellGet(state.GetCurrentCellRaw(), GhosttyCellData.Codepoint, &codepoint));
            rows.Add((state.GetCurrentRowViewportY(), state.GetCurrentRowId(), codepoint));
        }
        return rows;
    }
}
