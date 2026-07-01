// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalShellIntegrationContractTests
{
    [Fact]
    public void Parser_Osc7_WorkingDirectoryChanged_RaisesDecodedPath()
    {
        TerminalShellIntegrationParser parser = new();
        List<TerminalShellIntegrationEvent> events = [];
        parser.EventReceived += (_, e) => events.Add(e.Value);

        bool handled = parser.TryHandleOsc(
            7,
            "file://localhost/Users/alice/My%20Project",
            new DateTimeOffset(2026, 6, 30, 10, 0, 0, TimeSpan.Zero));

        Assert.True(handled);
        TerminalShellIntegrationEvent value = Assert.Single(events);
        Assert.Equal(TerminalShellIntegrationEventKind.WorkingDirectoryChanged, value.Kind);
        Assert.Equal("/Users/alice/My Project", value.WorkingDirectory);
        Assert.Equal("localhost", value.Host);
    }

    [Fact]
    public void Parser_Osc7_WindowsFileUri_RemovesLeadingSlashFromDrivePath()
    {
        TerminalShellIntegrationParser parser = new();
        TerminalShellIntegrationEvent? value = null;
        parser.EventReceived += (_, e) => value = e.Value;

        bool handled = parser.TryHandleOsc(
            7,
            "file://DESKTOP/C%3A%5CUsers%5CAlice%5CProject");

        Assert.True(handled);
        Assert.NotNull(value);
        Assert.Equal(@"C:\Users\Alice\Project", value!.WorkingDirectory);
        Assert.Equal("desktop", value.Host);
    }

    [Fact]
    public void Parser_Osc133_OutputStartAndFinish_RaisesStructuredCommandEvents()
    {
        TerminalShellIntegrationParser parser = new();
        List<TerminalShellIntegrationEvent> events = [];
        parser.EventReceived += (_, e) => events.Add(e.Value);

        bool outputHandled = parser.TryHandleOsc(
            133,
            "C;aid=session-1;cmdline_url=git%20status",
            new DateTimeOffset(2026, 6, 30, 10, 0, 0, TimeSpan.Zero));
        bool finishHandled = parser.TryHandleOsc(
            133,
            "D;0",
            new DateTimeOffset(2026, 6, 30, 10, 0, 2, TimeSpan.Zero));

        Assert.True(outputHandled);
        Assert.True(finishHandled);
        Assert.Equal(2, events.Count);
        Assert.Equal(TerminalShellIntegrationEventKind.OutputStarted, events[0].Kind);
        Assert.Equal("git status", events[0].CommandLine);
        Assert.Equal("session-1", events[0].ApplicationId);
        Assert.Equal(TerminalShellIntegrationEventKind.CommandFinished, events[1].Kind);
        Assert.Equal(0, events[1].ExitCode);
    }

    [Fact]
    public void Parser_Osc133_IgnoresUnknownOptions_AndDecodesCmdlineEscapes()
    {
        TerminalShellIntegrationParser parser = new();
        TerminalShellIntegrationEvent? captured = null;
        parser.EventReceived += (_, e) => captured = e.Value;

        bool handled = parser.TryHandleOsc(133, "C;unknown=value;cmdline=$'echo one\\ntwo'");

        Assert.True(handled);
        Assert.NotNull(captured);
        Assert.Equal("echo one\ntwo", captured!.CommandLine);
        Assert.True(captured.Options.ContainsKey("unknown"));
    }

    [Fact]
    public void Parser_DoesNotCaptureCommandsFromRawKeystrokes()
    {
        TerminalCommandHistoryCaptureService capture = new();
        TerminalShellIntegrationEvent unrelatedPrompt = new()
        {
            Kind = TerminalShellIntegrationEventKind.InputStarted,
            TimestampUtc = DateTimeOffset.UtcNow,
        };

        TerminalCommandHistoryEntry? entry = capture.Process(unrelatedPrompt);

        Assert.Null(entry);
    }

    [Fact]
    public void CaptureService_RecordsCompletedCommand_FromShellIntegrationEvents()
    {
        TerminalCommandHistoryCaptureService capture = new(
            new TerminalCommandHistoryCaptureContext(
                ProfileId: "local",
                TransportId: TerminalTransportIds.Pty));
        DateTimeOffset started = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset completed = started.AddSeconds(1);

        Assert.Null(capture.Process(new TerminalShellIntegrationEvent
        {
            Kind = TerminalShellIntegrationEventKind.WorkingDirectoryChanged,
            TimestampUtc = started,
            WorkingDirectory = "/tmp/project",
            Host = "localhost",
        }));
        Assert.Null(capture.Process(new TerminalShellIntegrationEvent
        {
            Kind = TerminalShellIntegrationEventKind.OutputStarted,
            TimestampUtc = started,
            CommandLine = "dotnet test",
            ApplicationId = "session-1",
        }));
        TerminalCommandHistoryEntry? entry = capture.Process(new TerminalShellIntegrationEvent
        {
            Kind = TerminalShellIntegrationEventKind.CommandFinished,
            TimestampUtc = completed,
            ExitCode = 0,
        });

        Assert.NotNull(entry);
        Assert.Equal("dotnet test", entry!.CommandLine);
        Assert.Equal("/tmp/project", entry.WorkingDirectory);
        Assert.Equal("local", entry.ProfileId);
        Assert.Equal(TerminalTransportIds.Pty, entry.TransportId);
        Assert.Equal("localhost", entry.Host);
        Assert.Equal("session-1", entry.ApplicationId);
        Assert.Equal(0, entry.ExitCode);
    }

    [Fact]
    public void BasicVtProcessor_Osc133_RaisesShellIntegrationEvents()
    {
        TerminalScreen screen = new(80, 24, 100);
        using BasicVtProcessor processor = new(screen);
        List<TerminalShellIntegrationEvent> events = [];
        processor.ShellIntegrationEventReceived += (_, e) => events.Add(e.Value);

        byte[] payload = Encoding.UTF8.GetBytes("\u001b]133;C;cmdline_url=git%20status\u001b\\\u001b]133;D;0\u001b\\");
        processor.Process(payload);

        Assert.Equal(2, events.Count);
        Assert.Equal(TerminalShellIntegrationEventKind.OutputStarted, events[0].Kind);
        Assert.Equal("git status", events[0].CommandLine);
        Assert.Equal(TerminalShellIntegrationEventKind.CommandFinished, events[1].Kind);
        Assert.Equal(0, events[1].ExitCode);
    }

    [Fact]
    public void GhosttyVtProcessor_Osc133_PreParserRaisesShellIntegrationEvents_WhenGhosttyAvailable()
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            return;
        }

        TerminalScreen screen = new(80, 24, 100);
        using GhosttyVtProcessor processor = new(screen);
        List<TerminalShellIntegrationEvent> events = [];
        processor.ShellIntegrationEventReceived += (_, e) => events.Add(e.Value);

        byte[] firstChunk = Encoding.UTF8.GetBytes("\u001b]7;file://localhost/tmp/project\u0007\u001b]133;C;cmdline_url=git%20status");
        byte[] secondChunk = Encoding.UTF8.GetBytes("\u001b\\\u001b]133;D;0\u0007");
        processor.Process(firstChunk);
        processor.Process(secondChunk);

        Assert.Equal(3, events.Count);
        Assert.Equal(TerminalShellIntegrationEventKind.WorkingDirectoryChanged, events[0].Kind);
        Assert.Equal("/tmp/project", events[0].WorkingDirectory);
        Assert.Equal(TerminalShellIntegrationEventKind.OutputStarted, events[1].Kind);
        Assert.Equal("git status", events[1].CommandLine);
        Assert.Equal(TerminalShellIntegrationEventKind.CommandFinished, events[2].Kind);
        Assert.Equal(0, events[2].ExitCode);
    }

    [Theory]
    [InlineData(TerminalShellIntegrationBootstrapShell.Bash, "__royalterminal_preexec", "PROMPT_COMMAND")]
    [InlineData(TerminalShellIntegrationBootstrapShell.Zsh, "add-zsh-hook preexec", "add-zsh-hook precmd")]
    [InlineData(TerminalShellIntegrationBootstrapShell.Fish, "fish_preexec", "fish_prompt")]
    [InlineData(TerminalShellIntegrationBootstrapShell.PowerShell, "function global:prompt", "__RoyalTerminalOriginalPrompt")]
    public void BootstrapBuilder_GeneratesOptInShellHooks(
        TerminalShellIntegrationBootstrapShell shell,
        string expectedCommandMarker,
        string expectedPromptMarker)
    {
        string? script = TerminalShellIntegrationBootstrapBuilder.Build(
            new TerminalShellIntegrationBootstrapOptions(shell));

        Assert.NotNull(script);
        Assert.Contains(expectedCommandMarker, script, StringComparison.Ordinal);
        Assert.Contains(expectedPromptMarker, script, StringComparison.Ordinal);
        Assert.Contains("]7;", script, StringComparison.Ordinal);
        Assert.Contains("]133;", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Tab", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BootstrapBuilder_BashCapturesOneHistoryLine_AndUsesByteLocaleForUrlEncode()
    {
        string? script = TerminalShellIntegrationBootstrapBuilder.Build(
            new TerminalShellIntegrationBootstrapOptions(TerminalShellIntegrationBootstrapShell.Bash));

        Assert.NotNull(script);
        Assert.Contains("local LC_ALL=C", script, StringComparison.Ordinal);
        Assert.Contains("history 1", script, StringComparison.Ordinal);
        Assert.Contains("__ROYALTERMINAL_LAST_HISTORY_ID", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BootstrapBuilder_ZshUsesByteLocaleForUrlEncode()
    {
        string? script = TerminalShellIntegrationBootstrapBuilder.Build(
            new TerminalShellIntegrationBootstrapOptions(TerminalShellIntegrationBootstrapShell.Zsh));

        Assert.NotNull(script);
        Assert.Contains("local LC_ALL=C", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BootstrapBuilder_PowerShellEmitsCommandStartFromReadLine()
    {
        string? script = TerminalShellIntegrationBootstrapBuilder.Build(
            new TerminalShellIntegrationBootstrapOptions(TerminalShellIntegrationBootstrapShell.PowerShell));

        Assert.NotNull(script);
        Assert.Contains("function global:PSConsoleHostReadLine", script, StringComparison.Ordinal);
        Assert.Contains("]133;C;cmdline_url=", script, StringComparison.Ordinal);
        Assert.Contains("[Uri]::EscapeDataString($rtLine)", script, StringComparison.Ordinal);
    }

    [Fact]
    public void BootstrapBuilder_ReturnsNull_WhenNoEventsRequested()
    {
        string? script = TerminalShellIntegrationBootstrapBuilder.Build(
            new TerminalShellIntegrationBootstrapOptions(TerminalShellIntegrationBootstrapShell.Bash)
            {
                EmitWorkingDirectory = false,
                EmitSemanticPrompt = false,
            });

        Assert.Null(script);
    }
}
