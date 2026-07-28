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
    public void BasicVtProcessor_ExposesManagedCodepointAndGraphemeWidths()
    {
        using BasicVtProcessor processor =
            new(new TerminalScreen(columns: 80, viewportRows: 24, scrollbackLimit: 100));

        Assert.Equal((byte)1, processor.GetCodepointWidth((uint)'A'));
        Assert.Equal((byte)0, processor.GetCodepointWidth(0x0301));
        Assert.Equal((byte)2, processor.GetCodepointWidth(0x4E00));
        Assert.Equal((byte)1, processor.GetCodepointWidth(0x11_0000));

        Assert.Equal((nuint)0, processor.GetGraphemeWidth([], out byte emptyWidth));
        Assert.Equal((byte)0, emptyWidth);

        uint[] family = [0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467, (uint)'A'];
        Assert.Equal((nuint)5, processor.GetGraphemeWidth(family, out byte width));
        Assert.Equal((byte)2, width);

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
