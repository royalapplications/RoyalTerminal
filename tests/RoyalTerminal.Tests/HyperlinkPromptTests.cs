// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.App.Services.Links;
using RoyalTerminal.Avalonia.App.ViewModels;
using RoyalTerminal.Avalonia.App.Views;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class HyperlinkPromptTests
{
    [Theory]
    [InlineData("custom:execute", "Editor", true)]
    [InlineData("custom:execute", null, false)]
    [InlineData("custom:execute", " ", false)]
    [InlineData("file:///tmp/payload.command", "Editor", false)]
    [InlineData("https://example.com/a\u202Eb", "Editor", false)]
    [InlineData("https://example.com", "Editor", false)]
    public async Task ViewModelOnlyEnablesOpenForCustomSchemeWithHandler(string target, string? handler, bool canOpen)
    {
        using HyperlinkPromptViewModel model = new(TerminalHyperlinkSafety.Classify(target), handler);
        Assert.Equal(canOpen, model.CanOpen);
        Assert.Equal(canOpen, await model.OpenCommand.CanExecute.FirstAsync());
        Assert.Equal(TerminalHyperlinkSafety.SanitizeDisplay(target), model.Target);
    }

    [Fact]
    public async Task CopyIsExplicitAndPreservesOriginalWhileHandlerAndTargetAreEscaped()
    {
        const string target = "custom:a\u202Eb";
        using HyperlinkPromptViewModel model = new(TerminalHyperlinkSafety.Classify(target), "Editor\n\u200Bname");
        Assert.Equal("custom:a\\u{202E}b", model.Target);
        Assert.Equal("Editor\\u{A}\\u{200B}name", model.Handler);
        string? copied = null;
        using IDisposable registration = model.CopyTargetInteraction.RegisterHandler(context =>
        {
            copied = context.Input;
            context.SetOutput(Unit.Default);
        });
        Assert.Null(copied);
        await model.CopyCommand.Execute().ToTask();
        Assert.Equal(target, copied);
        Assert.False(await model.CancelCommand.Execute().ToTask());
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompiledDialogUsesBoundCommandsForExplicitDecision(bool accept)
    {
        Window owner = new() { Width = 700, Height = 500 };
        owner.Show();
        try
        {
            AvaloniaHyperlinkPrompt prompt = new(owner);
            Task<bool> pending = prompt.ShowAsync(TerminalHyperlinkSafety.Classify("custom:execute"),
                "Editor", CancellationToken.None).AsTask();
            Dispatcher.UIThread.RunJobs();
            HyperlinkPromptWindow dialog = Assert.IsType<HyperlinkPromptWindow>(Assert.Single(owner.OwnedWindows));
            Assert.Equal("custom:execute", dialog.FindControl<SelectableTextBlock>("TargetText")!.Text);
            Button button = dialog.FindControl<Button>(accept ? "OpenLinkButton" : "CancelButton")!;
            Assert.True(button.IsVisible);
            Assert.True(button.IsEnabled);
            button.Focus();
            dialog.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            dialog.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(accept, await pending.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Empty(owner.OwnedWindows);
        }
        finally { owner.Close(); await HeadlessTerminalTestCleanup.DrainDispatcherAsync(); }
    }

    [AvaloniaFact]
    public async Task BlockedDialogHasNoOpenButtonAndCancellationClosesOwnedWindow()
    {
        Window owner = new() { Width = 700, Height = 500 };
        owner.Show();
        using CancellationTokenSource cancellation = new();
        try
        {
            AvaloniaHyperlinkPrompt prompt = new(owner);
            Task<bool> pending = prompt.ShowAsync(TerminalHyperlinkSafety.Classify("file:///tmp/payload.command"),
                "Editor", cancellation.Token).AsTask();
            Dispatcher.UIThread.RunJobs();
            Window dialog = Assert.Single(owner.OwnedWindows);
            Assert.False(dialog.FindControl<Button>("OpenLinkButton")!.IsVisible);
            Assert.True(dialog.FindControl<Button>("CopyTargetButton")!.IsEnabled);
            cancellation.Cancel();
            Dispatcher.UIThread.RunJobs();
            Assert.False(await pending.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Empty(owner.OwnedWindows);
        }
        finally { owner.Close(); await HeadlessTerminalTestCleanup.DrainDispatcherAsync(); }
    }

    [AvaloniaFact]
    public async Task InvisibleOwnerCannotPresentConfirmation()
    {
        Window owner = new();
        Assert.False(await new AvaloniaHyperlinkPrompt(owner).ShowAsync(
            TerminalHyperlinkSafety.Classify("custom:execute"), "Editor", CancellationToken.None));
        Assert.Empty(owner.OwnedWindows);
    }

    [Theory]
    [InlineData(TerminalHyperlinkDenialReason.UnsafeFile, "execute code")]
    [InlineData(TerminalHyperlinkDenialReason.InaccessibleFile, "does not exist")]
    [InlineData(TerminalHyperlinkDenialReason.FileInspectionUnavailable, "could not inspect")]
    public async Task InspectedFileDenialShowsResolvedPreviewButCopiesOriginal(TerminalHyperlinkDenialReason reason, string explanation)
    {
        const string original = "file:///alias/notes.txt";
        TerminalHyperlinkRequest request = TerminalHyperlinkSafety.Classify(original) with
        {
            Disposition = TerminalHyperlinkDisposition.Deny, DenialReason = reason,
            DisplayText = "/resolved/hidden\u202E.command", Uri = null,
        };
        using HyperlinkPromptViewModel model = new(request, "Editor");
        Assert.False(model.CanOpen);
        Assert.Equal("/resolved/hidden\\u{202E}.command", model.Target);
        Assert.Contains(explanation, model.Message, StringComparison.Ordinal);
        string? copied = null;
        using IDisposable registration = model.CopyTargetInteraction.RegisterHandler(context =>
        {
            copied = context.Input;
            context.SetOutput(Unit.Default);
        });
        await model.CopyCommand.Execute().ToTask();
        Assert.Equal(original, copied);
    }
}
