// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ManagedTerminfoReplyTests
{
    [Theory]
    [InlineData("436f", "\u001bP1+r436F=323536\u001b\\")]
    [InlineData("524742", "\u001bP1+r524742=38\u001b\\")]
    [InlineData("4158", "\u001bP1+r4158\u001b\\")]
    [InlineData("544e", "\u001bP1+r544E=7465726D\u001b\\")]
    public void ByteAndCharacterLookupsReturnOwnedCanonicalReplies(string key, string expected)
    {
        Assert.True(GhosttyXtgettcap.TryCreateResponse(key.AsSpan(), "term", out byte[] characters));
        Assert.True(GhosttyXtgettcap.TryCreateResponse(Encoding.ASCII.GetBytes(key), "term", out byte[] bytes));
        Assert.Equal(Encoding.ASCII.GetBytes(expected), characters);
        Assert.Equal(characters, bytes);
        characters[0] = 0;
        Assert.Equal(0x1B, bytes[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("GG")]
    [InlineData("ＴＮ")]
    [InlineData("ABCD")]
    [InlineData("0000000000000000000000000000")]
    public void UnknownMalformedAndOversizedKeysAllocateNoResponses(string key)
    {
        Assert.False(GhosttyXtgettcap.TryCreateResponse(key.AsSpan(), "term", out byte[] characters));
        Assert.Empty(characters);
        Assert.False(GhosttyXtgettcap.TryCreateResponse(Encoding.UTF8.GetBytes(key), "term", out byte[] bytes));
        Assert.Empty(bytes);
    }

    [Theory]
    [InlineData(63, true)]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void TerminfoNameLimitCountsUtf8BytesBeforeAllocating(int count, bool supported)
    {
        string name = new('é', count);
        Assert.Equal(supported, GhosttyXtgettcap.TryCreateResponse("544e"u8, name, out byte[] response));
        if (supported)
            Assert.Equal($"\u001bP1+r544E={Convert.ToHexString(Encoding.UTF8.GetBytes(name))}\u001b\\", Encoding.ASCII.GetString(response));
        else Assert.Empty(response);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void DisabledNameDoesNotSuppressOtherCapabilities(string? name)
    {
        Assert.False(GhosttyXtgettcap.TryCreateResponse("544E"u8, name, out _));
        Assert.True(GhosttyXtgettcap.TryCreateResponse("436F"u8, name, out _));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(150)]
    public void StackAndPooledBatchesPreserveReplyOrderAndOwnership(int count)
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 2), new() { TerminfoName = "term" });
        string[] keys = new string[count];
        Array.Fill(keys, "436f;BAD;4158;544e");
        byte[] input = Encoding.ASCII.GetBytes("\u001bP+q;" + string.Join(';', keys) + ";\u001b\\");
        List<byte[]> replies = [];
        processor.ResponseCallback = replies.Add;
        processor.Process(input.AsSpan(0, input.Length / 2));
        Assert.Empty(replies);
        processor.Process(input.AsSpan(input.Length / 2));
        Assert.Equal(count * 3, replies.Count);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal("\u001bP1+r436F=323536\u001b\\", Encoding.ASCII.GetString(replies[i * 3]));
            Assert.Equal("\u001bP1+r4158\u001b\\", Encoding.ASCII.GetString(replies[i * 3 + 1]));
            Assert.Equal("\u001bP1+r544E=7465726D\u001b\\", Encoding.ASCII.GetString(replies[i * 3 + 2]));
        }
        if (count > 1)
        {
            replies[0][0] = 0;
            Assert.Equal(0x1B, replies[3][0]);
        }
    }

    [Fact]
    public void CallbackCanDisableRemainingRepliesWithoutLeakingBorrowedStorage()
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 2));
        processor.Process("\u001bP+q436F;4158\u001b\\"u8); // No consumer: no deferred replies.
        int replies = 0;
        processor.ResponseCallback = _ => { replies++; processor.ResponseCallback = null; };
        string[] queries = new string[200];
        Array.Fill(queries, "436F");
        processor.Process(Encoding.ASCII.GetBytes("\u001bP+q" + string.Join(';', queries) + "\u001b\\"));
        Assert.Equal(1, replies);
        byte[]? next = null;
        processor.ResponseCallback = bytes => next = bytes;
        processor.Process("\u001bP+q4158\u001b\\"u8);
        Assert.Equal("\u001bP1+r4158\u001b\\"u8.ToArray(), next);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    public void ThrowingCallbackDoesNotRetainOrReplayBatchAfterReset(int count)
    {
        using BasicVtProcessor processor = new(new TerminalScreen(8, 2));
        string[] queries = new string[count];
        Array.Fill(queries, "436F");
        byte[] input = Encoding.ASCII.GetBytes("\u001bP+q" + string.Join(';', queries) + "\u001b\\");
        InvalidOperationException failure = new("Injected reply consumer failure");
        processor.ResponseCallback = _ => throw failure;
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => processor.Process(input)));
        processor.ResponseCallback = null;
        processor.Reset();
        List<byte[]> replies = [];
        processor.ResponseCallback = replies.Add;
        processor.Process("\u001bP+q4158\u001b\\"u8);
        Assert.Equal("\u001bP1+r4158\u001b\\"u8.ToArray(), Assert.Single(replies));
    }

    [Fact]
    public void InvalidQueriesAndSuppressedNamesHaveNoLookupScratchAllocations()
    {
        string longName = new('x', 129);
        for (int i = 0; i < 100; i++) Exercise();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) Exercise();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);

        void Exercise()
        {
            _ = GhosttyXtgettcap.TryCreateResponse("ABCD"u8, null, out _);
            _ = GhosttyXtgettcap.TryCreateResponse("GG"u8, null, out _);
            _ = GhosttyXtgettcap.TryCreateResponse("544e"u8, longName, out _);
        }
    }
}
