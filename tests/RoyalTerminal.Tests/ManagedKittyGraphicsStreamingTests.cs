// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

/// <summary>
/// Matches Ghostty apc.zig and graphics_command.zig: fixed control-field state,
/// encoded-data-only quotas, sticky per-command failure and exit-time execution.
/// xterm.js addon-image streams separately bounded control/data but caps headers
/// at 512 characters. We follow Ghostty's constant-space header parser instead.
/// Windows Terminal's closest graphics dispatch is DCS/SIXEL, not Kitty APC.
/// </summary>
public sealed class ManagedKittyGraphicsStreamingTests
{
    [Theory]
    [InlineData("YQ", "a")]
    [InlineData("YQ==", "a")]
    [InlineData("YR==", "a")]
    [InlineData("YWI", "ab")]
    [InlineData("YWJ=", "ab")]
    [InlineData("YWJj", "abc")]
    [InlineData("YWJjZA", "abcd")]
    [InlineData("YWJjZGV", "abcde")]
    [InlineData(" Y\tW\nJ\rj\fZA == ", "abcd")]
    [InlineData(" \t\r\n\f", "")]
    public void ForgivingGraphicsBase64SupportsOptionalPaddingAndAsciiWhitespace(string payload, string expected)
    {
        byte[] bytes = Encoding.ASCII.GetBytes("i=73;" + payload);
        for (int split = 0; split <= bytes.Length; split++)
        {
            ManagedKittyGraphicsParser parser = new(payload.Length);
            Assert.True(parser.TryAppend(bytes.AsSpan(0, split)));
            Assert.True(parser.TryAppend(bytes.AsSpan(split)));
            Assert.True(parser.TryComplete(out var command));
            Assert.Equal(expected, Encoding.ASCII.GetString(command.Data.Span));
        }
    }

    [Theory]
    [InlineData("A")]
    [InlineData("A===")]
    [InlineData("YQ=")]
    [InlineData("AAA==")]
    [InlineData("====")]
    [InlineData("=YQ=")]
    [InlineData("YQ==AAAA")]
    [InlineData("YQ==AA")]
    [InlineData("____")]
    [InlineData("-w==")]
    [InlineData("AQ\vID")]
    public void ForgivingDecodingStillRejectsInvalidAlphabetPaddingAndIncompleteBytes(string payload)
        => Assert.False(ManagedKittyGraphicsCommand.TryParse(Encoding.ASCII.GetBytes(";" + payload), 128, out _));

    [Theory]
    [InlineData("a=T,i=73,p=1,s=1,v=1,z=-2147483648,H=2147483647,V=-1;/wAA/w==", true)]
    [InlineData("a=p,i=73,p=1", true)]
    [InlineData("a=p,i=73,i=74,too_long_key=ignored;", true)]
    [InlineData("a=p,i=73,z=000000000000,ignored=1;", true)]
    [InlineData("a=p,i=4294967296;", false)]
    [InlineData("a=p,i=73,", false)]
    [InlineData("a=p,i=73,b", false)]
    [InlineData("a=p,i=73;!!!!", false)]
    public void ArbitraryFeedBoundariesPreserveControlStateAndOwnedData(string text, bool valid)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        bool expectedValid = ManagedKittyGraphicsCommand.TryParse(bytes, 8, out var expected);
        Assert.Equal(valid, expectedValid);
        for (int split = 0; split <= bytes.Length; split++)
        {
            ManagedKittyGraphicsParser parser = new(8);
            bool accepted = parser.TryAppend(bytes.AsSpan(0, split));
            accepted &= parser.TryAppend(bytes.AsSpan(split));
            bool completed = parser.TryComplete(out var actual);
            Assert.Equal(valid, accepted && completed);
            if (!valid) continue;
            Assert.NotNull(actual);
            Assert.Equal(expected!.Data.ToArray(), actual.Data.ToArray());
            for (char key = 'A'; key <= 'z'; key++) Assert.Equal(expected.Get(key), actual.Get(key));
            Assert.False(parser.TryAppend("i=99;AAAA"u8));
            Assert.False(parser.TryComplete(out _));
            Assert.Equal(expected.Data.ToArray(), actual.Data.ToArray());
        }
        ManagedKittyGraphicsParser bytewise = new(8);
        for (int i = 0; i < bytes.Length; i++) bytewise.TryAppend(bytes.AsSpan(i, 1));
        Assert.Equal(valid, bytewise.TryComplete(out _));
    }

    [Fact]
    public void LongControlStreamUsesConstantStorageOutsideThePayloadQuota()
    {
        ManagedKittyGraphicsParser parser = new(0);
        Assert.True(parser.TryAppend("a=p,i=1,"u8));
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool accepted = true;
        for (int i = 0; i < 100_000; i++) accepted &= parser.TryAppend("i=73,z=-1,"u8);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(accepted);
        Assert.Equal(0, allocated);
        Assert.True(parser.TryAppend("p=1"u8));
        Assert.True(parser.TryComplete(out var command));
        Assert.Equal(73u, command.ImageId);
        Assert.Equal(-1, command.GetSigned('z'));
        Assert.Empty(command.Data.ToArray());
    }

    [Fact]
    public void CompletionDecodesInPlaceWithoutAllocatingASecondImageArray()
    {
        ManagedKittyGraphicsCommand.TryParse(";AAAA"u8, 4, out _); // Warm decoder.
        byte[] bytes = new byte[64 * 1024];
        Array.Fill(bytes, (byte)'A');
        ManagedKittyGraphicsParser parser = new(bytes.Length);
        Assert.True(parser.TryAppend("i=73;"u8));
        Assert.True(parser.TryAppend(bytes));
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool completed = parser.TryComplete(out var command);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(completed);
        Assert.NotNull(command);
        Assert.Equal(0, allocated);
        Assert.Equal(bytes.Length / 4 * 3, command.Data.Length);
        Assert.Equal(new byte[command.Data.Length], command.Data.ToArray());
        Array.Fill(bytes, (byte)'B'); // No caller input is retained or modified.
        Assert.Equal(0, command.Data.Span[0]);
    }

    [Fact]
    public void OverflowDiscardsCommandAndCannotBeRepairedByLaterInput()
    {
        ManagedKittyGraphicsParser parser = new(4);
        Assert.True(parser.TryAppend("i=73;AAAA"u8));
        Assert.False(parser.TryAppend("A"u8));
        Assert.False(parser.TryAppend("i=74;"u8));
        Assert.False(parser.TryComplete(out _));
    }

    [Fact]
    public void ExactPayloadLimitExcludesIdentifierAndHeaderAtEverySplit()
    {
        byte[] sequence = "\u001b_Ga=q,i=73,s=1,v=1,f=32;/wAA/w==\u001b\\"u8.ToArray();
        for (int split = 0; split <= sequence.Length; split++)
        {
            using Session session = new(8);
            session.Processor.Process(sequence.AsSpan(0, split));
            session.Processor.Process(sequence.AsSpan(split));
            Assert.Equal("\u001b_Gi=73;OK\u001b\\", Assert.Single(session.Replies));
            Assert.Empty(session.Unknown);
            Assert.True(session.Processor.IsParserGround);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StreamingImageAnimationAndControlCommandsMatchNativeWithTinyManagedPayloadQuota(bool bytewise)
    {
        RequireNative();
        using Session session = new(8);
        using GhosttyVtProcessor native = new(new TerminalScreen(8, 3, 10));
        List<string> expected = [];
        native.ResponseCallback = bytes => expected.Add(Encoding.ASCII.GetString(bytes));
        string[] commands =
        [
            "a=T,i=73,p=1,s=1,v=1,C=1;/wAA/w==",
            "a=f,i=73,s=1,v=1,m=1;AAD/", "m=0;/w==",
            "a=a,i=73,c=2", "a=c,i=73,r=1,c=2",
            "a=q,i=7_4,s=1,v=1;AQIDBA", // Optional padding and Zig integer spelling.
            "a=q,i=74,I=-0,s=1,v=1;AQIDBB==", // Unsigned -0 and unused tail bits.
            "a=d,d=I,i=73", "a=p,i=73,p=1",
        ];
        foreach (string command in commands)
        {
            byte[] sequence = Encoding.ASCII.GetBytes("\u001b_G" + command + "\u001b\\");
            if (bytewise)
            {
                for (int i = 0; i < sequence.Length; i++)
                {
                    session.Processor.Process(sequence.AsSpan(i, 1));
                    native.Process(sequence.AsSpan(i, 1));
                }
            }
            else
            {
                session.Processor.Process(sequence);
                native.Process(sequence);
            }
            Assert.Equal(expected.ToArray(), session.Replies.ToArray());
        }
        Assert.Equal(6, expected.Count);
        Assert.Empty(session.Unknown);
    }

    [Theory]
    [InlineData(0x18)]
    [InlineData(0x1A)]
    [InlineData(0x1B)]
    [InlineData(0x9C)]
    [InlineData(0x9B)]
    [InlineData(0x85)]
    public void StreamingCommandCommitsOnceOnNativeApcExit(int exit)
    {
        RequireNative();
        byte[] prefix = "\u001b_Ga=q,i=73,s=1,v=1;/wAA/w=="u8.ToArray();
        byte[] suffix = [(byte)exit, 0x1B, (byte)'\\'];
        for (int split = 0; split <= prefix.Length; split++)
        {
            using Session session = new(8);
            using GhosttyVtProcessor native = new(new TerminalScreen(8, 3, 10));
            List<string> expected = [];
            native.ResponseCallback = bytes => expected.Add(Encoding.ASCII.GetString(bytes));
            session.Processor.Process(prefix.AsSpan(0, split));
            session.Processor.Process(prefix.AsSpan(split));
            session.Processor.Process(suffix);
            native.Process(prefix);
            native.Process(suffix);
            Assert.Equal("\u001b_Gi=73;OK\u001b\\", Assert.Single(expected));
            Assert.Equal(expected.ToArray(), session.Replies.ToArray());
            Assert.Empty(session.Unknown);
            Assert.True(session.Processor.IsParserGround);
        }
    }

    [Fact]
    public void WhitespaceDecodingMatchesNativeSimdPolicy()
    {
        RequireNative();
        if (!GhosttyVtHelpers.GetBuildFeatures().Simd)
            Assert.Skip("Pinned Ghostty scalar base64 rejects whitespace; this comparison targets its default simdutf decoder.");
        using Session session = new(32);
        using GhosttyVtProcessor native = new(new TerminalScreen(8, 3, 10));
        List<string> expected = [];
        native.ResponseCallback = bytes => expected.Add(Encoding.ASCII.GetString(bytes));
        byte[] sequence = "\u001b_Ga=q,i=73,s=1,v=1; \t/\fwAA/\rw\n== \u001b\\"u8.ToArray();
        session.Processor.Process(sequence);
        native.Process(sequence);
        Assert.Equal("\u001b_Gi=73;OK\u001b\\", Assert.Single(expected));
        Assert.Equal(expected.ToArray(), session.Replies.ToArray());
    }

    [Theory]
    [InlineData("a=p,i=73,p=1", "\u001b_Gi=73,p=1;ENOENT: image not found\u001b\\")]
    [InlineData("a=q,i=73,s=1,v=1,f=32;/wAA/w==", null)]
    public void ZeroPayloadQuotaPermitsControlCommandsAndSilentlyRejectsData(string command, string? reply)
    {
        using Session session = new(0);
        session.Send(command);
        if (reply is null) Assert.Empty(session.Replies);
        else Assert.Equal(reply, Assert.Single(session.Replies));
        Assert.Empty(session.Unknown);
    }

    [Theory]
    [InlineData("m=0,q=2;AAAAAAAAAAAA")]
    [InlineData("m=0,q=2;!!!!")]
    [InlineData("i=4294967296,m=0,q=2;/w==")]
    public void RejectedApcDoesNotCancelOrChangeQuietOfAnExistingImageUpload(string rejected)
    {
        using Session session = new(8);
        session.Send("a=T,i=73,p=1,s=1,v=1,C=1,m=1;/wAA");
        session.Send(rejected);
        Assert.Empty(session.Replies);
        session.Send("m=0;/w==");
        Assert.Equal("\u001b_Gi=73,p=1;OK\u001b\\", Assert.Single(session.Replies));
        Assert.True(session.Screen.TryGetKittyImageSource(73, out var image));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, image!.RgbaPixels);
        Assert.Empty(session.Unknown);
    }

    [Fact]
    public void DisabledAndAlreadyRejectedProtocolsDoNotAccumulateFurtherPayload()
    {
        byte[] bytes = new byte[1024 * 1024];
        Array.Fill(bytes, (byte)'A');
        foreach (bool disabled in new[] { false, true })
        {
            using Session session = new(0, disabled);
            session.Processor.Process("\u001b_Ga=q,i=73;A"u8);
            long before = GC.GetAllocatedBytesForCurrentThread();
            session.Processor.Process(bytes);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
            session.Processor.Process("\u001b\\"u8);
            Assert.Empty(session.Replies);
            Assert.Empty(session.Unknown);
            session.Processor.Process("\u001b_unknown\u001b\\"u8);
            Assert.Equal("unknown"u8.ToArray(), Assert.Single(session.Unknown).Content);
        }
    }

    [Fact]
    public void ResetAndContinuationReplayOwnIndependentParserStorage()
    {
        using Session original = new(8, continuationLimit: 1024);
        using Session replay = new(8, continuationLimit: 1024);
        original.Processor.Process("\u001b_Ga=q,i=73,s=1,v=1;AAAA"u8);
        replay.Processor.Process(original.Processor.GetContinuation());
        original.Processor.Reset();
        original.Send("a=p,i=74");
        replay.Processor.Process("/w==\u001b\\"u8);
        Assert.Equal("\u001b_Gi=74;ENOENT: image not found\u001b\\", Assert.Single(original.Replies));
        Assert.Equal("\u001b_Gi=73;OK\u001b\\", Assert.Single(replay.Replies));
    }

    private static void RequireNative()
    {
        if (GhosttyVtProcessor.IsAvailable() && GhosttyVtHelpers.GetBuildFeatures().KittyGraphics) return;
        Assert.NotEqual("1", Environment.GetEnvironmentVariable("ROYALTERMINAL_REQUIRE_NATIVE_TESTS"));
        Assert.Skip("Native Ghostty Kitty graphics runtime unavailable.");
    }

    private sealed class Session : IDisposable
    {
        internal TerminalScreen Screen { get; } = new(8, 3, 10);
        internal BasicVtProcessor Processor { get; }
        internal List<string> Replies { get; } = [];
        internal List<TerminalUnknownSequence> Unknown { get; } = [];

        internal Session(int limit, bool disabled = false, int continuationLimit = 0)
        {
            Processor = new(Screen, new()
            {
                KittyGraphicsMaxApcBytes = limit,
                KittyGraphicsStorageLimitBytes = disabled ? 0 : 1024,
                ContinuationMaxBytes = continuationLimit,
            });
            Processor.ResponseCallback = bytes => Replies.Add(Encoding.ASCII.GetString(bytes));
            Processor.UnknownSequenceCallback = Unknown.Add;
        }

        internal void Send(string command) => Processor.Process(Encoding.ASCII.GetBytes("\u001b_G" + command + "\u001b\\"));
        public void Dispose() => Processor.Dispose();
    }
}
