// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.App.ViewModels;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ProgramStatusViewModelTests
{
    [Fact]
    public void StatusShowsSourceAndMostUrgentRecordWhileRetainingAllDetails()
    {
        ProgramStatusViewModel model = new();
        Assert.False(model.HasStatus);
        model.Update("Deploy tab", [
            new(TerminalProgramStatusState.Working, "us", Progress: 42, App: "deploy"),
            new(TerminalProgramStatusState.Blocked, "eu", TerminalProgramStatusKind.Permission, App: "deploy", Message: "Apply?"),
            new(TerminalProgramStatusState.Done, "test", App: "cargo")]);
        Assert.True(model.HasStatus);
        Assert.Equal("Deploy tab / deploy: eu: Waiting for approval — Apply?", model.Summary);
        Assert.Contains("Working 42%", model.Details);
        Assert.Contains("cargo: test: Done", model.Details);
        model.Update("Another tab", []);
        Assert.False(model.HasStatus);
        Assert.Empty(model.Summary);
        Assert.Empty(model.Details);
    }

    [Theory]
    [InlineData(TerminalProgramStatusKind.Auth, "Waiting for authentication")]
    [InlineData(TerminalProgramStatusKind.Question, "Waiting for an answer")]
    [InlineData(TerminalProgramStatusKind.None, "Waiting for you")]
    public void DisplayEscapesInvisibleTextWithoutTreatingItAsMarkup(TerminalProgramStatusKind kind, string state)
    {
        ProgramStatusViewModel model = new();
        model.Update("Session\u202e", [new(TerminalProgramStatusState.Blocked, Kind: kind,
            Title: "A\u200bB", Message: "<b>plain</b>\u202e")]);
        Assert.Contains("Session\\u{202E}", model.Summary);
        Assert.Contains("A\\u{200B}B", model.Summary);
        Assert.Contains(state, model.Summary);
        Assert.Contains("<b>plain</b>\\u{202E}", model.Summary);
        Assert.DoesNotContain('\u202e', model.Details);
    }
}
