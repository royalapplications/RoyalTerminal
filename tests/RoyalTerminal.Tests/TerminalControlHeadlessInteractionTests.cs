// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — Headless interaction tests for TerminalControl.

using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;
using RoyalTerminal.Terminal.Transport.Ssh;
using RoyalTerminal.Terminal.Transport.Ssh.SshNet;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalControlHeadlessInteractionTests
{
    [AvaloniaFact]
    public async Task Headless_WindowResize_UpdatesGrid_AndPropagatesToTransport()
    {
        RecordingTransport transport = new();
        TerminalControl control = CreateControlWithTransport(transport);
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            Dispatcher.UIThread.RunJobs();

            int initialCols = control.Columns;
            int initialRows = control.Rows;
            int resizeCountBefore = transport.Resizes.Count;

            window.Width = 960;
            window.Height = 640;
            Dispatcher.UIThread.RunJobs();

            bool gridChanged = await WaitUntilAsync(
                () => control.Columns != initialCols || control.Rows != initialRows,
                TimeSpan.FromSeconds(2));
            Assert.True(gridChanged);

            bool resizeRecorded = await WaitUntilAsync(
                () => transport.Resizes.Count > resizeCountBefore,
                TimeSpan.FromSeconds(2));
            Assert.True(resizeRecorded);

            TerminalSessionDimensions lastResize = transport.Resizes[^1];
            Assert.Equal(control.Columns, lastResize.Columns);
            Assert.Equal(control.Rows, lastResize.Rows);
            Assert.True(lastResize.WidthPixels > 0);
            Assert.True(lastResize.HeightPixels > 0);
        }
        finally
        {
            window.Close();
            control.StopPty();
        }
    }

    [AvaloniaFact]
    public async Task Headless_KeyboardInput_SendsToTransportFallback()
    {
        RecordingTransport transport = new();
        TerminalControl control = CreateControlWithTransport(transport);
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            control.Focus();
            Dispatcher.UIThread.RunJobs();

            transport.Inputs.Clear();

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            bool enterSent = await WaitUntilAsync(
                () => transport.Inputs.Any(static input => input.Length == 1 && input[0] == (byte)'\r'),
                TimeSpan.FromSeconds(2));
            Assert.True(enterSent);

            transport.Inputs.Clear();
            window.KeyTextInput("x");

            bool textSent = await WaitUntilAsync(
                () => transport.Inputs.Any(static input => input.Length == 1 && input[0] == (byte)'x'),
                TimeSpan.FromSeconds(2));
            Assert.True(textSent);
        }
        finally
        {
            window.Close();
            control.StopPty();
        }
    }

    [AvaloniaFact]
    public async Task Headless_KeyboardInput_UsesAttachedEndpointSink()
    {
        RecordingEndpoint endpoint = new();
        TerminalControl control = new();
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);
            control.AttachEndpoint(endpoint);
            control.Focus();
            Dispatcher.UIThread.RunJobs();

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            bool keySent = await WaitUntilAsync(
                () => endpoint.KeyEvents.Count > 0,
                TimeSpan.FromSeconds(2));
            Assert.True(keySent);
            Assert.Contains(endpoint.KeyEvents, static key => key.KeyCode == (uint)Key.Return);

            window.KeyTextInput("z");
            bool textSent = await WaitUntilAsync(
                () => endpoint.TextInputs.Count > 0,
                TimeSpan.FromSeconds(2));
            Assert.True(textSent);
            Assert.Contains("z", endpoint.TextInputs);
        }
        finally
        {
            window.Close();
            control.DetachEndpoint();
        }
    }

    [AvaloniaFact]
    public async Task Headless_MouseInput_UsesAttachedEndpointSink()
    {
        RecordingEndpoint endpoint = new();
        TerminalControl control = new()
        {
            Width = 640,
            Height = 400,
        };
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);
            control.AttachEndpoint(endpoint);
            control.Focus();
            Dispatcher.UIThread.RunJobs();

            Point point = await GetInteractionPointAsync(control, window);

            RaiseMouseSequence(control, window, point);
            Dispatcher.UIThread.RunJobs();

            bool pointerSent = await WaitUntilAsync(
                () => endpoint.PointerEvents.Count >= 3,
                TimeSpan.FromSeconds(2));
            Assert.True(pointerSent);

            Assert.Contains(endpoint.PointerEvents, static evt =>
                evt.Kind == TerminalPointerEventKind.Button && evt.Action == TerminalInputAction.Press);
            Assert.Contains(endpoint.PointerEvents, static evt =>
                evt.Kind == TerminalPointerEventKind.Button && evt.Action == TerminalInputAction.Release);
            Assert.Contains(endpoint.PointerEvents, static evt =>
                evt.Kind == TerminalPointerEventKind.Scroll);
        }
        finally
        {
            window.Close();
            control.DetachEndpoint();
        }
    }

    [AvaloniaFact]
    public async Task Headless_MouseInput_EncodesToTransport_WhenMouseModeEnabled()
    {
        RecordingTransport transport = new();
        TerminalControl control = CreateControlWithTransport(transport);
        control.Width = 640;
        control.Height = 400;
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            control.WriteOutput("\x1b[?1002h\x1b[?1006h"u8);
            Dispatcher.UIThread.RunJobs();

            transport.Inputs.Clear();

            Point point = await GetInteractionPointAsync(control, window);
            RaiseMouseSequence(control, window, point);
            Dispatcher.UIThread.RunJobs();

            bool vtMouseSent = await WaitUntilAsync(
                () => transport.Inputs.Any(IsMouseProtocolInput),
                TimeSpan.FromSeconds(2));
            Assert.True(vtMouseSent);
        }
        finally
        {
            window.Close();
            control.StopPty();
        }
    }

    [AvaloniaFact]
    public async Task Headless_MouseInput_FromTopLevelRouting_EncodesToTransport_WhenMouseModeEnabled()
    {
        RecordingTransport transport = new();
        TerminalControl control = CreateControlWithTransport(transport);
        control.Width = 640;
        control.Height = 400;
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            control.WriteOutput("\x1b[?1002h\x1b[?1006h"u8);
            Dispatcher.UIThread.RunJobs();

            transport.Inputs.Clear();

            int pointerPressedCount = 0;
            control.AddHandler(
                InputElement.PointerPressedEvent,
                (_, _) => pointerPressedCount++,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
                handledEventsToo: true);

            Point point = await GetInteractionPointAsync(control, window);
            SendMouseSequenceViaTopLevel(window, point);
            Dispatcher.UIThread.RunJobs();

            bool vtMouseSent = await WaitUntilAsync(
                () => transport.Inputs.Any(IsMouseProtocolInput),
                TimeSpan.FromSeconds(2));
            Assert.True(vtMouseSent);
            Assert.True(pointerPressedCount > 0, "Top-level pointer input did not route to TerminalControl.");
        }
        finally
        {
            window.Close();
            control.StopPty();
        }
    }

    [AvaloniaFact]
    public async Task Headless_MouseInput_DoesNotEncodeToTransport_WhenMouseModeDisabled()
    {
        RecordingTransport transport = new();
        TerminalControl control = CreateControlWithTransport(transport);
        control.Width = 640;
        control.Height = 400;
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            Dispatcher.UIThread.RunJobs();

            transport.Inputs.Clear();

            Point point = await GetInteractionPointAsync(control, window);
            RaiseMouseSequence(control, window, point);
            Dispatcher.UIThread.RunJobs();

            // No mouse mode means no VT mouse encoding should be written to transport.
            Assert.DoesNotContain(transport.Inputs, IsMouseProtocolInput);
        }
        finally
        {
            window.Close();
            control.StopPty();
        }
    }

    [AvaloniaFact]
    public async Task Headless_MouseInput_EncodesWithPrimaryPointerFallback_WhenButtonMetadataMissing()
    {
        RecordingTransport transport = new();
        TerminalControl control = CreateControlWithTransport(transport);
        control.Width = 640;
        control.Height = 400;
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            control.WriteOutput("\x1b[?1002h\x1b[?1006h"u8);
            Dispatcher.UIThread.RunJobs();

            transport.Inputs.Clear();

            Point point = await GetInteractionPointAsync(control, window);
            RaisePrimaryPointerSequenceWithoutButtonMetadata(control, window, point);
            Dispatcher.UIThread.RunJobs();

            bool vtMouseSent = await WaitUntilAsync(
                () => transport.Inputs.Any(IsMouseProtocolInput),
                TimeSpan.FromSeconds(2));
            Assert.True(vtMouseSent);
        }
        finally
        {
            window.Close();
            control.StopPty();
        }
    }

    [AvaloniaFact]
    public async Task Headless_MouseInput_EncodesFromHandledEvents_WhenMouseModeEnabled()
    {
        RecordingTransport transport = new();
        TerminalControl control = CreateControlWithTransport(transport);
        control.Width = 640;
        control.Height = 400;
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        window.Show();

        try
        {
            await StabilizeWindowAsync(window, control);
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            control.WriteOutput("\x1b[?1000h"u8);
            Dispatcher.UIThread.RunJobs();

            transport.Inputs.Clear();

            Point point = await GetInteractionPointAsync(control, window);
            RaiseHandledMouseButtonSequence(control, window, point);
            Dispatcher.UIThread.RunJobs();

            bool vtMouseSent = await WaitUntilAsync(
                () => transport.Inputs.Any(IsMouseProtocolInput),
                TimeSpan.FromSeconds(2));
            Assert.True(vtMouseSent);
        }
        finally
        {
            window.Close();
            control.StopPty();
        }
    }

    private static TerminalControl CreateControlWithTransport(RecordingTransport transport)
    {
        CompositeTerminalTransportFactory factory = new(
            new ITerminalTransportProvider[]
            {
                new RecordingTransportProvider(transport),
            });

        return new TerminalControl(
            new TerminalSessionService(),
            new DefaultTerminalInputAdapter(),
            new DefaultTerminalSelectionService(),
            new DefaultTerminalScrollService(),
            new DefaultVtProcessorFactory(),
            new DefaultPtyFactory(),
            new NullSshCredentialProvider(),
            new RejectAllSshHostKeyValidator(),
            factory)
        {
            Background = Brushes.Transparent,
        };
    }

    private static bool IsMouseProtocolInput(byte[] input)
    {
        if (input.Length < 3 || input[0] != 0x1B || input[1] != (byte)'[')
        {
            return false;
        }

        string text = Encoding.ASCII.GetString(input);
        return text.StartsWith("\x1b[<", StringComparison.Ordinal) ||
               text.StartsWith("\x1b[M", StringComparison.Ordinal);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            if (predicate())
            {
                return true;
            }

            await Task.Delay(25);
        }

        Dispatcher.UIThread.RunJobs();
        return predicate();
    }

    private static async Task StabilizeWindowAsync(Window window, TerminalControl control)
    {
        Assert.True(window.IsVisible);
        bool arranged = await WaitUntilAsync(
            () => control.Bounds.Width > 1 && control.Bounds.Height > 1,
            TimeSpan.FromSeconds(2));
        Assert.True(arranged, $"Terminal control was not arranged in time. Bounds={control.Bounds}");

        control.Focus();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task<Point> GetInteractionPointAsync(TerminalControl control, Window window)
    {
        bool arranged = await WaitUntilAsync(
            () => control.Bounds.Width > 1 && control.Bounds.Height > 1,
            TimeSpan.FromSeconds(2));
        Assert.True(arranged, $"Terminal control was not arranged in time. Bounds={control.Bounds}");

        Point local = new(control.Bounds.Width * 0.5, control.Bounds.Height * 0.5);
        Point? translated = control.TranslatePoint(local, window);
        Assert.True(translated.HasValue, "Failed to translate interaction point to window coordinates.");
        return translated!.Value;
    }

    private static void RaiseMouseSequence(TerminalControl control, Window window, Point windowPoint)
    {
        Pointer pointer = new(id: 1, PointerType.Mouse, isPrimary: true);
        ulong timestamp = (ulong)Environment.TickCount64;

        PointerEventArgs move = new(
            InputElement.PointerMovedEvent,
            control,
            pointer,
            window,
            windowPoint,
            timestamp++,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            KeyModifiers.None);
        control.RaiseEvent(move);

        PointerPressedEventArgs press = new(
            control,
            pointer,
            window,
            windowPoint,
            timestamp++,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None,
            clickCount: 1);
        control.RaiseEvent(press);

        PointerReleasedEventArgs release = new(
            control,
            pointer,
            window,
            windowPoint,
            timestamp++,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None,
            MouseButton.Left);
        control.RaiseEvent(release);

        PointerWheelEventArgs wheel = new(
            control,
            pointer,
            window,
            windowPoint,
            timestamp,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            KeyModifiers.None,
            new Vector(0, -1));
        control.RaiseEvent(wheel);
    }

    private static void RaisePrimaryPointerSequenceWithoutButtonMetadata(
        TerminalControl control,
        Window window,
        Point windowPoint)
    {
        Pointer pointer = new(id: 2, PointerType.Touch, isPrimary: true);
        ulong timestamp = (ulong)Environment.TickCount64;

        PointerPressedEventArgs press = new(
            control,
            pointer,
            window,
            windowPoint,
            timestamp++,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            KeyModifiers.None,
            clickCount: 1);
        control.RaiseEvent(press);

        PointerReleasedEventArgs release = new(
            control,
            pointer,
            window,
            windowPoint,
            timestamp,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other),
            KeyModifiers.None,
            MouseButton.None);
        control.RaiseEvent(release);
    }

    private static void RaiseHandledMouseButtonSequence(TerminalControl control, Window window, Point windowPoint)
    {
        Pointer pointer = new(id: 3, PointerType.Mouse, isPrimary: true);
        ulong timestamp = (ulong)Environment.TickCount64;

        PointerPressedEventArgs press = new(
            control,
            pointer,
            window,
            windowPoint,
            timestamp++,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None,
            clickCount: 1);
        press.Handled = true;
        control.RaiseEvent(press);

        PointerReleasedEventArgs release = new(
            control,
            pointer,
            window,
            windowPoint,
            timestamp,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None,
            MouseButton.Left);
        release.Handled = true;
        control.RaiseEvent(release);
    }

    private static void SendMouseSequenceViaTopLevel(Window window, Point windowPoint)
    {
        window.MouseMove(windowPoint, RawInputModifiers.None);
        window.MouseDown(windowPoint, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        window.MouseUp(windowPoint, MouseButton.Left, RawInputModifiers.None);
        window.MouseWheel(windowPoint, new Vector(0, -1), RawInputModifiers.None);
    }

    private sealed record FakeTransportOptions(string TransportId) : ITerminalTransportOptions
    {
        public TerminalSessionDimensions Dimensions => new(80, 24, 640, 480);
    }

    private sealed class RecordingTransportProvider : ITerminalTransportProvider
    {
        private readonly RecordingTransport _transport;

        public RecordingTransportProvider(RecordingTransport transport)
        {
            _transport = transport;
        }

        public string TransportId => "fake";

        public bool CanHandle(ITerminalTransportOptions options)
        {
            return options is FakeTransportOptions;
        }

        public ITerminalTransport Create()
        {
            return _transport;
        }
    }

    private sealed class RecordingTransport : ITerminalTransport
    {
        public event Action<byte[], int>? DataReceived;
        public event Action<int>? ProcessExited;

        public bool IsRunning { get; private set; }
        public List<byte[]> Inputs { get; } = [];
        public List<TerminalSessionDimensions> Resizes { get; } = [];

        public ValueTask StartAsync(ITerminalTransportOptions options, CancellationToken cancellationToken = default)
        {
            _ = options;
            cancellationToken.ThrowIfCancellationRequested();
            IsRunning = true;
            return ValueTask.CompletedTask;
        }

        public void SendInput(ReadOnlySpan<byte> utf8)
        {
            Inputs.Add(utf8.ToArray());
            _ = DataReceived;
        }

        public void Resize(TerminalSessionDimensions dimensions)
        {
            Resizes.Add(dimensions);
        }

        public ValueTask StopAsync()
        {
            IsRunning = false;
            ProcessExited?.Invoke(0);
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            IsRunning = false;
        }
    }

    private sealed class RecordingEndpoint : ITerminalEndpoint, ITerminalInputSink
    {
        public List<TerminalKeyEvent> KeyEvents { get; } = [];
        public List<string> TextInputs { get; } = [];
        public List<TerminalPointerEvent> PointerEvents { get; } = [];

        public void SendText(ReadOnlySpan<byte> utf8)
        {
            _ = utf8;
        }

        public void SetFocus(bool focused)
        {
            _ = focused;
        }

        public void SetSize(int widthPx, int heightPx)
        {
            _ = widthPx;
            _ = heightPx;
        }

        public bool SendKey(TerminalKeyEvent keyEvent)
        {
            KeyEvents.Add(keyEvent);
            return true;
        }

        public bool SendText(string text)
        {
            TextInputs.Add(text);
            return true;
        }

        public bool SendPointer(TerminalPointerEvent pointerEvent)
        {
            PointerEvents.Add(pointerEvent);
            return true;
        }
    }
}
