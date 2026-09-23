// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class GhosttySnapshotTranscodeTests(ITestOutputHelper output)
{
    [Fact]
    public void CompleteGoldenSnapshotRoundTripsThroughAllManagedCodecs()
    {
        byte[] source = GhosttySnapshotFramingTests.Fixture("complete-v1.hex");
        Assert.Equal(source, Transcode(source));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiveNativeSnapshotsWithHistoryAndContinuationSurviveCompleteManagedTranscoding(bool alternate)
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native snapshot sequence differential available: {available}");
        if (!available) return;
        using GhosttyTerminal terminal = new(12, 3);
        terminal.SetScrollbackMaxBytes(4 * 1024 * 1024);
        terminal.SetScrollbackMaxLines(200);
        terminal.SetContinuationMaxBytes(65536);
        for (int row = 0; row < 100; row++) terminal.Write(Encoding.UTF8.GetBytes($"\u001b[38;5;{row}m{row}:界a\u0301\r\n"));
        terminal.Write("\u001b]8;id=test;https://example.com\u0007\u001b7"u8);
        if (alternate) terminal.Write("\u001b[?1049h\u001b[44mALT"u8);
        terminal.Write("\u001b[31"u8);
        byte[] original = GhosttySnapshot.Encode(terminal);
        byte[] transcoded = Transcode(original);
        using GhosttyTerminal direct = GhosttySnapshot.Decode(original, retainContinuation: true);
        using GhosttyTerminal managedWire = GhosttySnapshot.Decode(transcoded, retainContinuation: true);
        Assert.Equal(GhosttySnapshot.Encode(direct), GhosttySnapshot.Encode(managedWire));
        Assert.Equal("\u001b[31"u8.ToArray(), managedWire.GetContinuation());
        direct.Write("mNEXT\u001b8!\u001b[?1049l"u8);
        managedWire.Write("mNEXT\u001b8!\u001b[?1049l"u8);
        Assert.Equal(GhosttySnapshot.Encode(direct), GhosttySnapshot.Encode(managedWire));
    }

    private static byte[] Transcode(byte[] source)
    {
        using GhosttySnapshotStateReader reader = new(source, new());
        GhosttySnapshotReadyState ready = reader.ReadReady();
        using MemoryStream result = new(); result.Write(GhosttySnapshotFraming.Envelope);
        using MemoryStream payload = new();
        ready.Terminal.WritePayloadTo(payload);
        WritePayload(result, payload, GhosttySnapshotRecordTag.Terminal);
        foreach (GhosttySnapshotScreen screen in ready.Screens)
        {
            screen.State.WritePayloadTo(payload);
            WritePayload(result, payload, GhosttySnapshotRecordTag.Screen);
            foreach (GhosttySnapshotPage page in screen.Pages)
            {
                page.WritePayloadTo(payload);
                WritePayload(result, payload, GhosttySnapshotRecordTag.Page);
            }
        }
        GhosttySnapshotFraming.WriteRecord(result, GhosttySnapshotRecordTag.Continuation, ready.Continuation);
        GhosttySnapshotFraming.WriteRecord(result, GhosttySnapshotRecordTag.Ready, []);
        int keys = 0;
        Span<byte> headerBytes = stackalloc byte[6];
        while (reader.ReadNextHistoryPage() is { } history)
        {
            if ((keys & (1 << history.Key)) == 0)
            {
                keys |= 1 << history.Key;
                new GhosttySnapshotHistoryHeader((ushort)history.Key, history.Remaining + 1).Write(headerBytes);
                GhosttySnapshotFraming.WriteRecord(result, GhosttySnapshotRecordTag.History, headerBytes);
            }
            history.Page.WritePayloadTo(payload);
            WritePayload(result, payload, GhosttySnapshotRecordTag.Page);
        }
        // Empty history groups produce no page events but remain required records.
        for (int key = 0; key < ready.Screens.Length; key++)
        {
            if ((keys & (1 << key)) != 0) continue;
            new GhosttySnapshotHistoryHeader((ushort)key, 0).Write(headerBytes);
            GhosttySnapshotFraming.WriteRecord(result, GhosttySnapshotRecordTag.History, headerBytes);
        }
        GhosttySnapshotFraming.WriteRecord(result, GhosttySnapshotRecordTag.Finish, []);
        Assert.Equal(source.Length, reader.SourceOffset);
        return result.ToArray();
    }

    private static void WritePayload(Stream result, MemoryStream payload, GhosttySnapshotRecordTag tag)
    {
        GhosttySnapshotFraming.WriteRecord(result, tag, payload.GetBuffer().AsSpan(0, (int)payload.Length));
        payload.SetLength(0);
    }
}
