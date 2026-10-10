// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalUnknownSequencePolicyTests(ITestOutputHelper output)
{
    [Fact]
    public void ExistingConstructorAndDeconstructionPreserveSequenceData()
    {
        byte[] payload = "Xpayload"u8.ToArray();
        TerminalUnknownSequence sequence = new(Type: TerminalUnknownSequenceType.Apc, Content: payload, Truncated: true);
        (TerminalUnknownSequenceType type, byte[] content, bool truncated) = sequence;
        Assert.Equal(TerminalUnknownSequenceType.Apc, type);
        Assert.Same(payload, content);
        Assert.True(truncated);
        Assert.Equal(TerminalOscTerminator.St, sequence.Terminator);

        TerminalUnknownSequence osc = sequence with { Type = TerminalUnknownSequenceType.Osc, Terminator = TerminalOscTerminator.Bel };
        (type, content, truncated, TerminalOscTerminator terminator) = osc;
        Assert.Equal(TerminalUnknownSequenceType.Osc, type);
        Assert.Same(payload, content);
        Assert.True(truncated);
        Assert.Equal(TerminalOscTerminator.Bel, terminator);
    }

    public static IEnumerable<object[]> Prefixes()
    {
        foreach (bool native in new[] { false, true })
        foreach (string prefix in new[] { "", "2", "25", "25a", "25a1", "G", "25a1;" })
            yield return [native, prefix];
    }

    [Theory]
    [MemberData(nameof(Prefixes))]
    public void EmptyAndIncompleteKnownIdentifiersAreNotUnknownCommands(bool native, string prefix)
    {
        using IVtProcessor? processor = Create(native);
        if (processor is null) return;
        List<TerminalUnknownSequence> reports = [];
        ((ITerminalEffectSource)processor).UnknownSequenceCallback = reports.Add;
        Write(processor, "\u001b_" + prefix + "\u001b\\");
        Assert.Empty(reports);
        Write(processor, "\u001b_X\u001b\\");
        Assert.Equal("X"u8.ToArray(), Assert.Single(reports).Content);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    [InlineData(false, 4096)]
    [InlineData(true, 4096)]
    public void CaptureIsBoundedOwnedAndRetainsPolicyAcrossReset(bool native, int limit)
    {
        using IVtProcessor? processor = Create(native);
        if (processor is null) return;
        ITerminalUnknownSequencePolicy policy = (ITerminalUnknownSequencePolicy)processor;
        Assert.Equal(4096, policy.UnknownSequenceMaxBytes);
        policy.UnknownSequenceMaxBytes = limit;
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.UnknownSequenceMaxBytes = -1);
        Assert.Equal(limit, policy.UnknownSequenceMaxBytes);
        processor.Reset();
        Assert.Equal(limit, policy.UnknownSequenceMaxBytes);
        List<TerminalUnknownSequence> reports = [];
        ((ITerminalEffectSource)processor).UnknownSequenceCallback = reports.Add;
        // '25b' becomes unsupported after a two-byte known prefix; small
        // quotas must truncate that prefix as well as the following payload.
        Write(processor, "\u001b_25bhello\u001b\\");
        if (limit == 0) { Assert.Empty(reports); return; }
        TerminalUnknownSequence first = Assert.Single(reports);
        byte[] expected = Encoding.ASCII.GetBytes("25bhello"[..Math.Min(limit, 8)]);
        Assert.Equal(expected, first.Content);
        Assert.Equal(limit < 8, first.Truncated);
        first.Content[0] = 0;
        reports.Clear();
        Write(processor, "\u001b_25bhello\u001b\\");
        Assert.Equal(expected, Assert.Single(reports).Content);
    }

    public static IEnumerable<object[]> PolicyChanges()
    {
        object[][] cases =
        [
            [4, 1, "X", "abcdef", "Xabc", true],
            [4, 0, "X", "abcdef", "Xabc", true],
            [1, 8, "25", "brest", "25brest", false],
            [8, 1, "25a1", "?rest", "2", true],
            [0, 8, "X", "bad", "Xbad", false],
            // Native delays mismatch detection while capture is disabled;
            // enabling does not retroactively re-parse the previous byte.
            [0, 8, "X", "5a1", null!, false],
            [0, 8, ";", "X", null!, false],
            [0, 8, "", "hello", "hello", false],
        ];
        foreach (bool native in new[] { false, true })
        foreach (object[] item in cases) yield return [native, .. item];
    }

    [Theory]
    [MemberData(nameof(PolicyChanges))]
    public void CurrentCaptureKeepsItsLimitAndIdentifyUsesTheCurrentPolicy(bool native, int initial, int changed,
        string prefix, string suffix, string? expected, bool truncated)
    {
        using IVtProcessor? processor = Create(native);
        if (processor is null) return;
        ITerminalUnknownSequencePolicy policy = (ITerminalUnknownSequencePolicy)processor;
        List<TerminalUnknownSequence> reports = [];
        ((ITerminalEffectSource)processor).UnknownSequenceCallback = reports.Add;
        policy.UnknownSequenceMaxBytes = initial;
        Write(processor, "\u001b_" + prefix);
        policy.UnknownSequenceMaxBytes = changed;
        Write(processor, suffix + "\u001b\\");
        if (expected is null) Assert.Empty(reports);
        else
        {
            TerminalUnknownSequence report = Assert.Single(reports);
            Assert.Equal(Encoding.ASCII.GetBytes(expected), report.Content);
            Assert.Equal(truncated, report.Truncated);
        }
        reports.Clear();
        Write(processor, "\u001b_X\u001b\\");
        if (changed == 0) Assert.Empty(reports);
        else Assert.Equal("X"u8.ToArray(), Assert.Single(reports).Content);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AbortedCapturesStaySilentAndDisabledCaptureDoesNotPromoteKitty(bool native)
    {
        using IVtProcessor? processor = Create(native);
        if (processor is null) return;
        ITerminalUnknownSequencePolicy policy = (ITerminalUnknownSequencePolicy)processor;
        ITerminalEffectSource effects = (ITerminalEffectSource)processor;
        List<TerminalUnknownSequence> reports = [];
        List<byte[]> replies = [];
        effects.UnknownSequenceCallback = reports.Add;
        processor.ResponseCallback = replies.Add;
        Write(processor, "\u001b_X\u0018\u001b_X\u001a");
        Assert.Empty(reports);
        policy.UnknownSequenceMaxBytes = 0;
        // Once ignored, a later G must not restart Kitty recognition.
        Write(processor, "\u001b_;Ga=q,i=71,s=1,v=1,f=24;AAAA\u001b\\");
        Assert.Empty(reports);
        Assert.Empty(replies);
    }

    [Fact]
    public void ManagedOptionsValidateAndSnapshotDoesNotTransferHostPolicy()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BasicVtProcessor(new TerminalScreen(8, 2),
            new() { UnknownSequenceMaxBytes = -1 }));
        using BasicVtProcessor processor = new(new TerminalScreen(8, 2), new() { UnknownSequenceMaxBytes = 1 });
        Assert.Equal(1, processor.UnknownSequenceMaxBytes);
        using ManagedTerminalSnapshot restored = ManagedTerminalSnapshot.Restore(processor.GetBinarySnapshot());
        Assert.Equal(4096, restored.Processor.UnknownSequenceMaxBytes);
    }

    private IVtProcessor? Create(bool native)
    {
        if (native && !GhosttyVtProcessor.IsAvailable())
        {
            output.WriteLine("Native unknown-sequence policy comparison unavailable.");
            return null;
        }
        TerminalScreen screen = new(8, 2);
        return native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
    }

    private static void Write(IVtProcessor processor, string input) => processor.Process(Encoding.ASCII.GetBytes(input));
}
