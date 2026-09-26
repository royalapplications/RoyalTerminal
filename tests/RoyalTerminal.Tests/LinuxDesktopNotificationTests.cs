// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Threading.Channels;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.App.Services.Notifications;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class LinuxDesktopNotificationTests
{
    // Ghostty GTK presents per-surface notifications through GNotification;
    // WT has no OSC 99 dispatcher, while xterm.js delegates custom OSCs to hosts.
    // Follow the freedesktop desktop contract for the Linux delivery layer:
    // https://specifications.freedesktop.org/notification/latest/protocol.html
    // No real desktop bus is contacted by these unit/headless tests.
    [Fact]
    public async Task NegotiationAndPayloadPreservePlainTextAndAdvertiseOnlySupportedFeatures()
    {
        FakeConnection bus = new() { ServerCapabilities = ["body", "body-markup", "actions", "sound"] };
        await using LinuxDesktopNotificationBackend backend = new(() => bus);
        await backend.InitializeAsync(default);
        Assert.True(backend.Capabilities.HasFlag(TerminalNotificationCapabilities.Activation));
        Assert.True(backend.Capabilities.HasFlag(TerminalNotificationCapabilities.Sound));
        Assert.False(backend.Capabilities.HasFlag(TerminalNotificationCapabilities.Icons));
        Assert.False(backend.Capabilities.HasFlag(TerminalNotificationCapabilities.NamedSounds));
        TerminalNotificationRequest request = Request() with
        { Title = "<plain>", Body = "<b>& a link</b>", Buttons = ["Yes", "No"], IconNames = ["/tmp/untrusted", "warn"], Sound = "silent", Urgency = 2, ExpireMilliseconds = 0 };
        List<TerminalNotificationFeedback> events = new();
        await backend.ShowAsync(request, events.Add, default);
        FreedesktopNotification sent = Assert.Single(bus.Requests);
        Assert.Equal("<plain>", sent.Title);
        Assert.Equal("&lt;b&gt;&amp; a link&lt;/b&gt;", sent.Body);
        Assert.Equal(new[] { "default", "Open terminal", "1", "Yes", "2", "No" }, sent.Actions);
        Assert.Equal("dialog-warning", sent.Icon);
        Assert.True(sent.Silent);
        Assert.Equal(0, sent.Expiry);
        Assert.Equal((byte)2, sent.Urgency);
        bus.Invoke(1, "2");
        Assert.Equal(new TerminalNotificationFeedback(TerminalNotificationEvent.Activated, 2), Assert.Single(events));
        bus.Invoke(1, "999"); bus.Invoke(1, "untrusted command");
        Assert.Single(events);
        await backend.CloseAsync(request.Token, default);
        Assert.Equal(1u, Assert.Single(bus.ClosedIds));
        bus.Invoke(1, "default");
        Assert.Single(events);
    }

    [Fact]
    public async Task LimitedServerRetainsBodyInSummaryAndOmitsUnsupportedActions()
    {
        FakeConnection bus = new() { ServerCapabilities = [] };
        await using LinuxDesktopNotificationBackend backend = new(() => bus);
        await backend.InitializeAsync(default);
        await backend.ShowAsync(Request() with { Title = "title", Body = "body", Buttons = ["action"] }, _ => { }, default);
        FreedesktopNotification sent = Assert.Single(bus.Requests);
        Assert.Equal("title — body", sent.Title);
        Assert.Empty(sent.Body);
        Assert.Empty(sent.Actions);
        Assert.False(backend.Capabilities.HasFlag(TerminalNotificationCapabilities.Activation));
        Assert.False(backend.Capabilities.HasFlag(TerminalNotificationCapabilities.Sound));
    }

    [Fact]
    public async Task ReplacementsUseServerIdAndDisposalClosesOnlyCurrentRevisions()
    {
        FakeConnection bus = new();
        LinuxDesktopNotificationBackend backend = new(() => bus);
        await backend.InitializeAsync(default);
        TerminalNotificationRequest first = Request(), second = Request() with { ReplacesToken = first.Token };
        List<TerminalNotificationFeedback> old = new(), current = new();
        await backend.ShowAsync(first, old.Add, default);
        await backend.ShowAsync(second, current.Add, default);
        Assert.Equal(1u, bus.Requests[1].ReplacesId);
        await backend.CloseAsync(first.Token, default);
        Assert.Empty(bus.ClosedIds);
        bus.Invoke(1, "default");
        Assert.Empty(old);
        Assert.Single(current);
        await backend.DisposeAsync();
        Assert.Equal(1u, Assert.Single(bus.ClosedIds));
        Assert.True(bus.Disposed);
        bus.Invoke(1, "default");
        Assert.Single(current);
    }

    [Fact]
    public async Task EarlySignalsAreAppliedAfterReplyAndOldDaemonIdsNeverReachNewGeneration()
    {
        FakeConnection first = new() { BeforeReply = bus => bus.Dismiss(1) }, second = new();
        Queue<FakeConnection> connections = new([first, second]);
        await using LinuxDesktopNotificationBackend backend = new(() => connections.Dequeue());
        await backend.InitializeAsync(default);
        List<TerminalNotificationFeedback> early = new();
        await backend.ShowAsync(Request(), early.Add, default);
        Assert.Equal(TerminalNotificationEvent.Closed, Assert.Single(early).Event);
        first.BeforeReply = null;
        List<TerminalNotificationFeedback> failed = new();
        await backend.ShowAsync(Request(), failed.Add, default);
        first.Disconnect();
        Assert.Equal(TerminalNotificationCapabilities.None, backend.Capabilities);
        Assert.Equal(TerminalNotificationEvent.Failed, Assert.Single(failed).Event);
        await backend.InitializeAsync(default);
        List<TerminalNotificationFeedback> current = new();
        await backend.ShowAsync(Request(), current.Add, default);
        first.Invoke(1, "default"); first.Dismiss(1);
        Assert.Empty(current);
        second.Invoke(1, "default");
        Assert.Equal(TerminalNotificationEvent.Activated, Assert.Single(current).Event);
    }

    [Fact]
    public async Task CloseDuringInFlightShowIsNotLostAndReplacementRequestsAreOrdered()
    {
        FakeBackend backend = new() { HoldShows = true };
        using DesktopNotificationService service = new(backend);
        await service.Ready.WaitAsync(TimeSpan.FromSeconds(5));
        TerminalNotificationRequest first = Request();
        Assert.True(service.Show(first, _ => { }));
        await backend.Shows.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        service.Close(first.Token);
        Assert.False(service.IsAlive(first.Token));
        backend.Release.TrySetResult();
        Assert.Equal(first.Token, await backend.Closes.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        TerminalNotificationRequest old = Request(), replacement = Request() with { ReplacesToken = old.Token };
        Assert.True(service.Show(old, _ => { }));
        Assert.True(service.Show(replacement, _ => { }));
        Assert.Equal(old.Token, (await backend.Shows.Reader.ReadAsync()).Token);
        Assert.Equal(replacement.Token, (await backend.Shows.Reader.ReadAsync()).Token);
        service.Dispose();
        await service.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(backend.Disposed);
    }

    [Fact]
    public async Task SaturationCannotDropCloseAndDisposeOwnsOutstandingDelivery()
    {
        FakeBackend backend = new() { HoldShows = true };
        using DesktopNotificationService service = new(backend);
        await service.Ready.WaitAsync(TimeSpan.FromSeconds(5));
        TerminalNotificationRequest first = Request();
        Assert.True(service.Show(first, _ => { }));
        await backend.Shows.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        for (int i = 1; i < 128; i++) Assert.True(service.Show(Request(), _ => { }));
        Assert.False(service.Show(Request(), _ => { }));
        service.Close(first.Token);
        service.Dispose();
        Assert.False(service.Completion.IsCompleted);
        Assert.Equal(TerminalNotificationCapabilities.None, service.Capabilities);
        backend.Release.TrySetResult();
        await service.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains(first.Token, backend.DisposedTokens);
        Assert.False(service.IsAlive(first.Token));
        Assert.False(service.Show(Request(), _ => { }));
    }

    [Fact]
    public async Task BackendFailureAndActivationUpdateAliveOnlyAfterProtocolFeedback()
    {
        FakeBackend backend = new();
        using DesktopNotificationService service = new(backend);
        await service.Ready.WaitAsync(TimeSpan.FromSeconds(5));
        TerminalNotificationRequest request = Request();
        bool aliveDuringFeedback = false;
        TaskCompletionSource feedback = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(service.Show(request, value => { aliveDuringFeedback = service.IsAlive(request.Token); feedback.TrySetResult(); }));
        await backend.Shows.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        backend.Send(request.Token, new(TerminalNotificationEvent.Activated));
        await feedback.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(aliveDuringFeedback);
        Assert.False(service.IsAlive(request.Token));
        service.Dispose();
        await service.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ImagesDecodeFirstFrameToStraightRgbaAndRejectOversizeHeaders()
    {
        using SKBitmap bitmap = new(new SKImageInfo(2, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.SetPixel(0, 0, new SKColor(200, 100, 50, 128));
        bitmap.SetPixel(1, 0, SKColors.Blue);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        byte[] bytes = encoded.ToArray();
        NotificationImage decoded = Assert.IsType<NotificationImage>(NotificationImageDecoder.Decode(bytes));
        Assert.Equal((2, 1), (decoded.Width, decoded.Height));
        Assert.Equal(8, decoded.Rgba.Length);
        Assert.InRange(decoded.Rgba[0], (byte)199, (byte)201);
        Assert.Equal((byte)128, decoded.Rgba[3]);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), uint.MaxValue);
        Assert.Null(NotificationImageDecoder.Decode(bytes));
        Assert.Null(NotificationImageDecoder.Decode(new byte[1024 * 1024 + 1]));
        Assert.Null(NotificationImageDecoder.Decode("not an image"u8.ToArray()));
    }

    [AvaloniaFact]
    public async Task PaneHostCachesVisibilityAndCancelsQueuedFocusAcrossSessionReset()
    {
        FakeBackend backend = new();
        using DesktopNotificationService service = new(backend);
        await service.Ready.WaitAsync(TimeSpan.FromSeconds(5));
        TerminalControl control = new() { VtProcessorPreference = VtProcessorPreference.Managed };
        Border parent = new() { Child = control };
        Window window = new() { Content = parent };
        int focusCalls = 0;
        using DesktopNotificationHost host = new(service, window, control, () => focusCalls++);
        control.NotificationHost = host;
        window.Show(); window.Activate(); control.Focus();
        bool visible = host.IsVisible;
        parent.IsVisible = false;
        Assert.False(host.IsVisible);
        Assert.False(host.IsFocused);
        parent.IsVisible = true;
        Assert.Equal(visible, host.IsVisible);
        control.WriteOutput("\x1b]99;i=job;a title\x1b\\"u8);
        await backend.Shows.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        host.Focus();
        control.StopPty(); // Disposes the protocol and invalidates its queued focus.
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, focusCalls);
        host.Focus();
        host.Dispose();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, focusCalls);
        window.Close();
        control.ActiveVtProcessor!.Dispose();
        service.Dispose();
        await service.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [AvaloniaFact]
    public async Task WindowCloseWaitsAsynchronouslyForOwnedNativeCleanup()
    {
        FakeBackend backend = new() { HoldShows = true };
        using DesktopNotificationService service = new(backend);
        await service.Ready.WaitAsync(TimeSpan.FromSeconds(5));
        Window window = new();
        int disabled = 0;
        using DesktopNotificationWindowLifetime lifetime = new(window, () => service, () => disabled++);
        window.Show();
        Assert.True(service.Show(Request(), _ => { }));
        await backend.Shows.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        window.Close();
        Assert.Equal(1, disabled);
        Assert.False(closed.Task.IsCompleted);
        Assert.True(window.IsVisible);
        backend.Release.TrySetResult();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(backend.Disposed);
        Assert.True(service.Completion.IsCompletedSuccessfully);
    }

    private static TerminalNotificationRequest Request() => new(Guid.NewGuid(), null,
        "title", "body", null, [], [], ReadOnlyMemory<byte>.Empty, [], "system", 1, -1);

    private sealed class FakeConnection : IFreedesktopNotificationConnection
    {
        public event Action<uint, string>? ActionInvoked;
        public event Action<uint>? Closed;
        public event Action? Disconnected;
        internal string[] ServerCapabilities = ["body", "actions", "sound"];
        internal List<FreedesktopNotification> Requests = new();
        internal List<uint> ClosedIds = new();
        internal Action<FakeConnection>? BeforeReply;
        internal bool Disposed;
        private uint _next;
        public Task<string[]> ConnectAsync(CancellationToken cancellationToken) => Task.FromResult(ServerCapabilities);
        public Task<uint> NotifyAsync(FreedesktopNotification notification, CancellationToken cancellationToken)
        {
            Requests.Add(notification);
            uint id = notification.ReplacesId != 0 ? notification.ReplacesId : ++_next;
            BeforeReply?.Invoke(this);
            return Task.FromResult(id);
        }
        public Task CloseAsync(uint id, CancellationToken cancellationToken) { ClosedIds.Add(id); return Task.CompletedTask; }
        public void Dispose() => Disposed = true;
        internal void Invoke(uint id, string action) => ActionInvoked?.Invoke(id, action);
        internal void Dismiss(uint id) => Closed?.Invoke(id);
        internal void Disconnect() => Disconnected?.Invoke();
    }

    private sealed class FakeBackend : IDesktopNotificationBackend
    {
        public TerminalNotificationCapabilities Capabilities { get; private set; }
        internal Channel<TerminalNotificationRequest> Shows = Channel.CreateUnbounded<TerminalNotificationRequest>();
        internal Channel<Guid> Closes = Channel.CreateUnbounded<Guid>();
        internal TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ConcurrentDictionary<Guid, Action<TerminalNotificationFeedback>> Callbacks = new();
        internal ConcurrentDictionary<Guid, bool> Alive = new();
        internal List<Guid> DisposedTokens = new();
        internal bool HoldShows, Disposed;
        public ValueTask InitializeAsync(CancellationToken cancellationToken)
        {
            Capabilities = TerminalNotificationCapabilities.Display | TerminalNotificationCapabilities.Activation | TerminalNotificationCapabilities.Close | TerminalNotificationCapabilities.Alive;
            return ValueTask.CompletedTask;
        }
        public async ValueTask ShowAsync(TerminalNotificationRequest request, Action<TerminalNotificationFeedback> feedback, CancellationToken cancellationToken)
        {
            Callbacks[request.Token] = feedback;
            Shows.Writer.TryWrite(request);
            if (HoldShows) await Release.Task.WaitAsync(cancellationToken);
            if (request.ReplacesToken is Guid old) Alive.TryRemove(old, out _);
            Alive[request.Token] = true;
        }
        public ValueTask CloseAsync(Guid token, CancellationToken cancellationToken)
        { Alive.TryRemove(token, out _); Closes.Writer.TryWrite(token); return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync()
        { DisposedTokens.AddRange(Alive.Keys); Alive.Clear(); Disposed = true; Capabilities = 0; return ValueTask.CompletedTask; }
        internal void Send(Guid token, TerminalNotificationFeedback feedback) => Callbacks[token](feedback);
    }
}
