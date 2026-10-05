// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Theming;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedLegacyColorTests(ITestOutputHelper output)
{
    private const string Seed = "\u001b]21;foreground=#112233;background=#445566;cursor=#778899;0=red;1=green;2=blue\a";
    private const string Query = "\u001b]10;?;?;?\a\u001b]4;0;?;1;?;2;?\u001b\\";

    public static IEnumerable<object[]> Operations()
    {
        string[] operations = ["4;1;red;bad;blue;2;yellow", "4;1;red;2;invalid;0;yellow", "4;;1;;red;;;2;blue;",
            "4;1;?;1;red;1;?", "4;+0;red;-0;?;0_1;blue;01;?", "4; 1;red;2;blue", "4;1; ?;2;red",
            "4;256;red;1;red", "4;260;?;1;red", "4;261;red;1;red", "4;511;red;1;red", "4;65536;red;1;red",
            "4;256;invalid;1;red", "4;1", "4", "4;1;rgb:f/0/0;1;?", "5;0;red;1;blue", "5;0;?", "5",
            "10;red;blue;green", "10;;;red;;blue;;green;", "10;red;invalid;green", "10;?;?;?;?;?",
            "10; ?;red;blue", "10;red;?;?", "10;red;;?;?", "10", "11;blue;red", "12;red;blue", "13;red;?",
            "19;red", "010;red", "+10;red", "04;1;red", "104", "104;", "104;;;;", "104;invalid",
            "104;261", "104;511", "104;256", "104;260", "104;invalid;256", "104;bad;1;bad", "104;+0;-0;0_1",
            "104; 1", "104;1 ", "104;999999999999999999", "104;65536;2", "105;1", "110", "110;", "110;;;;",
            "110;0", "110; ", "110;?", "111;invalid", "111", "112;ignored", "112;;;", "113", "119;"];
        foreach (string operation in operations)
        {
            yield return [operation, "\a"];
            yield return [operation, "\u001b\\"];
        }
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public void BatchingResetRulesAndRepliesMatchNativeAtEverySplit(string operation, string terminator)
    {
        if (!Available()) return;
        ManagedKittyColorTests.Compare(Seed + "\u001b]" + operation + terminator + Query, everySplit: true);
    }

    [Theory]
    [InlineData(4, 2048)]
    [InlineData(4, 2049)]
    [InlineData(10, 2048)]
    [InlineData(10, 2049)]
    [InlineData(104, 2048)]
    [InlineData(104, 2049)]
    [InlineData(110, 2048)]
    [InlineData(110, 2049)]
    public void FixedCaptureLimitsApplyBeforeAnyMutation(int operation, int length)
    {
        if (!Available()) return;
        string payload = operation switch
        {
            4 => "1;red" + new string(' ', length - 5),
            10 => "red" + new string(' ', length - 3),
            _ => new string(';', length),
        };
        ManagedKittyColorTests.Compare(Seed + $"\u001b]{operation};" + payload + "\a" + Query, everySplit: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeAndManagedAdaptersHonorEightBitAndSixteenBitReports(bool eightBit)
    {
        if (!Available()) return;
        TerminalTheme theme = TerminalTheme.Dark.WithOscColorReportFormat(eightBit ? TerminalOscColorReportFormat.Bit8 : TerminalOscColorReportFormat.Bit16);
        using GhosttyVtProcessor native = new(new TerminalScreen(8, 2));
        using BasicVtProcessor managed = new(new TerminalScreen(8, 2));
        native.ApplyTheme(theme); managed.ApplyTheme(theme);
        List<byte[]> expected = [], actual = [];
        native.ResponseCallback = expected.Add; managed.ResponseCallback = actual.Add;
        byte[] input = Encoding.ASCII.GetBytes(Seed + Query + "\u001b]21;foreground=?;1=?\a\u001b[6n");
        native.Process(input); managed.Process(input);
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++) Assert.Equal(expected[i], actual[i]);
        Assert.Contains(eightBit ? "rgb:11/22/33" : "rgb:1111/2222/3333", Encoding.ASCII.GetString(actual[0]));
        // The host format does not change Kitty reports or non-color replies.
        Assert.Contains("rgb:11/22/33", Encoding.ASCII.GetString(actual[2]));
        Assert.Equal("\u001b[1;1R", Encoding.ASCII.GetString(actual[3]));
    }

    [Fact]
    public void HeldBatchedUpdatesPreservePublishedColorsUntilRelease()
    {
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor managed = new(screen);
        managed.Process("A\u001b[31mB\u001b[?2026h\u001b]10;red;blue;green\a\u001b]4;1;yellow\a"u8);
        Assert.Equal(TerminalTheme.Dark.DefaultForeground, screen.DefaultForeground);
        managed.Process("\u001b[?2026l"u8);
        Assert.Equal(0xFFFF0000u, screen.GetViewportRow(0)[0].Foreground);
        Assert.Equal(0xFFFFFF00u, screen.GetViewportRow(0)[1].Foreground);
        Assert.Equal(0xFF0000FFu, screen.DefaultBackground);
        Assert.Equal(0xFF00FF00u, screen.Theme.CursorColor);
    }

    private bool Available()
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native legacy color differential available: {available}");
        return available;
    }
}
