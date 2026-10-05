// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public class TerminalEffectAndUnicodeTests
{
    [Fact]
    public void BasicVtProcessor_IgnoresUtf8EncodedC1ControlsInGroundState()
    {
        TerminalScreen screen = new(columns: 8, viewportRows: 2, scrollbackLimit: 10);
        using BasicVtProcessor processor = new(screen);

        processor.Process([0xC2, 0x9B, (byte)'X']);

        Assert.Equal('X', screen.GetViewportRow(0)[0].Codepoint);
        Assert.Equal(0, screen.GetViewportRow(0)[1].Codepoint);
    }

    [Fact]
    public void BasicVtProcessor_NormalizesClipboardNotificationProgressAndDirectoryEffects()
    {
        using BasicVtProcessor processor =
            new(new TerminalScreen(columns: 80, viewportRows: 24, scrollbackLimit: 100));

        TerminalClipboardWrite? clipboard = null;
        TerminalDesktopNotification? notification = null;
        TerminalProgressReport? progress = null;
        string? directory = null;
        processor.ClipboardWriteCallback = value =>
        {
            clipboard = value;
            return TerminalClipboardWriteResult.Success;
        };
        processor.DesktopNotificationCallback = value => notification = value;
        processor.ProgressReportCallback = value => progress = value;
        processor.WorkingDirectoryCallback = value => directory = value;

        processor.Process("\u001b]52;c;SGk=\u0007"u8);
        Assert.NotNull(clipboard);
        Assert.Equal(TerminalClipboardLocation.Standard, clipboard.Location);
        TerminalClipboardContent content = Assert.Single(clipboard.Contents);
        Assert.Equal("text/plain", content.MimeType);
        Assert.Equal("Hi", Encoding.UTF8.GetString(content.Data));

        processor.Process("\u001b]9;Build complete\u0007"u8);
        Assert.Equal(new TerminalDesktopNotification(string.Empty, "Build complete"), notification);

        processor.Process("\u001b]777;notify;Build;Complete\u0007"u8);
        Assert.Equal(new TerminalDesktopNotification("Build", "Complete"), notification);

        processor.Process("\u001b]9;4;1;42\u0007"u8);
        Assert.Equal(new TerminalProgressReport(TerminalProgressState.Set, 42), progress);

        processor.Process("\u001b]1337;CurrentDir=/tmp/royal\u0007"u8);
        Assert.Equal("/tmp/royal", directory);

        processor.Process("\u001b]7;file:///tmp/from-osc7\u0007"u8);
        Assert.Equal("file:///tmp/from-osc7", directory);

        processor.Process("\u001b]9;9;/tmp/from-osc9\u0007"u8);
        Assert.Equal("/tmp/from-osc9", directory);
    }

    [Fact]
    public void BasicVtProcessor_IgnoresRecognizedConEmuCommandsInsteadOfReportingNotifications()
    {
        using BasicVtProcessor processor =
            new(new TerminalScreen(columns: 80, viewportRows: 24, scrollbackLimit: 100));

        List<TerminalDesktopNotification> notifications = [];
        processor.DesktopNotificationCallback = notifications.Add;

        string[] recognizedCommands =
        [
            "\u001b]9;1;25\u0007",
            "\u001b]9;2;message\u0007",
            "\u001b]9;3;title\u0007",
            "\u001b]9;5\u0007",
            "\u001b]9;6;macro\u0007",
            "\u001b]9;7;process\u0007",
            "\u001b]9;8;PATH\u0007",
            "\u001b]9;10;3\u0007",
            "\u001b]9;11;comment\u0007",
            "\u001b]9;12\u0007",
        ];

        foreach (string command in recognizedCommands)
        {
            processor.Process(Encoding.UTF8.GetBytes(command));
        }

        Assert.Empty(notifications);

        processor.Process("\u001b]9;1a\u0007"u8);
        processor.Process("\u001b]9;1;\u0007"u8);
        processor.Process("\u001b]9;1;tests passed\u0007"u8);
        Assert.Equal(
            [
                new TerminalDesktopNotification(string.Empty, "1a"),
                new TerminalDesktopNotification(string.Empty, "1;"),
                new TerminalDesktopNotification(string.Empty, "1;tests passed"),
            ],
            notifications);
    }

    [Fact]
    public void BasicVtProcessor_RejectsMalformedOsc9ProgressSuffixes()
    {
        using BasicVtProcessor processor =
            new(new TerminalScreen(columns: 80, viewportRows: 24, scrollbackLimit: 100));

        List<TerminalProgressReport> progressReports = [];
        List<TerminalDesktopNotification> notifications = [];
        processor.ProgressReportCallback = progressReports.Add;
        processor.DesktopNotificationCallback = notifications.Add;

        processor.Process("\u001b]9;4;1garbage\u0007"u8);
        processor.Process("\u001b]9;4;1;42garbage\u0007"u8);
        processor.Process("\u001b]9;4;1;\u0007"u8);

        Assert.Empty(progressReports);
        Assert.Equal(
            [
                new TerminalDesktopNotification(string.Empty, "4;1garbage"),
                new TerminalDesktopNotification(string.Empty, "4;1;42garbage"),
                new TerminalDesktopNotification(string.Empty, "4;1;"),
            ],
            notifications);
    }

    [Fact]
    public void BasicVtProcessor_TreatsOsc9Command12PrefixesAsNotifications()
    {
        using BasicVtProcessor processor =
            new(new TerminalScreen(columns: 80, viewportRows: 24, scrollbackLimit: 100));

        List<TerminalDesktopNotification> notifications = [];
        processor.DesktopNotificationCallback = notifications.Add;

        processor.Process("\u001b]9;12\u0007"u8);
        Assert.Empty(notifications);

        processor.Process("\u001b]9;12 tests passed\u0007"u8);
        Assert.Equal(
            new TerminalDesktopNotification(string.Empty, "12 tests passed"),
            Assert.Single(notifications));
    }

    [Fact]
    public void BasicVtProcessor_TreatsOsc9Command5PrefixesAsNotifications()
    {
        using BasicVtProcessor processor =
            new(new TerminalScreen(columns: 80, viewportRows: 24, scrollbackLimit: 100));

        List<TerminalDesktopNotification> notifications = [];
        processor.DesktopNotificationCallback = notifications.Add;

        processor.Process("\u001b]9;5\u0007"u8);
        Assert.Empty(notifications);

        processor.Process("\u001b]9;5 tests passed\u0007"u8);
        Assert.Equal(
            new TerminalDesktopNotification(string.Empty, "5 tests passed"),
            Assert.Single(notifications));
    }

    [Fact]
    public void BasicVtProcessor_TreatsOsc9Command10SuffixesAsNotifications()
    {
        using BasicVtProcessor processor =
            new(new TerminalScreen(columns: 80, viewportRows: 24, scrollbackLimit: 100));

        List<TerminalDesktopNotification> notifications = [];
        processor.DesktopNotificationCallback = notifications.Add;

        processor.Process("\u001b]9;10;3\u0007"u8);
        Assert.Empty(notifications);

        processor.Process("\u001b]9;10;3 tests passed\u0007"u8);
        Assert.Equal(
            new TerminalDesktopNotification(string.Empty, "10;3 tests passed"),
            Assert.Single(notifications));
    }

    [Fact]
    public void BasicVtProcessor_RejectsMalformedOsc52ClipboardWrites()
    {
        using BasicVtProcessor processor =
            new(new TerminalScreen(columns: 80, viewportRows: 24, scrollbackLimit: 100));

        List<TerminalClipboardWrite> writes = [];
        processor.ClipboardWriteCallback = value =>
        {
            writes.Add(value);
            return TerminalClipboardWriteResult.Success;
        };

        processor.Process("\u001b]52;sp;SGk=\u0007"u8);
        processor.Process("\u001b]52;c;SG k=\u0007"u8);
        processor.Process("\u001b]52;c;SGk\u0007"u8);
        processor.Process("\u001b]52;c;?\u0007"u8);
        processor.Process("\u001b]52;x;SGk=\u0007"u8);

        Assert.Empty(writes);

        processor.Process("\u001b]52;;\u0007"u8);
        TerminalClipboardWrite clear = Assert.Single(writes);
        Assert.Equal(TerminalClipboardLocation.Standard, clear.Location);
        Assert.Empty(clear.Contents);
    }

    [Fact]
    public void BasicVtProcessor_AnswersOsc52ClipboardReads()
    {
        using BasicVtProcessor processor =
            new(new TerminalScreen(columns: 80, viewportRows: 24, scrollbackLimit: 100));
        List<byte[]> responses = [];
        TerminalClipboardRead? request = null;
        processor.ResponseCallback = responses.Add;
        processor.ClipboardReadCallback = value =>
        {
            request = value;
            return new TerminalClipboardReadReply(
                TerminalClipboardReadResult.Success,
                [new TerminalClipboardContent("text/plain;charset=utf-8", "Hello"u8.ToArray())]);
        };

        processor.Process("\u001b]52;c;?\u0007"u8);

        Assert.NotNull(request);
        Assert.Equal(TerminalClipboardLocation.Standard, request.Location);
        Assert.Equal("text/plain", Assert.Single(request.MimeTypes));
        Assert.Equal("\u001b]52;c;SGVsbG8=\u001b\\", Encoding.ASCII.GetString(Assert.Single(responses)));
    }

    [Fact]
    public void BasicVtProcessor_PrefersExtendedClipboardWriteReply()
    {
        using BasicVtProcessor processor =
            new(new TerminalScreen(columns: 80, viewportRows: 24, scrollbackLimit: 100));
        int legacyCalls = 0;
        TerminalClipboardWrite? request = null;
        processor.ClipboardWriteCallback = _ =>
        {
            legacyCalls++;
            return TerminalClipboardWriteResult.Success;
        };
        processor.ClipboardWriteRequestCallback = value =>
        {
            request = value;
            return new TerminalClipboardWriteReply(TerminalClipboardWriteResult.Success, Remember: true);
        };

        processor.Process("\u001b]52;c;SGk=\u0007"u8);

        Assert.NotNull(request);
        Assert.Equal(0, legacyCalls);
    }

    [Fact]
    public void BasicVtProcessor_HandlesKittyClipboardWriteTransactionsAndSessionGrants()
    {
        using BasicVtProcessor processor =
            new(new TerminalScreen(columns: 80, viewportRows: 24, scrollbackLimit: 100));
        List<byte[]> responses = [];
        List<TerminalClipboardWrite> writes = [];
        processor.ResponseCallback = responses.Add;
        processor.ClipboardWriteRequestCallback = request =>
        {
            writes.Add(request);
            return new TerminalClipboardWriteReply(TerminalClipboardWriteResult.Success, Remember: true);
        };

        WriteKittyClipboardTransaction(processor, "one");
        WriteKittyClipboardTransaction(processor, "two");

        Assert.Equal(2, writes.Count);
        Assert.Equal("app", writes[0].Name);
        Assert.False(writes[0].Granted);
        Assert.True(writes[0].CanRemember);
        Assert.True(writes[1].Granted);
        Assert.Equal("Hello", Encoding.UTF8.GetString(Assert.Single(writes[1].Contents).Data));
        Assert.Equal(
            [
                "\u001b]5522;type=write:status=DONE:id=one\u001b\\",
                "\u001b]5522;type=write:status=DONE:id=two\u001b\\",
            ],
            responses.Select(Encoding.ASCII.GetString));
    }

    [Fact]
    public void BasicVtProcessor_RejectsInvalidKittyClipboardDataAtomically()
    {
        using BasicVtProcessor processor =
            new(new TerminalScreen(columns: 80, viewportRows: 24, scrollbackLimit: 100));
        List<byte[]> responses = [];
        int writes = 0;
        processor.ResponseCallback = responses.Add;
        processor.ClipboardWriteRequestCallback = _ =>
        {
            writes++;
            return new TerminalClipboardWriteReply(TerminalClipboardWriteResult.Success);
        };

        processor.Process("\u001b]5522;type=write:id=bad\u001b\\"u8);
        processor.Process("\u001b]5522;type=wdata:mime=dGV4dC9wbGFpbg==;SG VsbG8=\u001b\\"u8);

        Assert.Equal(0, writes);
        Assert.Equal(
            "\u001b]5522;type=write:status=EINVAL:id=bad\u001b\\",
            Encoding.ASCII.GetString(Assert.Single(responses)));
    }

    [Fact]
    public void BasicVtProcessor_AnswersKittyClipboardReadsWithMimeListingAndChunks()
    {
        using BasicVtProcessor processor =
            new(new TerminalScreen(columns: 80, viewportRows: 24, scrollbackLimit: 100));
        byte[]? response = null;
        TerminalClipboardRead? request = null;
        processor.ResponseCallback = value => response = value;
        processor.ClipboardReadCallback = value =>
        {
            request = value;
            return new TerminalClipboardReadReply(
                TerminalClipboardReadResult.Success,
                [new TerminalClipboardContent("text/plain", "Hello"u8.ToArray())],
                ["text/plain", "image/png"]);
        };

        string requested = Convert.ToBase64String(Encoding.UTF8.GetBytes(". text/plain"));
        processor.Process(Encoding.ASCII.GetBytes(
            $"\u001b]5522;type=read:id=r:name=YXBw;{requested}\u0007"));

        Assert.NotNull(request);
        Assert.True(request.ListAvailableTypes);
        Assert.Equal("text/plain", Assert.Single(request.MimeTypes));
        Assert.Equal("app", request.Name);
        string encoded = Encoding.ASCII.GetString(Assert.IsType<byte[]>(response));
        Assert.StartsWith("\u001b]5522;type=read:status=OK:id=r\u0007", encoded, StringComparison.Ordinal);
        Assert.Contains(":mime=Lg==;dGV4dC9wbGFpbiBpbWFnZS9wbmcK\u0007", encoded, StringComparison.Ordinal);
        Assert.Contains(":mime=dGV4dC9wbGFpbg==;SGVsbG8=\u0007", encoded, StringComparison.Ordinal);
        Assert.EndsWith("\u001b]5522;type=read:status=DONE:id=r\u0007", encoded, StringComparison.Ordinal);
    }

    [Fact]
    public void BasicVtProcessor_UsesKittyPasteEventAndOneTimeReadGrantInMode5522()
    {
        using BasicVtProcessor processor =
            new(new TerminalScreen(columns: 80, viewportRows: 24, scrollbackLimit: 100));
        processor.Process("\u001b[?5522h"u8);

        Assert.True(processor.TryEncodePaste("paste", bracketedPaste: false, out byte[] paste));
        string pasteEvent = Encoding.ASCII.GetString(paste);
        const string PasswordMarker = ":pw=";
        int marker = pasteEvent.IndexOf(PasswordMarker, StringComparison.Ordinal);
        int end = pasteEvent.IndexOf('\u001b', marker);
        string encodedPassword = pasteEvent[(marker + PasswordMarker.Length)..end];

        byte[]? response = null;
        processor.ResponseCallback = value => response = value;
        string requested = Convert.ToBase64String("text/plain"u8);
        processor.Process(Encoding.ASCII.GetBytes(
            $"\u001b]5522;type=read:name=YXBw:pw={encodedPassword};{requested}\u001b\\"));

        string readResponse = Encoding.ASCII.GetString(Assert.IsType<byte[]>(response));
        Assert.Contains(":mime=dGV4dC9wbGFpbg==;cGFzdGU=\u001b\\", readResponse, StringComparison.Ordinal);
    }

    private static void WriteKittyClipboardTransaction(BasicVtProcessor processor, string id)
    {
        processor.Process(Encoding.ASCII.GetBytes(
            $"\u001b]5522;type=write:id={id}:name=YXBw:pw=c2VjcmV0\u001b\\"));
        processor.Process("\u001b]5522;type=wdata:mime=dGV4dC9wbGFpbg==;SGVs\u001b\\"u8);
        processor.Process("\u001b]5522;type=wdata:mime=dGV4dC9wbGFpbg==;bG8=\u001b\\"u8);
        processor.Process("\u001b]5522;type=wdata\u001b\\"u8);
    }

    [Fact]
    public void BasicVtProcessor_ReportsUnknownApcSequencesAndTruncation()
    {
        using BasicVtProcessor processor =
            new(new TerminalScreen(columns: 80, viewportRows: 24, scrollbackLimit: 100));
        List<TerminalUnknownSequence> sequences = [];
        processor.UnknownSequenceCallback = sequences.Add;

        processor.Process("\u001b_hello\u001b\\"u8);
        TerminalUnknownSequence first = Assert.Single(sequences);
        Assert.Equal(TerminalUnknownSequenceType.Apc, first.Type);
        Assert.Equal("hello", Encoding.ASCII.GetString(first.Content));
        Assert.False(first.Truncated);

        sequences.Clear();
        byte[] oversized = new byte[4_100 + 4];
        oversized[0] = 0x1B;
        oversized[1] = (byte)'_';
        oversized.AsSpan(2, 4_100).Fill((byte)'x');
        oversized[^2] = 0x1B;
        oversized[^1] = (byte)'\\';
        processor.Process(oversized);

        TerminalUnknownSequence truncated = Assert.Single(sequences);
        Assert.Equal(4_096, truncated.Content.Length);
        Assert.True(truncated.Truncated);
    }

    [Fact]
    public void BasicVtProcessor_NormalizesIterm2CopyAndCurrentDirectory()
    {
        using BasicVtProcessor processor =
            new(new TerminalScreen(columns: 80, viewportRows: 24, scrollbackLimit: 100));

        List<TerminalClipboardWrite> writes = [];
        List<string> directories = [];
        processor.ClipboardWriteCallback = value =>
        {
            writes.Add(value);
            return TerminalClipboardWriteResult.Success;
        };
        processor.WorkingDirectoryCallback = directories.Add;

        processor.Process("\u001b]1337;cOpY=:SGk=\u0007"u8);
        TerminalClipboardWrite write = Assert.Single(writes);
        Assert.Equal(TerminalClipboardLocation.Standard, write.Location);
        Assert.Equal("Hi", Encoding.UTF8.GetString(Assert.Single(write.Contents).Data));

        processor.Process("\u001b]1337;cUrReNtDiR=/tmp/royal\u0007"u8);
        Assert.Equal("/tmp/royal", Assert.Single(directories));

        processor.Process("\u001b]1337;Copy=\u0007"u8);
        processor.Process("\u001b]1337;Copy=:\u0007"u8);
        processor.Process("\u001b]1337;Copy=:?\u0007"u8);
        processor.Process("\u001b]1337;Copy=:SG k=\u0007"u8);
        processor.Process("\u001b]1337;CurrentDir=\u0007"u8);

        Assert.Single(writes);
        Assert.Single(directories);
    }

    [Fact]
    public void BasicVtProcessor_ExposesManagedCodepointAndGraphemeWidths()
    {
        using BasicVtProcessor processor =
            new(new TerminalScreen(columns: 80, viewportRows: 24, scrollbackLimit: 100));

        Assert.Equal((byte)1, processor.GetCodepointWidth((uint)'A'));
        Assert.Equal((byte)0, processor.GetCodepointWidth(0x0301));
        Assert.Equal((byte)2, processor.GetCodepointWidth(0x4E00));
        Assert.Equal((byte)0, processor.GetCodepointWidth(0xD800));
        Assert.Equal((byte)2, processor.GetCodepointWidth(0x1F1E6));
        Assert.Equal((byte)1, processor.GetCodepointWidth(0x11_0000));

        Assert.Equal((nuint)0, processor.GetGraphemeWidth([], out byte emptyWidth));
        Assert.Equal((byte)0, emptyWidth);

        uint[] family = [0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467, (uint)'A'];
        Assert.Equal((nuint)5, processor.GetGraphemeWidth(family, out byte width));
        Assert.Equal((byte)2, width);

        Assert.Equal(
            (nuint)2,
            processor.GetGraphemeWidth([(uint)'A', 0xFE0F], out byte invalidVsWidth));
        Assert.Equal((byte)1, invalidVsWidth);

        Assert.Equal(
            (nuint)2,
            processor.GetGraphemeWidth([0xD800, 0x0301], out byte surrogateWidth));
        Assert.Equal((byte)0, surrogateWidth);

        Assert.Equal(
            (nuint)1,
            processor.GetGraphemeWidth([0xD83D, 0xDE00], out byte surrogatePairWidth));
        Assert.Equal((byte)0, surrogatePairWidth);

        Assert.Equal(
            (nuint)1,
            processor.GetGraphemeWidth([0x11_0000], out byte invalidWidth));
        Assert.Equal((byte)1, invalidWidth);
    }

    [Fact]
    public void GhosttyVtProcessor_NormalizesNativeTerminalEffects()
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            return;
        }

        using GhosttyVtProcessor processor =
            new(new TerminalScreen(columns: 80, viewportRows: 24, scrollbackLimit: 100));

        Assert.Equal((byte)1, processor.GetCodepointWidth(0x11_0000));
        Assert.Equal((nuint)0, processor.GetGraphemeWidth([], out byte emptyWidth));
        Assert.Equal((byte)0, emptyWidth);
        Assert.Equal(
            (nuint)1,
            processor.GetGraphemeWidth([0x11_0000], out byte invalidWidth));
        Assert.Equal((byte)1, invalidWidth);

        TerminalClipboardWrite? clipboard = null;
        TerminalDesktopNotification? notification = null;
        TerminalProgressReport? progress = null;
        string? directory = null;
        processor.ClipboardWriteCallback = value =>
        {
            clipboard = value;
            return TerminalClipboardWriteResult.Success;
        };
        processor.DesktopNotificationCallback = value => notification = value;
        processor.ProgressReportCallback = value => progress = value;
        processor.WorkingDirectoryCallback = value => directory = value;

        processor.Process("\u001b]52;c;SGk=\u0007"u8);
        Assert.NotNull(clipboard);
        Assert.Equal(TerminalClipboardLocation.Standard, clipboard.Location);
        Assert.Equal("Hi", Encoding.UTF8.GetString(Assert.Single(clipboard.Contents).Data));

        clipboard = null;
        processor.Process("\u001b]1337;Copy=:Qnll\u0007"u8);
        Assert.NotNull(clipboard);
        Assert.Equal(TerminalClipboardLocation.Standard, clipboard.Location);
        Assert.Equal("Bye", Encoding.UTF8.GetString(Assert.Single(clipboard.Contents).Data));

        processor.Process("\u001b]9;Build complete\u0007"u8);
        Assert.Equal(new TerminalDesktopNotification(string.Empty, "Build complete"), notification);

        processor.Process("\u001b]777;notify;Build;Complete\u0007"u8);
        Assert.Equal(new TerminalDesktopNotification("Build", "Complete"), notification);

        processor.Process("\u001b]9;4;1;42\u0007"u8);
        Assert.Equal(new TerminalProgressReport(TerminalProgressState.Set, 42), progress);

        processor.Process("\u001b]1337;CurrentDir=/tmp/royal\u0007"u8);
        Assert.Equal("/tmp/royal", directory);

        processor.Process("\u001b]7;file:///tmp/from-osc7\u0007"u8);
        Assert.Equal("file:///tmp/from-osc7", directory);

        processor.Process("\u001b]9;9;/tmp/from-osc9\u0007"u8);
        Assert.Equal("/tmp/from-osc9", directory);
    }
}
