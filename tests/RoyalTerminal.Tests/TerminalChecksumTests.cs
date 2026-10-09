// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalChecksumTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReportsRequireOptInAndDefaultsSurviveResets(bool native)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) return;
        using IVtProcessor processor = Create(native);
        ITerminalChecksumPolicy policy = (ITerminalChecksumPolicy)processor;
        List<string> replies = [];
        processor.ResponseCallback = bytes => replies.Add(Encoding.ASCII.GetString(bytes));
        Assert.False(policy.ChecksumReportsEnabled);
        Assert.Equal(TerminalChecksumFlags.None, policy.DefaultChecksumFlags);
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.DefaultChecksumFlags = (TerminalChecksumFlags)32);
        processor.Process("hello\u001b[1#y\u001b[3;1;1;1;1;5*y"u8);
        Assert.Empty(replies);
        policy.ChecksumReportsEnabled = true;
        processor.Process("\u001b[3;1;1;1;1;5*y"u8);
        Assert.Equal("\u001bP3!~FDEC\u001b\\", Assert.Single(replies));
        replies.Clear();
        policy.DefaultChecksumFlags = TerminalChecksumFlags.Positive;
        foreach (string reset in new[] { "\u001b[!p", "\u001bc" })
        {
            processor.Process(Encoding.ASCII.GetBytes("\u001b[0#y" + reset + "\u001b[Hhello\u001b[3;1;1;1;1;5*y"));
            Assert.Equal("\u001bP3!~0214\u001b\\", Assert.Single(replies));
            replies.Clear();
        }
        foreach (bool history in new[] { false, true })
        {
            ((ITerminalSessionHistoryController)processor).PrepareForNewSession(history);
            Assert.True(policy.ChecksumReportsEnabled);
            Assert.Equal(TerminalChecksumFlags.Positive, policy.DefaultChecksumFlags);
        }
        processor.Process("\u001b[1;2#y\u001b[1;2;3;4;5;6;7*y\u001b[?1*y\u001b[1:2*y"u8);
        Assert.Empty(replies);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData(" \u001b[3G \u001b[2;2H ")]
    [InlineData("éÿ界e\u0301❤\ufe0f")]
    [InlineData("\u001b[1;2;3;4;5;7;8;9m A \u001b[0m \u001b[4:2m \u001b[1\"qB")]
    [InlineData("\u001b(0qj\u001b(B\u001b[31m   \u001b[53m ")]
    [InlineData("\u001b[?69h\u001b[3;7s\u001b[2;3r\u001b[?6hA B")]
    [InlineData("\u001b[?2026hhidden")]
    public void EveryFlagAndRectangleMatchesNative(string content)
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        using GhosttyVtProcessor native = new(new TerminalScreen(8, 4));
        using BasicVtProcessor managed = new(new TerminalScreen(8, 4));
        native.ChecksumReportsEnabled = managed.ChecksumReportsEnabled = true;
        List<string> expected = [], actual = [];
        native.ResponseCallback = bytes => expected.Add(Encoding.ASCII.GetString(bytes));
        managed.ResponseCallback = bytes => actual.Add(Encoding.ASCII.GetString(bytes));
        Write(content);
        for (int flags = 0; flags <= 63; flags++)
        {
            Write($"\u001b[{flags}#y");
            foreach (string area in new[] { "", "1;1;1;1;1;5", "65535;0;0;0;0;0", "2;8;3;7;1;2", "7;0;65535;65535;65535;65535", "5;1;1;2;3;7" })
            {
                Write("\u001b[" + area + "*y");
                Assert.True(expected.Count == 1 && actual.Count == 1 && expected[0] == actual[0],
                    $"Flags {flags}, area {area}: native {string.Join(',', expected)}, managed {string.Join(',', actual)}");
                expected.Clear(); actual.Clear();
            }
        }
        void Write(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            native.Process(bytes); managed.Process(bytes);
        }
    }

    private static IVtProcessor Create(bool native)
        => native ? new GhosttyVtProcessor(new TerminalScreen(8, 4)) : new BasicVtProcessor(new TerminalScreen(8, 4));
}
