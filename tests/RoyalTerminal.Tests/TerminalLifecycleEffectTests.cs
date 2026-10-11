// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalLifecycleEffectTests
{
    [Theory]
    [InlineData("A")]
    [InlineData("N;k=r")]
    [InlineData("A;k=c")]
    [InlineData("P;k=s")]
    [InlineData("P;k=x;k=r")]
    [InlineData("B;ignored=yes")]
    [InlineData("I")]
    [InlineData("C;cmdline=echo\\ hi")]
    [InlineData("C;cmdline=$'echo\\nhi\\e\\t\\v\\r'")]
    [InlineData("C;cmdline='echo \\\"hi\\\"'")]
    [InlineData("C;cmdline_url=echo%20%FF%00%F0%9F%98%80")]
    [InlineData("C;cmdline_url=second;cmdline=first;cmdline=third")]
    [InlineData("C;cmdline=;cmdline_url=second")]
    [InlineData("C;cmdline=bad\\x20;cmdline_url=second")]
    [InlineData("C;cmdline=$'missing")]
    [InlineData("C;cmdline_url=bad%2")]
    [InlineData("D;-2147483648;err=example;err=ignored")]
    [InlineData("D;+42;err=error")]
    [InlineData("D;4_2")]
    [InlineData("D; 42")]
    [InlineData("D;2147483648")]
    [InlineData("D;exit_code=1")]
    [InlineData("D;;42")]
    [InlineData("L")]
    [InlineData("L;invalid")]
    [InlineData("Ainvalid")]
    public void PromptEffectsMatchNativeAtEverySplit(string payload)
    {
        if (!GhosttyVtProcessor.IsAvailable()) return;
        byte[] bytes = Encoding.UTF8.GetBytes("\u001b]133;" + payload + "\a");
        for (int split = 0; split <= bytes.Length; split++)
        {
            using GhosttyVtProcessor native = new(new TerminalScreen(8, 3));
            using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
            List<string> expected = [], actual = [];
            native.SemanticPromptCallback = report => expected.Add(Describe(report));
            managed.SemanticPromptCallback = report => actual.Add(Describe(report));
            native.Process(bytes.AsSpan(0, split)); managed.Process(bytes.AsSpan(0, split));
            native.Process(bytes.AsSpan(split)); managed.Process(bytes.AsSpan(split));
            Assert.Equal(expected, actual);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResetIsOrderedAfterProgressAndProgramCleanupAndOnlyRaisedForRis(bool native)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) return;
        using IVtProcessor processor = native ? new GhosttyVtProcessor(new TerminalScreen(8, 3)) : new BasicVtProcessor(new TerminalScreen(8, 3));
        ITerminalLifecycleEffectSource lifecycle = (ITerminalLifecycleEffectSource)processor;
        ITerminalProgramStatusSource status = (ITerminalProgramStatusSource)processor;
        List<string> effects = [];
        ((ITerminalEffectSource)processor).ProgressReportCallback = _ => effects.Add("progress");
        status.ProgramStatusChangedCallback = () => effects.Add("program");
        lifecycle.ResetCallback = () => { Assert.Empty(status.ProgramStatuses); effects.Add("reset"); };
        processor.Process("\u001b]7501;state=working\a"u8);
        effects.Clear();
        processor.Process("\u001bc"u8);
        Assert.Equal(new[] { "progress", "program", "reset" }, effects);
        effects.Clear();
        processor.Process("\u001b[!p"u8);
        processor.Reset();
        ((ITerminalSessionHistoryController)processor).PrepareForNewSession(false);
        Assert.Empty(effects);
        List<TerminalSemanticPromptReport> prompts = [];
        lifecycle.SemanticPromptCallback = prompts.Add;
        processor.Process(Encoding.ASCII.GetBytes("\u001b]133;A;" + new string('x', 2047) + "\a"));
        processor.Process("\u001b]133;A\u0018\u001b]133;A\u001a"u8);
        Assert.Empty(prompts);
        processor.Process("\u001b]133;C;cmdline_url=%FF%00\a"u8);
        TerminalSemanticPromptReport saved = Assert.Single(prompts);
        processor.Process("\u001b]133;C;cmdline=other\a"u8);
        Assert.Equal(new byte[] { 255, 0 }, saved.Command.ToArray());
    }

    private static string Describe(TerminalSemanticPromptReport report)
        => $"{report.Kind}/{report.Role}/{report.ExitCode}/{Convert.ToHexString(report.Command.Span)}/{Convert.ToHexString(report.Error.Span)}";
}
