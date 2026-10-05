// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalNotificationHostTests
{
    [AvaloniaFact]
    public void ControlBindsBeforeProcessorCreationAndRebindsWithoutOwningHost()
    {
        Host first = new(), second = new();
        TerminalControl control = new() { VtProcessorPreference = VtProcessorPreference.Managed, NotificationHost = first };
        control.WriteOutput("\x1b]99;;first\x1b\\"u8);
        Assert.Single(first.Shown);
        control.NotificationHost = second;
        Assert.Contains(first.Shown[0].Token, first.Closed);
        Assert.Same(second, ((ITerminalNotificationSource)control.ActiveVtProcessor!).NotificationHost);
        control.WriteOutput("\x1b]99;;second\x1b\\"u8);
        Assert.Single(second.Shown);
        control.StopPty();
        Assert.Contains(second.Shown[0].Token, second.Closed);
        control.WriteOutput("\x1b]99;;another session\x1b\\"u8);
        Assert.Equal(2, second.Shown.Count);
        control.ActiveVtProcessor!.Dispose();
        Assert.Contains(second.Shown[1].Token, second.Closed);
    }

    [AvaloniaFact]
    public void DetachmentDisablesProtocolAndClosesOwnedNotificationsUntilReattached()
    {
        Host host = new();
        TerminalControl control = new() { VtProcessorPreference = VtProcessorPreference.Managed, NotificationHost = host };
        Window window = new() { Content = control };
        window.Show();
        control.WriteOutput("\x1b]99;;first\x1b\\"u8);
        Assert.Single(host.Shown);
        window.Content = null;
        Assert.Contains(host.Shown[0].Token, host.Closed);
        control.WriteOutput("\x1b]99;;detached\x1b\\"u8);
        Assert.Single(host.Shown);
        window.Content = control;
        control.WriteOutput("\x1b]99;;reattached\x1b\\"u8);
        Assert.Equal(2, host.Shown.Count);
        window.Close();
        control.ActiveVtProcessor!.Dispose();
    }

    private sealed class Host : ITerminalNotificationHost
    {
        public TerminalNotificationCapabilities Capabilities => TerminalNotificationCapabilities.Display | TerminalNotificationCapabilities.Close;
        public bool IsFocused => false;
        public bool IsVisible => true;
        internal List<TerminalNotificationRequest> Shown { get; } = new();
        internal List<Guid> Closed { get; } = new();
        public bool Show(TerminalNotificationRequest request, Action<TerminalNotificationFeedback> feedback) { Shown.Add(request); return true; }
        public void Close(Guid token) => Closed.Add(token);
        public bool IsAlive(Guid token) => false;
        public void Focus() { }
    }
}
