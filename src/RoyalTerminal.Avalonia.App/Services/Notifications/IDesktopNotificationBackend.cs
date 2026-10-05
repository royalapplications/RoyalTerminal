// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.App.Services.Notifications;

// Owned by DesktopNotificationService. Operations are serialized on its async
// worker, never on the UI/VT thread. Events may arrive from a transport thread.
internal interface IDesktopNotificationBackend : IAsyncDisposable
{
    TerminalNotificationCapabilities Capabilities { get; }
    ValueTask InitializeAsync(CancellationToken cancellationToken);
    ValueTask ShowAsync(TerminalNotificationRequest request, Action<TerminalNotificationFeedback> feedback, CancellationToken cancellationToken);
    ValueTask CloseAsync(Guid token, CancellationToken cancellationToken);
}

// Backends with native-owned late cleanup can honor ShowAsync's token when a
// pane closes. CancelPending interrupts all managed delivery waits during window
// shutdown; it must not answer or dismiss the user's OS authorization dialog.
internal interface IDesktopNotificationCancellation
{
    void CancelPending();
}
