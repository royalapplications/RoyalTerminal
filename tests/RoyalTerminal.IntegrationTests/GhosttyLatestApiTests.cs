// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.IntegrationTests.TestInfrastructure;
using Xunit;

namespace RoyalTerminal.IntegrationTests;

public class GhosttyLatestApiTests
{
    [GhosttyNativeFact]
    public void SearchTracksUnicodeAndWrappedMatches()
    {
        using GhosttyTerminal terminal = new(6, 3);
        terminal.Write(Encoding.UTF8.GetBytes("zero\r\nAbCdEFgh\r\nabc"));
        using GhosttySearch search = new(terminal);

        search.SetNeedle("aBc");
        search.SetSelectionScroll(GhosttyVtNative.GhosttySearchScroll.None);
        search.Run();

        Assert.Equal(GhosttyVtNative.GhosttySearchStatus.Complete, search.GetStatus());
        Assert.Equal("aBc", search.GetNeedle());
        Assert.Equal(GhosttyVtNative.GhosttySearchScroll.None, search.GetSelectionScroll());
        Assert.Equal((nuint)2, search.GetTotalMatches());
        Assert.Equal(2, search.GetMatches().Length);
        Assert.True(search.SelectNext());
        Assert.NotNull(search.GetSelectedIndex());
        Assert.NotNull(search.GetSelectedMatch());
    }

    [GhosttyNativeFact]
    public void SnapshotRoundTripsScreenAndContinuation()
    {
        using GhosttyTerminal terminal = new(12, 3);
        terminal.SetContinuationMaxBytes(1024);
        terminal.Write(Encoding.UTF8.GetBytes("hello\r\nworld\u001b[31"));
        Assert.False(terminal.GetVtGround());
        Assert.NotEmpty(terminal.GetContinuation());

        byte[] snapshot = GhosttySnapshot.Encode(terminal);
        Assert.NotEmpty(snapshot);

        using GhosttySnapshotDecoder decoder = new(snapshot);
        decoder.SetMaxContinuationBytes(1024);
        decoder.SetRetainContinuation(true);
        Assert.Equal((nuint)1024, decoder.GetMaxContinuationBytes());
        Assert.True(decoder.GetRetainContinuation());
        using GhosttyTerminal restored = decoder.Decode();
        Assert.Equal(terminal.GetColumns(), restored.GetColumns());
        Assert.Equal(terminal.GetRows(), restored.GetRows());
        Assert.False(restored.GetVtGround());
        Assert.Equal(terminal.GetContinuation(), restored.GetContinuation());

        Assert.True(restored.WriteUntilGround(Encoding.ASCII.GetBytes("mred"), out nuint consumed));
        Assert.Equal((nuint)1, consumed);
        Assert.True(restored.GetVtGround());
    }

    [GhosttyNativeFact]
    public void RenderStateExposesCursorRawCellsAndDirtyRows()
    {
        using GhosttyTerminal terminal = new(10, 3);
        using GhosttyRenderState renderState = new();
        terminal.Write(Encoding.UTF8.GetBytes("hello"));
        renderState.Update(terminal);

        GhosttyVtNative.GhosttyRenderStateCursor cursor = renderState.GetCursor();
        Assert.True(cursor.ViewportHasValue);
        Assert.Equal((ushort)5, cursor.ViewportX);

        renderState.BeginRows();
        Assert.True(renderState.MoveNextDirtyRow(out ushort row));
        Assert.Equal((ushort)0, row);
        Assert.Equal(10, renderState.GetCurrentRowRawCells().Length);

        renderState.Clean();
        Assert.Equal(GhosttyVtNative.GhosttyRenderStateDirty.False, renderState.GetDirty());
        renderState.BeginRows();
        Assert.False(renderState.MoveNextDirtyRow(out _));
    }

    [GhosttyNativeFact]
    public void FormatterStreamsWithoutAnIntermediateNativeAllocation()
    {
        using GhosttyTerminal terminal = new(10, 3);
        terminal.Write("streamed"u8);
        using GhosttyFormatter formatter = new(terminal, GhosttyVtNative.GhosttyFormatterFormat.Plain);
        using MemoryStream destination = new();

        formatter.WriteTo(destination);

        Assert.StartsWith("streamed", Encoding.UTF8.GetString(destination.ToArray()), StringComparison.Ordinal);
    }
}
