// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class GhosttyNativeContinuationBoundaryTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0x90, "$q", "\u001bP$q", "m\u001b\\X")]
    [InlineData(0x9B, "31", "\u001b[31", "mX")]
    [InlineData(0x9D, "2;next", "\u001b]2;next", "\u001b\\X")]
    [InlineData(0x9B, "3\u00071", "\u001b[31", "mX")]
    [InlineData(0x90, "\u009fGa=q,i=74,s=1,v=1,f=32;AAAA/w==\u009b31", "\u001b[31", "mX")]
    public void C1AfterCommittedApcExportsCanonicalReplayAndSnapshot(int control, string suffix, string expected, string tail)
    {
        bool available = GhosttyVtProcessor.IsAvailable();
        output.WriteLine($"Native continuation boundary available: {available}");
        if (!available) return;
        byte[] prefix = "\u001b_Ga=q,i=73,s=1,v=1,f=32;AAAA/w=="u8.ToArray();
        byte[] bytes = [.. prefix, (byte)control, .. Encoding.Latin1.GetBytes(suffix)];
        byte[] expectedBytes = Encoding.ASCII.GetBytes(expected);
        for (int split = 0; split <= bytes.Length; split++)
        {
            using GhosttyTerminal original = new(12, 3);
            original.SetContinuationMaxBytes(65536);
            original.Write(bytes.AsSpan(0, split)); original.Write(bytes.AsSpan(split));
            Assert.Equal(expectedBytes, original.GetContinuation());
            Assert.True(GhosttySnapshotContinuation.IsValid(original.GetContinuation()));
            using MemoryStream streamed = new();
            original.WriteContinuationTo(streamed);
            Assert.Equal(expectedBytes, streamed.ToArray());
            byte[] snapshot = GhosttySnapshot.Encode(original);
            using GhosttyTerminal restored = GhosttySnapshot.Decode(snapshot, retainContinuation: true);
            Assert.Equal(expectedBytes, restored.GetContinuation());
            original.Write(Encoding.ASCII.GetBytes(tail)); restored.Write(Encoding.ASCII.GetBytes(tail));
            Assert.Equal(GhosttySnapshot.Encode(original), GhosttySnapshot.Encode(restored));
        }
    }

    [Theory]
    [InlineData("\u001b]2;a\u009bb")]
    [InlineData("\u001bPqa\u009bb")]
    [InlineData("\u001b[\u009b31")]
    public void UncommittedC1PayloadOrHeaderIsNotDiscarded(string sequence)
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        using GhosttyTerminal terminal = new(12, 3);
        terminal.SetContinuationMaxBytes(65536);
        byte[] bytes = Encoding.Latin1.GetBytes(sequence);
        terminal.Write(bytes);
        Assert.Equal(bytes, terminal.GetContinuation());
        using GhosttyTerminal restored = GhosttySnapshot.Decode(GhosttySnapshot.Encode(terminal), retainContinuation: true);
        Assert.Equal(bytes, restored.GetContinuation());
    }
}
