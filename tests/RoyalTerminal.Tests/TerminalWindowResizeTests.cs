// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

// Follow Ghostty #14375: a host effect, zero/omitted dimensions preserve size.
// WT AdaptDispatch.WindowManipulation likewise delegates character requests to
// the host. xterm.js InputHandler.windowOptions handles reports behind options
// but does not implement CSI 8 t window mutation. Neither engine resizes itself.
public sealed class TerminalWindowResizeTests(ITestOutputHelper output)
{
    public static TheoryData<string, int, int> Accepted => new()
    {
        { "\u001b[8t", 0, 0 },
        { "\u001b[8;t", 0, 0 },
        { "\u001b[8;;t", 0, 0 },
        { "\u001b[8;;;t", 0, 0 },
        { "\u001b[8;24t", 0, 24 },
        { "\u001b[8;;80t", 80, 0 },
        { "\u001b[8;24;80t", 80, 24 },
        { "\u001b[8;24;80;t", 80, 24 },
        { "\u001b[8;1;1t", 1, 1 },
        { "\u001b[8;0;0t", 0, 0 },
        { "\u001b[8;65535;65535t", 65535, 65535 },
        { "\u001b[8;999999;999999t", 65535, 65535 },
        { "\u001b[08;024;080t", 80, 24 },
    };

    public static TheoryData<string> Ignored => new()
    {
        "\u001b[8;24;80;1t", "\u001b[8;24;80;;t", "\u001b[8:24:80t",
        "\u001b[8;24:80t", "\u001b[?8;24;80t", "\u001b[>8;24;80t",
        "\u001b[=8;24;80t", "\u001b[<8;24;80t", "\u001b[8;24;80 t",
        "\u001b[8;24;80$t", "\u001b[8;24;80\u0018t", "\u001b[8;24;80\u001at",
        "\u001b[4;240;800t", "\u001b[18t", "\u001b[t",
    };

    [Theory]
    [MemberData(nameof(Accepted))]
    public void ManagedRequestsMatchGhosttyParameterRulesAtEverySplit(string input, int columns, int rows)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(input);
        for (int split = 0; split <= bytes.Length; split++)
        {
            TerminalScreen screen = new(12, 5, 0);
            using BasicVtProcessor processor = new(screen);
            processor.Process("abc"u8);
            List<TerminalWindowResizeRequest> requests = [];
            processor.WindowResizeCallback = requests.Add;
            processor.Process(bytes.AsSpan(0, split)); processor.Process(bytes.AsSpan(split));
            Assert.Equal(new TerminalWindowResizeRequest((ushort)columns, (ushort)rows), Assert.Single(requests));
            Assert.Equal(12, screen.Columns); Assert.Equal(5, screen.ViewportRows);
            Assert.Equal(3, processor.CursorCol); Assert.Equal(0, processor.CursorRow);
            Assert.Equal('a', screen.GetViewportRow(0)[0].Codepoint);
        }
    }

    [Theory]
    [MemberData(nameof(Ignored))]
    public void MalformedPrivateAndOtherWindowSequencesDoNotRequestResize(string input)
    {
        using BasicVtProcessor processor = new(new TerminalScreen(12, 5, 0));
        int requests = 0;
        processor.WindowResizeCallback = _ => requests++;
        byte[] bytes = Encoding.ASCII.GetBytes(input);
        for (int index = 0; index < bytes.Length; index++) processor.Process(bytes.AsSpan(index, 1));
        Assert.Equal(0, requests);
    }

    [Theory]
    [MemberData(nameof(Accepted))]
    public void NativeAndManagedRequestsHaveIdenticalDimensions(string input, int columns, int rows)
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            output.WriteLine("Native resize-request differential unavailable; not counted as native validation.");
            return;
        }
        using GhosttyVtProcessor native = new(new TerminalScreen(12, 5, 0));
        using BasicVtProcessor managed = new(new TerminalScreen(12, 5, 0));
        List<TerminalWindowResizeRequest> expected = [], actual = [];
        native.WindowResizeCallback = expected.Add;
        managed.WindowResizeCallback = actual.Add;
        byte[] bytes = Encoding.ASCII.GetBytes(input);
        for (int index = 0; index < bytes.Length; index++)
        {
            native.Process(bytes.AsSpan(index, 1)); managed.Process(bytes.AsSpan(index, 1));
        }
        Assert.Equal(new TerminalWindowResizeRequest((ushort)columns, (ushort)rows), Assert.Single(expected));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CallbackIsOptInReplaceableAndSurvivesResetWithoutSnapshotReplay()
    {
        TerminalScreen screen = new(12, 5, 0);
        using BasicVtProcessor processor = new(screen);
        processor.Process("\u001b[8;40;120t"u8);
        Assert.Equal(12, screen.Columns); Assert.Equal(5, screen.ViewportRows);
        int first = 0, second = 0;
        processor.WindowResizeCallback = _ => first++;
        processor.Process("\u001b[8;40;120t"u8);
        processor.Reset();
        processor.Process("\u001b[8;40;120t"u8);
        Assert.Equal(2, first);
        processor.WindowResizeCallback = _ => second++;
        processor.Process("\u001b[8;40;120t"u8);
        using ManagedTerminalSnapshot restored = ManagedTerminalSnapshot.Restore(processor.GetBinarySnapshot());
        Assert.Null(restored.Processor.WindowResizeCallback);
        restored.Processor.WindowResizeCallback = _ => second++;
        restored.Processor.Process("X"u8);
        Assert.Equal(1, second);
        processor.WindowResizeCallback = null;
        processor.Process("\u001b[8;40;120t"u8);
        Assert.Equal(2, first); Assert.Equal(1, second);
    }

    [Fact]
    public void NativeAdapterCallbackCanBeEnabledDisabledAndRetainedAcrossReset()
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            output.WriteLine("Native resize callback lifecycle unavailable; not counted as native validation.");
            return;
        }
        using GhosttyVtProcessor native = new(new TerminalScreen(12, 5, 0));
        Assert.Null(native.WindowResizeCallback);
        native.Process("\u001b[8;40;120t"u8);
        int requests = 0;
        Action<TerminalWindowResizeRequest> callback = _ => requests++;
        native.WindowResizeCallback = callback;
        native.WindowResizeCallback = callback;
        native.Process("\u001b[8;40;120t"u8);
        native.Reset();
        native.Process("\u001b[8;40;120t"u8);
        Assert.Equal(2, requests);
        native.WindowResizeCallback = null;
        native.Process("\u001b[8;40;120t"u8);
        Assert.Equal(2, requests);
        native.WindowResizeCallback = callback;
        native.Process("\u001b[8;40;120t"u8);
        Assert.Equal(3, requests);
        native.Dispose();
        Assert.Throws<ObjectDisposedException>(() => native.WindowResizeCallback = callback);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroundC1DoesNotRequestResizeButParserStateC1Does(bool native)
    {
        if (native && !GhosttyVtProcessor.IsAvailable())
        {
            output.WriteLine("Native C1 resize differential unavailable.");
            return;
        }
        TerminalScreen screen = new(12, 5, 0);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        List<TerminalWindowResizeRequest> requests = [];
        ((ITerminalWindowResizeSource)processor).WindowResizeCallback = requests.Add;
        // Ghostty stream.next decodes ground-state high bytes as UTF-8. Raw C1
        // is an invalid scalar, not CSI; encoded C1 is ignored. C1 transitions
        // still apply when already inside the VT parser (e.g. after ESC).
        processor.Process([0x9B, (byte)'8', (byte)';', (byte)'4', (byte)';', (byte)'5', (byte)'t']);
        Assert.Empty(requests);
        processor.Process(Encoding.UTF8.GetBytes("\u009b8;4;5t"));
        Assert.Empty(requests);
        processor.Process("\u001b"u8);
        processor.Process([0x9B, (byte)'8', (byte)';', (byte)'4', (byte)';', (byte)'5', (byte)'t']);
        Assert.Equal(new TerminalWindowResizeRequest(5, 4), Assert.Single(requests));
    }
}
