// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using RoyalTerminal.Terminal.Theming;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedKittyColorTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Requests()
    {
        string[] requests = ["", "foreground=?;background=?;cursor=?", "foreground=aliceblue;foreground=?;foreground;foreground=?",
            "1=#abc;1=?;1=;1=?", "0001=#123456;0_1=?;255=rgbi:.5/0/1;255=?", "foreground= #123 ;foreground= ? ",
            "foreground=#112233;cursor=?", "cursor_text=?;selection_foreground=?;visual_bell=red;second_transparent_background=?",
            "selection_background=red;selection_background=?;foreground=?", "Foreground=red;Foreground=?;foreground=?",
            "256=red;-0=red;+0=red;0_=red;_0=red; 0=red;0 =red;0=?", "foreground=red=blue;foreground=?",
            "foreground=\t?;foreground=?", "foreground=\tred\t;foreground=?", "foreground=\t;foreground=?",
            ";;bad;foreground=not-a-color;foreground=?", "background=#abcdef;background=?;background=;background=?",
            "2=#123456789;2=?;cursor=rgb:f/ff/fff;cursor=?", "foreground=RGB:ff/00/00;foreground=0xff0000;foreground=?"];
        foreach (string request in requests)
        {
            yield return ["\u001b]21;" + request + "\u001b\\"];
            yield return ["\u001b]21;" + request + "\a"];
        }
        yield return ["\u001b]21;1=red;1=?\u009c"];
        yield return ["\u001b]21;1=red\u0018\u001b]21;1=?\u001b\\"];
    }

    [Theory]
    [MemberData(nameof(Requests))]
    public void RepliesAndAllColorStateMatchNativeAtEveryInputSplit(string input)
    {
        if (!Available()) return;
        Compare(input, everySplit: true);
    }

    [Theory]
    [InlineData(525, "")]
    [InlineData(526, "")]
    [InlineData(526, ";")]
    [InlineData(526, ";invalid")]
    [InlineData(527, "")]
    public void RequestLimitIsAtomicAndCountsUnsupportedButValidKeys(int count, string suffix)
    {
        if (!Available()) return;
        string request = "foreground=red;" + string.Join(';', Enumerable.Repeat("cursor_text=?", count - 1)) + suffix;
        Compare("\u001b]21;" + request + "\a\u001b]21;foreground=?\a", everySplit: false);
    }

    [Fact]
    public void InvalidRequestsDoNotConsumeTheAcceptedRequestLimit()
    {
        if (!Available()) return;
        Compare("\u001b]21;" + string.Concat(Enumerable.Repeat("invalid=?;", 1000)) + "foreground=red;foreground=?\a", false);
    }

    [Fact]
    public void SharedParserIsUsedByExistingOscColorCommands()
    {
        if (!Available()) return;
        Compare("\u001b]4;1;aliceblue;255;rgbi:.5/0/1\u001b\\\u001b]10;#123456789\u001b\\" +
            "\u001b]11;rgb:1/22/333\u001b\\\u001b]12;DarkSlateGray\u001b\\\u001b]21;foreground=?;background=?;cursor=?;1=?;255=?\a", true);
    }

    [Fact]
    public void HeldColorChangesRecolorOnlyOnPublicationAndPreserveTruecolor()
    {
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor managed = new(screen);
        managed.Process("A\u001b[31mB\u001b[38;2;1;2;3mC\u001b[?2026h\u001b]21;foreground=red;1=blue\a"u8);
        Assert.Equal(TerminalTheme.Dark.DefaultForeground, screen.DefaultForeground);
        managed.Process("\u001b[?2026l"u8);
        Assert.Equal(0xFFFF0000u, screen.GetViewportRow(0)[0].Foreground);
        Assert.Equal(0xFF0000FFu, screen.GetViewportRow(0)[1].Foreground);
        Assert.Equal(0xFF010203u, screen.GetViewportRow(0)[2].Foreground);
    }

    private bool Available()
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native Kitty color differential available: {available}");
        return available;
    }

    private static void Compare(string input, bool everySplit)
    {
        byte[] bytes = Encoding.Latin1.GetBytes(input);
        using GhosttyTerminal native = new(8, 2);
        GhosttySnapshotTerminalState initial = Read(native);
        StringBuilder expected = new();
        GhosttyVtNative.GhosttyTerminalWritePtyCallback callback = (_, _, data, length) =>
        {
            byte[] response = new byte[checked((int)length)];
            Marshal.Copy(data, response, 0, response.Length);
            expected.Append(Encoding.ASCII.GetString(response));
        };
        native.SetWritePtyCallback(Marshal.GetFunctionPointerForDelegate(callback));
        try
        {
            native.Write(bytes);
            GhosttySnapshotTerminalState state = Read(native);
            for (int split = 0; split <= bytes.Length; split += everySplit ? 1 : Math.Max(1, bytes.Length / 2))
            {
                using BasicVtProcessor managed = new(new TerminalScreen(8, 2));
                managed.InstallSnapshotColors(initial, TerminalTheme.Dark);
                StringBuilder actual = new();
                managed.ResponseCallback = data => actual.Append(Encoding.ASCII.GetString(data));
                managed.Process(bytes.AsSpan(0, split)); managed.Process(bytes.AsSpan(split));
                Assert.Equal(expected.ToString(), actual.ToString());
                Assert.Equal(state.Header.Foreground, managed.SnapshotColors.GetSnapshotDynamic(10));
                Assert.Equal(state.Header.Background, managed.SnapshotColors.GetSnapshotDynamic(11));
                Assert.Equal(state.Header.CursorColor, managed.SnapshotColors.GetSnapshotDynamic(12));
                for (int i = 0; i < 256; i++)
                {
                    Assert.Equal(state.CurrentPaletteColor(i), managed.SnapshotColors.GetPalette(i) & 0xFFFFFF);
                    Assert.Equal(state.HasPaletteOverride(i), managed.SnapshotColors.HasPaletteOverride(i));
                }
            }
        }
        finally { native.SetWritePtyCallback(0); GC.KeepAlive(callback); }
    }

    private static GhosttySnapshotTerminalState Read(GhosttyTerminal terminal)
    {
        using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(terminal), new());
        return reader.ReadReady().Terminal;
    }
}
