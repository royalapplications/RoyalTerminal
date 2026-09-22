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
    public void StreamingSnapshotAndContinuationApisRoundTrip()
    {
        using GhosttyTerminal terminal = new(12, 3);
        terminal.SetContinuationMaxBytes(1024);
        terminal.Write("stream snapshot\u001b[31"u8);

        using MemoryStream continuation = new();
        terminal.WriteContinuationTo(continuation);
        Assert.Equal(terminal.GetContinuation(), continuation.ToArray());

        using MemoryStream encoded = new();
        GhosttySnapshot.WriteTo(terminal, encoded);
        Assert.NotEmpty(encoded.ToArray());

        encoded.Position = 0;
        using GhosttySnapshotDecoder decoder = new(encoded);
        decoder.SetMaxContinuationBytes(1024);
        decoder.SetRetainContinuation(true);
        using GhosttyTerminal restored = decoder.Decode();

        Assert.Equal(terminal.GetColumns(), restored.GetColumns());
        Assert.Equal(terminal.GetRows(), restored.GetRows());
        Assert.Equal(terminal.GetContinuation(), restored.GetContinuation());
    }

    [GhosttyNativeFact]
    public void StreamingCallbacksRethrowManagedIoFailures()
    {
        using GhosttyTerminal terminal = new(10, 2);
        terminal.Write("failure"u8);

        IOException writeFailure = Assert.Throws<IOException>(
            () => GhosttySnapshot.WriteTo(terminal, new FailingWriteStream()));
        Assert.Equal("managed write failure", writeFailure.Message);

        using GhosttySnapshotDecoder decoder = new(new FailingReadStream());
        IOException readFailure = Assert.Throws<IOException>(() => decoder.Decode());
        Assert.Equal("managed read failure", readFailure.Message);
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

    [GhosttyNativeFact]
    public void TerminalWrapperCoversEveryCurrentTerminalDataFamily()
    {
        using GhosttyTerminal terminal = new(4, 2);
        terminal.SetTitle("managed title");
        terminal.SetWorkingDirectory("file:///tmp/example");
        GhosttyVtNative.GhosttyColorRgb foreground = new() { R = 1, G = 2, B = 3 };
        terminal.SetForegroundColor(foreground);

        terminal.Write("1234"u8);

        Assert.Equal("managed title", terminal.GetTitle());
        Assert.Equal("file:///tmp/example", terminal.GetWorkingDirectory());
        Assert.True(terminal.GetCursorPendingWrap());
        Assert.Equal((nuint)2, terminal.GetTotalRows());
        Assert.Equal((nuint)0, terminal.GetScrollbackRows());
        Assert.True(terminal.TryGetDefaultForegroundColor(out GhosttyVtNative.GhosttyColorRgb actual));
        Assert.Equal(foreground.R, actual.R);
        Assert.Equal(foreground.G, actual.G);
        Assert.Equal(foreground.B, actual.B);
        Assert.True(terminal.GetCursorStyle().Size > 0);

        GhosttyVtNative.GhosttyColorRgb[] palette = new GhosttyVtNative.GhosttyColorRgb[256];
        terminal.GetDefaultPalette(palette);
        Assert.Contains(palette, color => color.R != 0 || color.G != 0 || color.B != 0);
    }

    [GhosttyNativeFact]
    public void FormatterLeaseKeepsBorrowedTerminalAliveUntilFormatterDisposal()
    {
        GhosttyTerminal terminal = new(10, 2);
        terminal.Write("leased"u8);
        using GhosttyFormatter formatter = new(terminal);

        terminal.Dispose();

        Assert.StartsWith("leased", formatter.FormatToString(), StringComparison.Ordinal);
    }

    [GhosttyNativeFact]
    public void IncrementalSnapshotLeaseAllowsTerminalDisposalBeforeDecoder()
    {
        using GhosttyTerminal source = new(10, 2);
        source.Write("snapshot"u8);
        byte[] snapshot = GhosttySnapshot.Encode(source);
        using GhosttySnapshotDecoder decoder = new(snapshot);
        GhosttyTerminal restored = decoder.Ready();

        restored.Dispose();

        while (decoder.Next())
        {
        }
    }

    private sealed class FailingWriteStream : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer)
            => throw new IOException("managed write failure");
    }

    private sealed class FailingReadStream : MemoryStream
    {
        public override int Read(Span<byte> buffer)
            => throw new IOException("managed read failure");
    }
}
