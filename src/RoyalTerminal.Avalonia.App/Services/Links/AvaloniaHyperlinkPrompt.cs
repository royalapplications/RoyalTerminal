// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.App.ViewModels;
using RoyalTerminal.Avalonia.App.Views;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.App.Services.Links;

internal sealed class AvaloniaHyperlinkPrompt(Window owner) : ITerminalHyperlinkPrompt
{
    public async ValueTask<bool> ShowAsync(TerminalHyperlinkRequest request, string? handler,
        CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();
        if (!owner.IsVisible) return false;
        using HyperlinkPromptViewModel model = new(request, handler);
        HyperlinkPromptWindow dialog = new() { DataContext = model };
        using CompositeDisposable bindings = new();
        bindings.Add(model.OpenCommand.Subscribe(accepted => dialog.Close(accepted)));
        bindings.Add(model.CancelCommand.Subscribe(accepted => dialog.Close(accepted)));
        bindings.Add(model.CopyTargetInteraction.RegisterHandler(async context =>
        {
            // Copy may outlive the dialog while the OS owns its request. Absorb
            // clipboard failures here, including after command disposal.
            try
            {
                if (!cancellationToken.IsCancellationRequested && owner.Clipboard is { } clipboard)
                    await clipboard.SetTextAsync(context.Input);
            }
            catch { /* Copy failure is never permission to open the target. */ }
            context.SetOutput(Unit.Default);
        }));
        using CancellationTokenRegistration registration = cancellationToken.Register(
            () => Dispatcher.UIThread.Post(() => { if (dialog.IsVisible) dialog.Close(false); }));
        cancellationToken.ThrowIfCancellationRequested();
        bool accepted = await dialog.ShowDialog<bool>(owner);
        return accepted && !cancellationToken.IsCancellationRequested;
    }
}

internal sealed class AvaloniaHyperlinkLauncher(Window owner) : ITerminalHyperlinkLauncher
{
    public async ValueTask LaunchAsync(Uri uri, CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();
        if (owner.IsVisible) await owner.Launcher.LaunchUriAsync(uri);
    }
}
