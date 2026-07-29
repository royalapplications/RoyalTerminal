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
        Assert.Equal(
            new TerminalDesktopNotification(string.Empty, "1a"),
            Assert.Single(notifications));
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

        Assert.Empty(writes);

        processor.Process("\u001b]52;;\u0007"u8);
        TerminalClipboardWrite clear = Assert.Single(writes);
        Assert.Equal(TerminalClipboardLocation.Standard, clear.Location);
        Assert.Empty(clear.Contents);
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
