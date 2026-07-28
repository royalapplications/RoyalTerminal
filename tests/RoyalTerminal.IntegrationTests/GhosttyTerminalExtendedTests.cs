// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.IntegrationTests.TestInfrastructure;
using Xunit;

namespace RoyalTerminal.IntegrationTests;

public class GhosttyTerminalExtendedTests
{
    [GhosttyNativeFact]
    public void LimitsAbsoluteViewportAndCompressionApis_Work()
    {
        using GhosttyTerminal terminal = new(10, 3, maxScrollback: 4096);
        terminal.SetScrollbackMaxLines(20);
        terminal.SetDefaultCursorStyle(GhosttyVtNative.GhosttyTerminalCursorStyle.Underline);
        terminal.SetDefaultCursorBlink(blink: true);
        terminal.SetGlyphProtocol(enabled: true);

        Assert.True(terminal.TryGetScrollbackMaxBytes(out nuint maxBytes));
        Assert.Equal((nuint)4096, maxBytes);
        Assert.True(terminal.TryGetScrollbackMaxLines(out nuint maxLines));
        Assert.Equal((nuint)20, maxLines);
        Assert.True(terminal.GetViewportActive());
        Assert.False(terminal.GetVtProcessingError());

        for (int index = 0; index < 12; index++)
        {
            terminal.Write(Encoding.UTF8.GetBytes($"line-{index}\r\n"));
        }

        terminal.ScrollViewport(GhosttyVtNative.GhosttyTerminalScrollViewport.AbsoluteRow(0));
        Assert.Equal(0ul, terminal.GetScrollbar().Offset);
        Assert.False(terminal.GetViewportActive());

        ulong activityBefore = terminal.GetCompressionActivity();
        GhosttyVtNative.GhosttyTerminalCompressionResult result =
            terminal.Compress(GhosttyVtNative.GhosttyTerminalCompressionMode.Incremental);
        Assert.True(Enum.IsDefined(result));
        Assert.True(terminal.GetCompressionActivity() >= activityBefore);
    }
}
