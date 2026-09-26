// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalDragDropTests
{
    // Exact conversations from Ghostty kitty/dnd_test.zig, including the captured
    // kitten 0.47 exchange. WT's unregistered path paste / xterm.js's paste helper
    // do not implement this request/response protocol; registered clients must
    // get OSC 72, not command-line paths or an unsolicited paste.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KittenConversationAndOwnedDropData(bool native)
    {
        using Harness h = new(native);
        h.Command("t=a:m=0;text/uri-list text/plain");
        h.Command("t=a:x=1:m=0;remote-machine");
        Assert.True(h.Target.IsDropRegistered);
        Assert.Equal(new[] { "text/uri-list", "text/plain" }, h.Target.RegisteredDropMimeTypes);
        Assert.Equal("\x1b]72;t=m:x=2:y=1:X=20:Y=18:o=1:m=0;text/plain \x1b\\", Text(h.Target.DragMove(Position, ["text/plain"])));
        h.Command("t=m:o=1:m=0;text/plain");
        Assert.Equal(TerminalDropOperation.Copy, h.Target.AcceptedDropOperation);
        byte[] payload = Encoding.UTF8.GetBytes("hello from ghostty\n");
        Assert.Equal("\x1b]72;t=M:x=2:y=1:X=20:Y=18:o=1:m=0;text/plain \x1b\\", Text(h.Target.Drop(Position, [new("text/plain", payload)])));
        payload.AsSpan().Fill(0); // Dropped bytes must be copied, not borrowed.
        Assert.Empty(h.Target.DragLeave());
        Assert.Equal("\x1b]72;t=r:x=1:m=0;aGVsbG8gZnJvbSBnaG9zdHR5Cg==\x1b\\\x1b]72;t=r:x=1\x1b\\", h.Command("t=r:x=1"));
        Assert.Empty(h.Command("t=r:o=1"));
        Assert.Contains("ENOENT:no drop data available", h.Command("t=r:x=1"));
        Assert.True(h.Target.IsDropRegistered);
        h.Command("t=A");
        Assert.False(h.Target.IsDropRegistered);
        Assert.Empty(h.Target.DragMove(Position, ["text/plain"]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChunkedRegistrationAcceptanceAndResetLifecycle(bool native)
    {
        using Harness h = new(native);
        h.Command("t=a:i=7:m=1;text/");
        h.Command("t=q:i=99:m=0;plain text/uri-list");
        Assert.Equal(new[] { "text/plain", "text/uri-list" }, h.Target.RegisteredDropMimeTypes);
        Assert.Contains(":i=7:", Text(h.Target.DragMove(Position, ["text/plain"])));
        h.Command("t=m:o=2:m=1;text/");
        Assert.Null(h.Target.AcceptedDropOperation);
        h.Command("t=q:o=1;moved");
        Assert.Equal(TerminalDropOperation.Move, h.Target.AcceptedDropOperation);
        h.Command("t=a:i=11;text/plain");
        Assert.Contains(":i=11:", Text(h.Target.DragMove(Position, ["text/plain"])));
        h.Command("t=m:o=1:m=1;text/");
        h.Processor.Reset();
        Assert.True(h.Target.IsDropRegistered);
        Assert.Equal("\x1b]72;t=q:i=9\x07", h.Command("t=q:i=9", bell: true));
        ((ITerminalSessionHistoryController)h.Processor).PrepareForNewSession(preserveScrollback: true);
        Assert.False(h.Target.IsDropRegistered);
        Assert.Null(h.Target.AcceptedDropOperation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewDragLeaveCancelAndUnregisterReleaseHeldData(bool native)
    {
        using Harness h = new(native);
        h.Command("t=a:i=8");
        Assert.Empty(h.Target.DragLeave());
        h.Target.Drop(Position, [new("text/plain", "first"u8.ToArray())]);
        h.Target.DragMove(Position, ["text/plain"]);
        Assert.Contains("ENOENT", h.Command("t=r:x=1"));
        Assert.Equal("\x1b]72;t=m:x=-1:y=-1:i=8\x1b\\", Text(h.Target.DragLeave()));
        Assert.Empty(h.Target.DragLeave());
        h.Target.Drop(Position, [new("text/plain", "second"u8.ToArray())]);
        h.Command("t=m:o=1:m=1;part");
        h.Target.CancelDrop(); // A host cancellation is NOT an OSC chunk continuation.
        h.Command("m=0;rest");
        Assert.Contains("ENOENT", h.Command("t=r:x=1"));
        Assert.True(h.Target.IsDropRegistered);
        h.Target.Drop(Position, [new("text/plain", "third"u8.ToArray())]);
        h.Command("t=A");
        Assert.Contains("ENOENT", h.Command("t=r:x=1"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DataChunkingEmptyCompletionAndRequestKeys(bool native)
    {
        using Harness h = new(native);
        h.Command("t=a:i=42");
        byte[] binary = new byte[6145];
        for (int i = 0; i < binary.Length; i++) binary[i] = (byte)i;
        h.Target.Drop(Position, [new("application/octet-stream", binary), new("text/plain", ReadOnlyMemory<byte>.Empty)]);
        string response = h.Command("t=r:i=999:x=1", bell: true);
        string[] chunks = response.Split('\x07', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, chunks.Length);
        Assert.StartsWith("\x1b]72;t=r:x=1:i=42:m=1;", chunks[0]);
        Assert.Equal(4096, chunks[0][(chunks[0].LastIndexOf(';') + 1)..].Length);
        Assert.StartsWith("\x1b]72;t=r:x=1:i=42:m=0;", chunks[2]);
        Assert.Equal("\x1b]72;t=r:x=1:i=42", chunks[3]);
        using MemoryStream decoded = new();
        foreach (string chunk in chunks[..3]) decoded.Write(Convert.FromBase64String(chunk[(chunk.LastIndexOf(';') + 1)..]));
        Assert.Equal(binary, decoded.ToArray());
        Assert.Equal("\x1b]72;t=r:x=2:i=42\x1b\\", h.Command("t=r:x=2"));
        Assert.Contains("t=R:x=-1:i=42:m=0;ENOENT:drop data request index out of bounds", h.Command("t=r:x=-1"));
        Assert.Contains("t=R:x=1:y=3:i=42:m=0;EINVAL:remote drop data is not supported", h.Command("t=r:x=1:y=3"));
        Assert.Contains("t=R:x=2:Y=4:i=42:m=0;EINVAL:remote drop data is not supported", h.Command("t=r:x=2:y=3:Y=4"));
        Assert.Contains("EPERM:drag out is not supported by this terminal", h.Command("t=o:i=3"));
        Assert.Contains("t=E:i=3", h.Command("t=P:i=3"));
        Assert.Empty(h.Command("t=o:x=1"));
        Assert.Empty(h.Command("t=o:x=2"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryInputSplitPreservesRegistrationAndClientId(bool native)
    {
        byte[] input = Encoding.ASCII.GetBytes("\x1b]72;t=a:i=4294967295;mimes\x1b\\");
        using Harness h = new(native);
        for (int split = 0; split <= input.Length; split++)
        {
            h.Command("t=A");
            h.Processor.Process(input.AsSpan(0, split));
            h.Processor.Process(input.AsSpan(split));
            Assert.True(h.Target.IsDropRegistered);
            Assert.Contains(":i=4294967295", Text(h.Target.DragMove(Position, [])));
        }
    }

    [Theory]
    [InlineData("t=z")]
    [InlineData("z=1")]
    [InlineData("x10")]
    [InlineData("x=-")]
    [InlineData("i=4294967296")]
    [InlineData("i=99999999999")]
    [InlineData("x=1z")]
    [InlineData("t=a: x=1")]
    [InlineData("t = a")]
    [InlineData("t")]
    [InlineData("t=")]
    [InlineData("x=")]
    [InlineData("t=r:x=")]
    [InlineData("x=1:t=")]
    public void MalformedMetadataIsRejectedAtomically(string metadata)
    {
        Assert.False(ManagedDragDropMetadata.TryParse(Encoding.ASCII.GetBytes(metadata), out _));
        using Harness h = new(false);
        h.Command("t=a");
        h.Target.Drop(Position, [new("text/plain", "retained"u8.ToArray())]);
        Assert.Empty(h.Command(metadata));
        Assert.Contains(Convert.ToBase64String("retained"u8), h.Command("t=r:x=1"));
    }

    [Fact]
    public void MetadataWrapsSignedMagnitudesAndAllowsFinalSeparator()
    {
        Assert.True(ManagedDragDropMetadata.TryParse("t=m:x=4294967295:y=-2147483648:X=-4294967295:Y=2147483648:i=1:i=7:"u8, out var value));
        Assert.Equal((-1, int.MinValue, 1, int.MinValue, 7u), (value.X, value.Y, value.PixelX, value.PixelY, value.Client));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HostLimitsAreAtomicAndDropCountMatchesAdvertisedCount(bool native)
    {
        using Harness h = new(native);
        h.Command("t=a");
        h.Target.Drop(Position, [new("text/plain", "keep"u8.ToArray())]);
        Assert.Throws<ArgumentException>(() => h.Target.Drop(Position, [new("text/plain\x1b]0;injected", "bad"u8.ToArray())]));
        Assert.Contains(Convert.ToBase64String("keep"u8), h.Command("t=r:x=1"));
        TerminalDropItem[] items = new TerminalDropItem[17];
        for (int i = 0; i < items.Length; i++) items[i] = new("application/" + i, new byte[] { (byte)i });
        string drop = Text(h.Target.Drop(Position, items));
        Assert.Contains("application/15 ", drop);
        Assert.DoesNotContain("application/16", drop);
        Assert.Contains("ENOENT", h.Command("t=r:x=17"));
    }

    [Fact]
    public void OverQuotaAcceptanceRemainsUnanswered()
    {
        using Harness h = new(false);
        h.Command("t=a");
        h.Target.DragMove(Position, ["text/plain"]);
        h.Command("t=m:o=1:m=1;" + new string('a', 1024 * 1024));
        Assert.Null(h.Target.AcceptedDropOperation);
        h.Command("m=0;x");
        Assert.Null(h.Target.AcceptedDropOperation);
        h.Target.CancelDrop();
        h.Command("t=m:o=1;text/plain");
        Assert.Equal(TerminalDropOperation.Copy, h.Target.AcceptedDropOperation);
    }

    private static TerminalDropPosition Position => new(2, 1, 20, 18, TerminalDropOperation.Copy);
    private static string Text(byte[] value) => Encoding.ASCII.GetString(value);

    private sealed class Harness : IDisposable
    {
        internal IVtProcessor Processor { get; }
        internal ITerminalDragDropTarget Target => (ITerminalDragDropTarget)Processor;
        private readonly StringBuilder _responses = new();
        internal Harness(bool native)
        {
            if (native && !GhosttyVtProcessor.IsAvailable())
            {
                Assert.NotEqual("1", Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS"));
                Assert.Skip("Native Ghostty runtime unavailable.");
            }
            TerminalScreen screen = new(20, 4, 20);
            Processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
            Processor.ResponseCallback = bytes => _responses.Append(Text(bytes));
        }
        internal string Command(string value, bool bell = false)
        {
            _responses.Clear();
            Processor.Process(Encoding.ASCII.GetBytes("\x1b]72;" + value + (bell ? "\x07" : "\x1b\\")));
            return _responses.ToString();
        }
        public void Dispose() => Processor.Dispose();
    }
}
