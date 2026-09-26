// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Text.Json;
using RoyalTerminal.Avalonia.App.Services;
using RoyalTerminal.Avalonia.App.Services.Notifications;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class WindowsDesktopNotificationTests
{
    // Windows Terminal DesktopNotification.cpp owns Activated handlers and tag/group
    // replacement. The shared OSC 99 host uses the same live-session boundary.
    // Neither these tests nor the native fake-platform tests display real toasts.
    [Fact]
    public async Task WindowsCapabilitiesExposeUrgencyButNotUnreliableCloseOrNamedSoundSupport()
    {
        Transport transport = new();
        await using NativeDesktopNotificationBackend backend = new(() => transport, NativeNotificationPlatform.Windows);
        await backend.InitializeAsync(default);
        Assert.True(backend.Capabilities.HasFlag(TerminalNotificationCapabilities.Display));
        Assert.True(backend.Capabilities.HasFlag(TerminalNotificationCapabilities.Buttons));
        Assert.True(backend.Capabilities.HasFlag(TerminalNotificationCapabilities.Icons));
        Assert.True(backend.Capabilities.HasFlag(TerminalNotificationCapabilities.Urgency));
        Assert.False(backend.Capabilities.HasFlag(TerminalNotificationCapabilities.CloseEvents));
        Assert.False(backend.Capabilities.HasFlag(TerminalNotificationCapabilities.NamedSounds));
        Assert.False(backend.Capabilities.HasFlag(TerminalNotificationCapabilities.Focus)); // UI facade owns focus.
    }

    [Fact]
    public async Task ButtonIndicesCannotExceedTheFiveButtonsActuallySubmittedToWindows()
    {
        Transport transport = new();
        await using NativeDesktopNotificationBackend backend = new(() => transport, NativeNotificationPlatform.Windows);
        await backend.InitializeAsync(default);
        TerminalNotificationRequest request = Request() with { Buttons = ["one", "two", "three", "four", "five", "six"] };
        TaskCompletionSource<TerminalNotificationFeedback> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await backend.ShowAsync(request, value => result.TrySetResult(value), default);
        Assert.Equal(5, transport.Sent.Single().Buttons.Length);
        transport.Activate(request.Token, 6);
        transport.Activate(request.Token, 5);
        Assert.Equal(new TerminalNotificationFeedback(TerminalNotificationEvent.Activated, 5), await result.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void PlatformPayloadPoliciesPreserveLiteralTextAndDoNotMixMacOsEmptyBodyWorkaround()
    {
        TerminalNotificationRequest request = Request() with
        { Title = "<unsafe>&\"%", Body = "", Sound = "../sound.wav", IconNames = ["../image", "info"], Buttons = ["a", "b", "c", "d", "e", "f"] };
        NativeNotificationCommand windows = NativeDesktopNotificationBackend.Convert(request, NativeNotificationPlatform.Windows);
        NativeNotificationCommand mac = NativeDesktopNotificationBackend.Convert(request, NativeNotificationPlatform.MacOS);
        Assert.Equal(request.Title, windows.Title);
        Assert.Equal(string.Empty, windows.Body);
        Assert.Equal(" ", mac.Body);
        Assert.Equal("system", windows.Sound);
        Assert.Equal(5, windows.Buttons.Length);
        Assert.Equal(6, mac.Buttons.Length);
        Assert.Equal(request.IconNames, windows.Icons); // The native fixed namespace lookup rejects paths.
        Assert.Null(windows.Image);
    }

    [Theory]
    [InlineData("--royalterminal-notification", true)]
    [InlineData("--royalterminal-notification:1", true)]
    [InlineData("--royalterminal-notification:5", true)]
    [InlineData("--royalterminal-notification:0", false)]
    [InlineData("--royalterminal-notification:6", false)]
    [InlineData("--royalterminal-notification:10", false)]
    [InlineData("--royalterminal-notification:1;command", false)]
    [InlineData("--royalterminal-notification other", false)]
    [InlineData("--ROYALTERMINAL-NOTIFICATION", false)]
    [InlineData("command", false)]
    public void SecondaryLaunchOnlyAcceptsTheExactInertSentinel(string argument, bool expected)
        => Assert.Equal(expected, TerminalNotificationLaunch.IsInertActivation([argument]));

    [Fact]
    public void OrdinaryStartupAndMultipleArgumentsRemainUntouched()
    {
        Assert.False(TerminalNotificationLaunch.IsInertActivation([]));
        Assert.False(TerminalNotificationLaunch.IsInertActivation(["--royalterminal-notification", "command"]));
        Assert.False(TerminalNotificationLaunch.IsInertActivation([null!]));
    }

    private static TerminalNotificationRequest Request() => new(Guid.NewGuid(), null, "title", "body", null, [], [], default, [], "system", 1, -1);
    private sealed class Transport : INativeNotificationTransport
    {
        private readonly ConcurrentQueue<byte[]> _states = new();
        internal readonly List<NativeNotificationCommand> Sent = new();
        internal Transport() => State([]);
        public void Send(ReadOnlySpan<byte> bytes)
        {
            NativeNotificationCommand command = JsonSerializer.Deserialize(bytes, NativeNotificationJsonContext.Default.NativeNotificationCommand)!;
            Sent.Add(command);
            State([new() { Kind = "operation", Sequence = command.Sequence, Success = true }]);
        }
        public byte[]? Poll() => _states.TryDequeue(out byte[]? state) ? state : null;
        public void Dispose() { }
        internal void Activate(Guid token, int button) => State([new() { Kind = "activated", Token = token.ToString("N"), Button = button }]);
        private void State(NativeNotificationEvent[] events) => _states.Enqueue(JsonSerializer.SerializeToUtf8Bytes(
            new NativeNotificationState { Ready = true, Capabilities = int.MaxValue, Events = events }, NativeNotificationJsonContext.Default.NativeNotificationState));
    }
}
