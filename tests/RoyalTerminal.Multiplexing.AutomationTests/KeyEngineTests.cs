// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Multiplexing.Automation;
using Xunit;
using static RoyalTerminal.Multiplexing.AutomationTests.AutomationTestData;

namespace RoyalTerminal.Multiplexing.AutomationTests;

public sealed class KeyEngineTests
{
    [Theory]
    [InlineData("Control+B > Shift+C", "ctrl+b>shift+c")]
    [InlineData("Command+Option+SHIFT+x", "alt+shift+meta+x")]
    [InlineData("esc", "escape")]
    [InlineData("super+return", "meta+enter")]
    [InlineData("ctrl+plus", "ctrl+plus")]
    public void KeyNormalizationIsPlatformIndependent(string raw, string expected)
        => Assert.Equal(expected, MultiplexerKeySyntax.NormalizeSequence(raw));

    [Theory]
    [InlineData("ctrl+control+x")]
    [InlineData("ctrl")]
    [InlineData("ctrl++")]
    [InlineData("a+b")]
    [InlineData("a>")]
    public void MalformedKeySequencesAreRejected(string raw)
    {
        Exception? error = Record.Exception(() => MultiplexerKeySyntax.NormalizeSequence(raw));
        Assert.True(error is MultiplexerAutomationException or ArgumentException);
    }

    [Fact]
    public void ChordConsumesPrefixesAndDispatchesOnlyOnCompletion()
    {
        MultiplexerKeyEngine engine = new(Config([new() { Sequence = "ctrl+b>c", ActionId = "one" }]));
        Assert.Equal(MultiplexerKeyDispatchKind.Pending, engine.ProcessKey("ctrl+b").Kind);
        Assert.Equal("ctrl+b", engine.PendingSequence);
        MultiplexerKeyDispatch result = engine.ProcessKey("c");
        Assert.Equal(MultiplexerKeyDispatchKind.Action, result.Kind);
        Assert.True(result.Consumed);
        Assert.Equal("one", result.ActionId);
        Assert.Null(engine.PendingSequence);
        Assert.False(engine.ProcessKey("x").Consumed);
    }

    [Fact]
    public void EscapeCancelsChordBeforeLeavingMode()
    {
        MultiplexerKeyEngine engine = new(Config([new() { Mode = "resize", Sequence = "g>g", ActionId = "one" }],
            [new() { Name = "resize" }]));
        engine.EnterMode("resize");
        engine.ProcessKey("g");
        Assert.Equal(MultiplexerKeyDispatchKind.Canceled, engine.ProcessKey("escape").Kind);
        Assert.Equal("resize", engine.CurrentMode);
        Assert.Equal(MultiplexerKeyDispatchKind.ModeChanged, engine.ProcessKey("escape").Kind);
        Assert.Equal("default", engine.CurrentMode);
    }

    [Fact]
    public void ExclusiveModeBlocksFallbackAndTerminalTyping()
    {
        MultiplexerKeyEngine engine = new(Config([new() { Sequence = "x", ActionId = "one" },
            new() { Mode = "resize", Sequence = "left", ActionId = "two" }], [new() { Name = "resize", Exclusive = true }]));
        engine.EnterMode("resize");
        Assert.Equal(MultiplexerKeyDispatchKind.Blocked, engine.ProcessKey("x").Kind);
        Assert.Equal(MultiplexerKeyDispatchKind.Blocked, engine.ProcessKey("z").Kind);
        Assert.Equal("two", engine.ProcessKey("left").ActionId);
    }

    [Fact]
    public void HighestNonexclusiveModeWinsAndUnboundKeysFallThrough()
    {
        MultiplexerKeyEngine engine = new(Config([
            new() { Sequence = "a", ActionId = "one" },
            new() { Mode = "lower", Sequence = "b", ActionId = "two" },
            new() { Mode = "upper", Sequence = "b", ActionId = "three" },
        ]));
        engine.EnterMode("lower");
        engine.EnterMode("upper");
        Assert.Equal("three", engine.ProcessKey("b").ActionId);
        Assert.Equal("one", engine.ProcessKey("a").ActionId);
        Assert.True(engine.ExitMode());
        Assert.Equal("two", engine.ProcessKey("b").ActionId);
        engine.ExitAllModes();
        Assert.False(engine.ExitMode());
        Assert.Equal(["default"], engine.ModeStack);
    }

    [Fact]
    public void OnceFrameRemainsThroughPrefixAndLeavesAfterResolvedShortcut()
    {
        MultiplexerKeyEngine engine = new(Config([new() { Mode = "once", Sequence = "g>g", ActionId = "one" }]));
        engine.EnterMode("once", once: true);
        Assert.False(engine.ProcessKey("x").Consumed);
        Assert.Equal("once", engine.CurrentMode);
        Assert.Equal(MultiplexerKeyDispatchKind.Pending, engine.ProcessKey("g").Kind);
        Assert.Equal("once", engine.CurrentMode);
        Assert.Equal("one", engine.ProcessKey("g").ActionId);
        Assert.Equal("default", engine.CurrentMode);
    }

    [Fact]
    public void ExplicitEscapeBindingOverridesAutomaticModeExit()
    {
        MultiplexerKeyEngine engine = new(Config([new() { Mode = "mode", Sequence = "escape", ActionId = "one" }]));
        engine.EnterMode("mode");
        Assert.Equal("one", engine.ProcessKey("escape").ActionId);
        Assert.Equal("mode", engine.CurrentMode);
    }

    [Fact]
    public void BlockedBindingSuppressesDefaultEvenInNonexclusiveMode()
    {
        MultiplexerKeyEngine engine = new(Config([new() { Sequence = "left", ActionId = "one" },
            new() { Mode = "mode", Sequence = "left", Block = true }]));
        engine.EnterMode("mode", once: true);
        Assert.Equal(MultiplexerKeyDispatchKind.Blocked, engine.ProcessKey("left").Kind);
        Assert.Equal("default", engine.CurrentMode);
    }

    [Fact]
    public void PrefixTimeoutCannotTriggerAStaleChord()
    {
        KeyTimeProvider time = new();
        MultiplexerKeyEngine engine = new(Config([new() { Sequence = "ctrl+b>c", ActionId = "one" }]), timeProvider: time);
        engine.ProcessKey("ctrl+b");
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.False(engine.ProcessKey("c").Consumed);
        Assert.Null(engine.PendingSequence);
    }

    [Fact]
    public void NonexclusiveChordMismatchReprocessesOnlyTheLastKey()
    {
        MultiplexerKeyEngine engine = new(Config([new() { Sequence = "ctrl+b>c", ActionId = "one" },
            new() { Sequence = "x", ActionId = "two" }]));
        engine.ProcessKey("ctrl+b");
        Assert.Equal("two", engine.ProcessKey("x").ActionId);
        Assert.Null(engine.PendingSequence);
    }

    [Fact]
    public void ModeBindingsEnterOnceAndExitWithoutDoublePoppingParent()
    {
        MultiplexerKeyEngine engine = new(Config([
            new() { Sequence = "ctrl+r", EnterMode = "resize", Once = true },
            new() { Mode = "resize", Sequence = "enter", ExitMode = true },
        ]));
        engine.ProcessKey("ctrl+r");
        Assert.Equal("resize", engine.CurrentMode);
        engine.ProcessKey("enter");
        Assert.Equal(["default"], engine.ModeStack);
    }

    [Fact]
    public void StackDepthIsBoundedAndFailedEnterPreservesState()
    {
        MultiplexerKeyEngine engine = new(Config([], [new() { Name = "mode" }]), new() { MaxModeDepth = 2 });
        engine.EnterMode("mode");
        Assert.Throws<MultiplexerAutomationException>(() => engine.EnterMode("mode"));
        Assert.Equal(["default", "mode"], engine.ModeStack);
    }
}
