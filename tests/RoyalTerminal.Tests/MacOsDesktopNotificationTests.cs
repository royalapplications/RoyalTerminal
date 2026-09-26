// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using RoyalTerminal.Avalonia.App.Services.Notifications;
using RoyalTerminal.Terminal;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class MacOsDesktopNotificationTests
{
    // Ghostty's macOS surface owns UUID requests and drops late deliveries on
    // teardown. Kitty's macOS presenter cannot promise every close event or named
    // sounds. This fake transport never contacts UNUserNotificationCenter.
    [Fact]
    public async Task NegotiationMasksFeaturesNotSupportedByTheMacOsPresenter()
    {
        Transport transport = new();
        await using MacOsDesktopNotificationBackend backend = new(() => transport);
        await backend.InitializeAsync(default);
        Assert.True(backend.Capabilities.HasFlag(TerminalNotificationCapabilities.Display));
        Assert.True(backend.Capabilities.HasFlag(TerminalNotificationCapabilities.Icons));
        Assert.True(backend.Capabilities.HasFlag(TerminalNotificationCapabilities.Buttons));
        Assert.False(backend.Capabilities.HasFlag(TerminalNotificationCapabilities.CloseEvents));
        Assert.False(backend.Capabilities.HasFlag(TerminalNotificationCapabilities.NamedSounds));
        Assert.False(backend.Capabilities.HasFlag(TerminalNotificationCapabilities.Urgency));
        Assert.False(backend.Capabilities.HasFlag(TerminalNotificationCapabilities.Focus));
    }

    [Fact]
    public async Task ReplacementsUseRevisionTokensAndIgnoreStaleOrInvalidActions()
    {
        Transport transport = new();
        await using MacOsDesktopNotificationBackend backend = new(() => transport);
        await backend.InitializeAsync(default);
        TerminalNotificationRequest first = Request(), second = Request() with { ReplacesToken = first.Token, Buttons = ["Yes", "No"] };
        List<TerminalNotificationFeedback> old = new();
        TaskCompletionSource<TerminalNotificationFeedback> activated = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await backend.ShowAsync(first, old.Add, default);
        await backend.ShowAsync(second, value => activated.TrySetResult(value), default);
        MacNotificationCommand sent = transport.Sent[1];
        Assert.Equal(second.Token.ToString("N"), sent.Token);
        Assert.Equal(first.Token.ToString("N"), sent.Replaces);
        transport.Event(first.Token, "activated", 0);
        transport.Event(second.Token, "activated", 3);
        transport.Event(second.Token, "unrecognized", 0);
        transport.Event(second.Token, "activated", 2);
        TerminalNotificationFeedback result = await activated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new TerminalNotificationFeedback(TerminalNotificationEvent.Activated, 2), result);
        Assert.Empty(old);
    }

    [Fact]
    public async Task CancellationInterruptsPendingPermissionAndQueuesNativeCleanup()
    {
        Transport transport = new() { HoldShows = true };
        await using MacOsDesktopNotificationBackend backend = new(() => transport);
        await backend.InitializeAsync(default);
        TerminalNotificationRequest request = Request();
        Task show = backend.ShowAsync(request, _ => { }, default).AsTask();
        await transport.Commands.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        backend.CancelPending();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => show);
        MacNotificationCommand close = await transport.Commands.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("close", close.Op);
        Assert.Equal(request.Token.ToString("N"), close.Token);
        await backend.DisposeAsync();
        Assert.True(transport.Disposed);
        Assert.Equal("stop", transport.Sent[^1].Op);
        Assert.Equal(TerminalNotificationCapabilities.None, backend.Capabilities);
    }

    [Fact]
    public async Task ServiceShutdownCancelsMacPermissionWithoutWaitingForTheUser()
    {
        Transport transport = new() { HoldShows = true };
        MacOsDesktopNotificationBackend backend = new(() => transport);
        using DesktopNotificationService service = new(backend);
        await service.Ready.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(service.Show(Request(), _ => { }));
        await transport.Commands.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        service.Dispose();
        await service.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(transport.Disposed);
        Assert.Equal(TerminalNotificationCapabilities.None, service.Capabilities);
    }

    [Fact]
    public async Task ClosingOnePaneCancelsItsPermissionWaitWithoutStoppingTheService()
    {
        Transport transport = new() { HoldShows = true };
        using DesktopNotificationService service = new(new MacOsDesktopNotificationBackend(() => transport));
        await service.Ready.WaitAsync(TimeSpan.FromSeconds(5));
        TerminalNotificationRequest first = Request();
        Assert.True(service.Show(first, _ => { }));
        MacNotificationCommand show = await transport.Commands.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("show", show.Op);
        service.Close(first.Token);
        MacNotificationCommand close = await transport.Commands.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("close", close.Op);
        Assert.Equal(first.Token.ToString("N"), close.Token);
        Assert.False(service.IsAlive(first.Token));
        Assert.False(transport.Disposed);
        transport.HoldShows = false;
        TerminalNotificationRequest second = Request();
        Assert.True(service.Show(second, _ => { }));
        MacNotificationCommand next = await transport.Commands.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("show", next.Op);
        Assert.Equal(second.Token.ToString("N"), next.Token);
        service.Dispose();
        await service.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(transport.Disposed);
    }

    [Fact]
    public async Task ReinitializationOwnsOldPollerAndCannotRouteOldCenterEvents()
    {
        Transport first = new(), second = new();
        Queue<Transport> transports = new([first, second]);
        await using MacOsDesktopNotificationBackend backend = new(() => transports.Dequeue());
        await backend.InitializeAsync(default);
        TerminalNotificationRequest previous = Request();
        List<TerminalNotificationFeedback> stale = new();
        await backend.ShowAsync(previous, stale.Add, default);
        await backend.InitializeAsync(default);
        Assert.True(first.Disposed);
        TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TerminalNotificationRequest current = Request();
        await backend.ShowAsync(current, _ => done.TrySetResult(), default);
        first.Event(previous.Token, "activated", 0);
        second.Event(current.Token, "closed", 0);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(stale);
    }

    [Fact]
    public async Task NativeFailureRejectsDeliveryAndClosesItsToken()
    {
        Transport transport = new() { FailShows = true };
        await using MacOsDesktopNotificationBackend backend = new(() => transport);
        await backend.InitializeAsync(default);
        TerminalNotificationRequest request = Request();
        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.ShowAsync(request, _ => { }, default).AsTask());
        Assert.Equal("close", transport.Sent[^1].Op);
        Assert.Equal(request.Token.ToString("N"), transport.Sent[^1].Token);
    }

    [Fact]
    public async Task PermissionDeniedOrDelegateUnavailableDoesNotAdvertiseDisplay()
    {
        Transport transport = new(initialCapabilities: 0);
        await using MacOsDesktopNotificationBackend backend = new(() => transport);
        await backend.InitializeAsync(default);
        Assert.Equal(TerminalNotificationCapabilities.None, backend.Capabilities);
        Assert.Empty(transport.Sent); // Initialization cannot issue show/permission commands.
    }

    [Fact]
    public void PayloadRemainsPlainTextUsesSourceGeneratedJsonAndDoesNotAcceptPaths()
    {
        TerminalNotificationRequest request = Request() with
        { Title = "<title> 100%", Body = "", ApplicationName = "com.example.app", IconNames = ["/tmp/not-a-path", "info"], Sound = "unknown", Urgency = 2 };
        MacNotificationCommand command = MacOsDesktopNotificationBackend.Convert(request);
        Assert.Equal("<title> 100%", command.Title);
        Assert.Equal(" ", command.Body); // Avoid macOS replacing an empty body with the title.
        Assert.Equal("system", command.Sound);
        Assert.Equal(2, command.Urgency);
        Assert.Null(command.Image);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(command, MacNotificationJsonContext.Default.MacNotificationCommand);
        MacNotificationCommand restored = JsonSerializer.Deserialize(json, MacNotificationJsonContext.Default.MacNotificationCommand)!;
        Assert.Equal(command.Title, restored.Title);
        Assert.Equal(command.Icons, restored.Icons); // Native lookup rejects paths rather than opening them.
        Assert.DoesNotContain("path", restored.Op, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SuppliedImagesAreDecodedWithinBoundsAndReencodedAsSingleFramePng()
    {
        using SKBitmap bitmap = new(2, 1);
        bitmap.Erase(SKColors.Red);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData encoded = image.Encode(SKEncodedImageFormat.Jpeg, 95);
        MacNotificationCommand command = MacOsDesktopNotificationBackend.Convert(Request() with { IconData = encoded.ToArray() });
        Assert.NotNull(command.Image);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, command.Image![..8]);
        NotificationImage decoded = Assert.IsType<NotificationImage>(NotificationImageDecoder.Decode(command.Image));
        Assert.Equal((2, 1), (decoded.Width, decoded.Height));
        Assert.Null(MacOsDesktopNotificationBackend.Convert(Request() with { IconData = new byte[1024 * 1024 + 1] }).Image);
    }

    private static TerminalNotificationRequest Request() => new(Guid.NewGuid(), null, "title", "body", null, [], [], default, [], "system", 1, -1);

    private sealed class Transport : IMacNotificationTransport
    {
        private readonly ConcurrentQueue<byte[]> _states = new();
        internal readonly List<MacNotificationCommand> Sent = new();
        internal readonly Channel<MacNotificationCommand> Commands = Channel.CreateUnbounded<MacNotificationCommand>();
        internal bool HoldShows, FailShows, Disposed;
        internal Transport(int initialCapabilities = int.MaxValue) => State(new() { Ready = true, Capabilities = initialCapabilities });
        public void Send(ReadOnlySpan<byte> json)
        {
            MacNotificationCommand command = JsonSerializer.Deserialize(json, MacNotificationJsonContext.Default.MacNotificationCommand)!;
            Sent.Add(command); Commands.Writer.TryWrite(command);
            if (command.Op == "show" && HoldShows) return;
            State(new() { Ready = true, Capabilities = int.MaxValue, Events = [new() { Kind = "operation", Sequence = command.Sequence, Success = !(command.Op == "show" && FailShows) }] });
        }
        public byte[]? Poll() => _states.TryDequeue(out byte[]? value) ? value : null;
        public void Dispose() => Disposed = true;
        internal void Event(Guid token, string kind, int button)
            => State(new() { Ready = true, Capabilities = int.MaxValue, Events = [new() { Kind = kind, Token = token.ToString("N"), Button = button }] });
        private void State(MacNotificationState state) => _states.Enqueue(JsonSerializer.SerializeToUtf8Bytes(state, MacNotificationJsonContext.Default.MacNotificationState));
    }
}
