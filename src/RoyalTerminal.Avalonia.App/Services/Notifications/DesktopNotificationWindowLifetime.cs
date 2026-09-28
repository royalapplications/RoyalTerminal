// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia.Controls;
using Avalonia.Threading;

namespace RoyalTerminal.Avalonia.App.Services.Notifications;

// Finish owned native cleanup before the final window closes and the desktop
// lifetime can end the process. Never synchronously block the UI dispatcher.
internal sealed class DesktopNotificationWindowLifetime : IDisposable
{
    private readonly Window _window;
    private readonly Func<DesktopNotificationService?> _service;
    private readonly Action _disableHosts;
    private bool _closing, _disposed;

    internal DesktopNotificationWindowLifetime(Window window, Func<DesktopNotificationService?> service, Action disableHosts)
    {
        _window = window; _service = service; _disableHosts = disableHosts;
        window.Closing += Closing;
    }

    private void Closing(object? sender, WindowClosingEventArgs args)
    {
        if (args.Cancel || _disposed || _service() is not { } service || service.Completion.IsCompleted) return;
        args.Cancel = true;
        if (_closing) return;
        _closing = true;
        _disableHosts();
        service.Dispose();
        _ = CompleteCloseAsync(service);
    }

    private async Task CompleteCloseAsync(DesktopNotificationService service)
    {
        try { await service.Completion; }
        catch (Exception) { }
        finally
        {
            // Completion may already be synchronous inside Closing. Re-enter
            // Close only after Avalonia has unwound that original event.
            Dispatcher.UIThread.Post(() =>
            {
                _closing = false;
                if (!_disposed) _window.Close();
            });
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _window.Closing -= Closing;
    }
}
