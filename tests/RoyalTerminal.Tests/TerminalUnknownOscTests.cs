// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalUnknownOscTests
{
    [Theory]
    [InlineData("7400;status=busy")]
    [InlineData("00;alias")]
    [InlineData("+22;pointer")]
    [InlineData("1338;hello")]
    [InlineData("3")]
    [InlineData("750")]
    [InlineData("55")]
    [InlineData("λ;界")]
    public void UnknownSelectorsMatchNativeAtEverySplit(string content)
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        foreach (string end in new[] { "\a", "\u001b\\", "\u001bX" })
        {
            byte[] bytes = Encoding.UTF8.GetBytes("\u001b]" + content + end);
            for (int split = 0; split <= bytes.Length; split++)
            {
                using GhosttyVtProcessor native = new(new TerminalScreen(8, 3));
                using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
                List<TerminalUnknownSequence> expected = [], actual = [];
                native.UnknownSequenceCallback = expected.Add;
                managed.UnknownSequenceCallback = actual.Add;
                native.Process(bytes.AsSpan(0, split)); managed.Process(bytes.AsSpan(0, split));
                native.Process(bytes.AsSpan(split)); managed.Process(bytes.AsSpan(split));
                Assert.True(expected.Count == 1, $"Native end {Convert.ToHexString(Encoding.UTF8.GetBytes(end))} split {split}: {expected.Count}");
                Assert.True(actual.Count == 1, $"Managed end {Convert.ToHexString(Encoding.UTF8.GetBytes(end))} split {split}: {actual.Count}");
                TerminalUnknownSequence e = expected[0], a = actual[0];
                Assert.Equal(TerminalUnknownSequenceType.Osc, a.Type);
                Assert.Equal(e.Content, a.Content);
                Assert.Equal(e.Truncated, a.Truncated);
                Assert.Equal(e.Terminator, a.Terminator);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LimitsAreBoundedOwnedAndFixedAtRecognition(bool native)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) return;
        using IVtProcessor processor = Create(native);
        ITerminalUnknownSequencePolicy policy = (ITerminalUnknownSequencePolicy)processor;
        List<TerminalUnknownSequence> reports = [];
        ((ITerminalEffectSource)processor).UnknownSequenceCallback = reports.Add;
        foreach (int limit in new[] { 0, 1, 4, 2048, 4096, 10000 })
        {
            policy.UnknownSequenceMaxBytes = limit;
            processor.Process("\u001b]7400;"u8);
            policy.UnknownSequenceMaxBytes = 1;
            processor.Process(Encoding.ASCII.GetBytes(new string('x', 12000) + "\a"));
            if (limit == 0) { Assert.Empty(reports); continue; }
            TerminalUnknownSequence report = Assert.Single(reports);
            Assert.Equal(limit, report.Content.Length);
            Assert.True(report.Truncated);
            Assert.Equal((byte)'7', report.Content[0]);
            report.Content[0] = 0;
            reports.Clear();
        }
        policy.UnknownSequenceMaxBytes = 8;
        processor.Process("\u001b]750"u8); // Still a supported prefix; policy is not captured yet.
        policy.UnknownSequenceMaxBytes = 2;
        processor.Process("2;ignored\a"u8);
        Assert.Equal("75"u8.ToArray(), Assert.Single(reports).Content);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KnownMalformedAndCancelledCommandsAreNotUnknown(bool native)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) return;
        using IVtProcessor processor = Create(native);
        List<TerminalUnknownSequence> reports = [];
        ((ITerminalEffectSource)processor).UnknownSequenceCallback = reports.Add;
        foreach (int selector in new[] { 0, 1, 2, 4, 5, 7, 8, 9, 10, 19, 21, 22, 52, 66,
            72, 99, 104, 105, 110, 119, 133, 777, 1337, 3008, 5522, 7501 })
            processor.Process(Encoding.ASCII.GetBytes($"\u001b]{selector};invalid\a"));
        processor.Process("\u001b]\a\u001b]7400;aborted\u0018\u001b]7400;aborted\u001a"u8);
        Assert.Empty(reports);
        processor.Process("\u001b]7400;\u0001hi\a"u8);
        Assert.Equal("7400;hi"u8.ToArray(), Assert.Single(reports).Content);
        reports.Clear();
        // Ghostty retains C1 bytes as binary OSC payload, including 0x9c.
        processor.Process([27, (byte)']', (byte)'X', 0x9c, 0xff, 7]);
        Assert.Equal(new byte[] { (byte)'X', 0x9c, 0xff }, Assert.Single(reports).Content);
    }

    private static IVtProcessor Create(bool native)
        => native ? new GhosttyVtProcessor(new TerminalScreen(8, 3)) : new BasicVtProcessor(new TerminalScreen(8, 3));
}
