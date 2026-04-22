// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — Avalonia headless tests for TerminalControl.

using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Avalonia.Scrolling;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Theming;
using RoyalTerminal.Terminal.Services;
using RoyalTerminal.Terminal.Transport.Ssh;
using RoyalTerminal.Terminal.Transport.Ssh.SshNet;
using Xunit;

namespace RoyalTerminal.Tests;

/// <summary>
/// Headless Avalonia tests for the terminal control.
/// Uses [AvaloniaFact] to run on the UI thread.
/// </summary>
public class TerminalControlTests
{
    [AvaloniaFact]
    public void Control_CanBeInstantiated()
    {
        var control = new TerminalControl();
        Assert.NotNull(control);
    }

    [AvaloniaFact]
    public void Control_DefaultProperties_HaveCorrectValues()
    {
        var control = new TerminalControl();

        var expectedFont =
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Menlo" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "DejaVu Sans Mono" :
            "Consolas";
        Assert.Equal(expectedFont, control.FontFamilyName);
        Assert.Equal(TerminalFontSource.System, control.FontSource);
        Assert.Equal(string.Empty, control.FontFilePath);
        Assert.Equal(14.0, control.TerminalFontSize);
        Assert.Equal(80, control.Columns);
        Assert.Equal(24, control.Rows);
        Assert.Equal(10_000, control.ScrollbackLimit);
        Assert.True(control.AutoScroll);
        Assert.True(control.ReflowOnResize);
    }

    [AvaloniaFact]
    public void Control_DefaultSshHostKeyValidator_UsesKnownHostsValidator()
    {
        var control = new TerminalControl();

        Assert.IsType<KnownHostsSshHostKeyValidator>(control.SshHostKeyValidator);
    }

    [AvaloniaFact]
    public void Control_StyledProperties_CanBeSetAndRead()
    {
        var control = new TerminalControl
        {
            FontFamilyName = "JetBrains Mono",
            FontSource = TerminalFontSource.System,
            FontFilePath = string.Empty,
            TerminalFontSize = 16.0,
            Columns = 120,
            Rows = 40,
            ScrollbackLimit = 50_000,
            AutoScroll = false,
            ReflowOnResize = false,
        };

        Assert.Equal("JetBrains Mono", control.FontFamilyName);
        Assert.Equal(TerminalFontSource.System, control.FontSource);
        Assert.Equal(string.Empty, control.FontFilePath);
        Assert.Equal(16.0, control.TerminalFontSize);
        Assert.Equal(120, control.Columns);
        Assert.Equal(40, control.Rows);
        Assert.Equal(50_000, control.ScrollbackLimit);
        Assert.False(control.AutoScroll);
        Assert.False(control.ReflowOnResize);
    }

    [AvaloniaFact]
    public void Control_FileFontSourceWithMissingPath_FallsBackWithoutThrowing()
    {
        var control = new TerminalControl
        {
            FontFamilyName = "Missing Font Family",
            FontFilePath = Path.Combine(Path.GetTempPath(), "missing-royalterminal-font.ttf"),
            FontSource = TerminalFontSource.File,
        };

        Assert.Equal(TerminalFontSource.File, control.FontSource);
        Assert.EndsWith("missing-royalterminal-font.ttf", control.FontFilePath, StringComparison.Ordinal);
        Assert.NotNull(control.Renderer);
        Assert.True(control.Renderer!.CellWidth > 0);
        Assert.True(control.Renderer.CellHeight > 0);
    }

    [AvaloniaFact]
    public void Control_DefaultColors_AreSet()
    {
        var control = new TerminalControl();

        Assert.Equal(Color.FromRgb(0xD4, 0xD4, 0xD4), control.DefaultForeground);
        Assert.Equal(Color.FromRgb(0x1E, 0x1E, 0x1E), control.DefaultBackground);
    }

    [AvaloniaFact]
    public void Control_Colors_CanBeChanged()
    {
        var control = new TerminalControl
        {
            DefaultForeground = Colors.White,
            DefaultBackground = Colors.Black,
        };

        Assert.Equal(Colors.White, control.DefaultForeground);
        Assert.Equal(Colors.Black, control.DefaultBackground);
    }

    [AvaloniaFact]
    public void Control_IsFocusable()
    {
        var control = new TerminalControl();
        Assert.True(control.Focusable);
    }

    [AvaloniaFact]
    public void Control_Screen_IsInitialized()
    {
        var control = new TerminalControl();
        Assert.NotNull(control.Screen);
        Assert.Equal(80, control.Screen!.Columns);
        Assert.Equal(24, control.Screen.ViewportRows);
    }

    [AvaloniaFact]
    public void Control_Renderer_IsInitialized()
    {
        var control = new TerminalControl();
        Assert.NotNull(control.Renderer);
    }

    [AvaloniaFact]
    public void Control_ScrollData_IsInitialized()
    {
        var control = new TerminalControl();
        Assert.NotNull(control.ScrollData);
    }

    [AvaloniaFact]
    public void Control_Endpoint_IsNullByDefault()
    {
        var control = new TerminalControl();
        Assert.Null(control.Endpoint);
    }

    [AvaloniaFact]
    public void Control_DetachEndpoint_DoesNotThrow()
    {
        var control = new TerminalControl();
        control.DetachEndpoint(); // Should not throw even when no endpoint attached
    }

    [AvaloniaFact]
    public void Control_AttachEndpoint_AssignsSessionEndpoint()
    {
        var control = new TerminalControl();
        var endpoint = new FakeEndpoint();

        control.AttachEndpoint(endpoint);

        Assert.Same(endpoint, control.Endpoint);
    }

    [AvaloniaFact]
    public async Task Control_StartSessionAsync_SetsActiveTransportAndStopClearsState()
    {
        FakeTransport transport = new();
        CompositeTerminalTransportFactory factory = new(
            new ITerminalTransportProvider[]
            {
                new FakeTransportProvider(transport),
            });

        TerminalControl control = new(
            new TerminalSessionService(),
            new DefaultTerminalInputAdapter(),
            new DefaultTerminalSelectionService(),
            new DefaultTerminalScrollService(),
            new DefaultVtProcessorFactory(),
            new DefaultPtyFactory(),
            new NullSshCredentialProvider(),
            new RejectAllSshHostKeyValidator(),
            factory);

        await control.StartSessionAsync(new FakeTransportOptions("fake"));

        Assert.True(control.HasActiveSession);
        Assert.Equal("fake", control.ActiveTransportId);

        control.StopPty();

        Assert.False(control.HasActiveSession);
        Assert.Null(control.ActiveTransportId);
        Assert.True(transport.StopCalled);
        await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
    }

    [AvaloniaFact]
    public async Task Control_TransportExit_RaisesProcessExited()
    {
        FakeTransport transport = new();
        CompositeTerminalTransportFactory factory = new(
            new ITerminalTransportProvider[]
            {
                new FakeTransportProvider(transport),
            });

        TerminalControl control = new(
            new TerminalSessionService(),
            new DefaultTerminalInputAdapter(),
            new DefaultTerminalSelectionService(),
            new DefaultTerminalScrollService(),
            new DefaultVtProcessorFactory(),
            new DefaultPtyFactory(),
            new NullSshCredentialProvider(),
            new RejectAllSshHostKeyValidator(),
            factory);

        int? exitCode = null;
        control.ProcessExited += (_, code) => exitCode = code;

        try
        {
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            transport.RaiseProcessExited(17);
            HeadlessTerminalTestCleanup.RunDispatcherJobs();

            Assert.Equal(17, exitCode);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupControlAsync(control);
        }
    }

    [AvaloniaFact]
    public async Task Control_TransportOutputBurst_DrainsAllChunksInOrder()
    {
        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed);

        object sync = new();
        List<int> received = [];
        control.DataReceived += (_, args) =>
        {
            ReadOnlySpan<byte> payload = args.Data.Span;
            if (payload.Length <= 1 || payload[0] != (byte)'#')
            {
                return;
            }

            string token = Encoding.UTF8.GetString(payload[1..]);
            if (int.TryParse(token, out int sequence))
            {
                lock (sync)
                {
                    received.Add(sequence);
                }
            }
        };

        const int chunkCount = 512;
        try
        {
            await control.StartSessionAsync(new FakeTransportOptions("fake"));

            await Task.Run(() =>
            {
                for (int i = 0; i < chunkCount; i++)
                {
                    transport.RaiseData(Encoding.UTF8.GetBytes($"#{i}"));
                }
            });

            bool drained = await WaitUntilAsync(
                () =>
                {
                    lock (sync)
                    {
                        return received.Count == chunkCount;
                    }
                },
                TimeSpan.FromSeconds(5));

            Assert.True(drained, $"Expected {chunkCount} chunks to be drained from queued transport output.");
            lock (sync)
            {
                Assert.Equal(chunkCount, received.Count);
                for (int i = 0; i < chunkCount; i++)
                {
                    Assert.Equal(i, received[i]);
                }
            }
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupControlAsync(control);
        }
    }

    [AvaloniaFact]
    public async Task Control_StopPty_DrainsPendingOutput_AndNextSessionStartsClean()
    {
        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed);

        object sync = new();
        StringBuilder observed = new();
        control.DataReceived += (_, args) =>
        {
            lock (sync)
            {
                observed.Append(Encoding.UTF8.GetString(args.Data.Span));
            }
        };

        try
        {
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            transport.RaiseData(Encoding.UTF8.GetBytes("OLD-SESSION\n"));

            control.StopPty();

            bool oldSeen = await WaitUntilAsync(
                () =>
                {
                    lock (sync)
                    {
                        return observed.ToString().Contains("OLD-SESSION", StringComparison.Ordinal);
                    }
                },
                TimeSpan.FromSeconds(5));

            Assert.True(oldSeen, "Expected pending output to be drained when stopping the session.");

            lock (sync)
            {
                observed.Clear();
            }

            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            transport.RaiseData(Encoding.UTF8.GetBytes("NEW-SESSION\n"));

            bool newSeen = await WaitUntilAsync(
                () =>
                {
                    lock (sync)
                    {
                        return observed.ToString().Contains("NEW-SESSION", StringComparison.Ordinal);
                    }
                },
                TimeSpan.FromSeconds(5));

            Assert.True(newSeen, "Expected output from the restarted session.");

            string allOutput;
            lock (sync)
            {
                allOutput = observed.ToString();
            }

            Assert.DoesNotContain("OLD-SESSION", allOutput, StringComparison.Ordinal);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupControlAsync(control);
        }
    }

    [AvaloniaFact]
    public async Task Control_TransportOutputBurst_BatchesScrollPostProcessing()
    {
        FakeTransport transport = new();
        CountingTerminalScrollService scrollService = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed,
            scrollService);

        int receivedCount = 0;
        control.DataReceived += (_, _) => Interlocked.Increment(ref receivedCount);

        const int chunkCount = 512;
        try
        {
            await control.StartSessionAsync(new FakeTransportOptions("fake"));

            await Task.Run(() =>
            {
                for (int i = 0; i < chunkCount; i++)
                {
                    transport.RaiseData(Encoding.UTF8.GetBytes($"chunk-{i}\n"));
                }
            });

            bool drained = await WaitUntilAsync(
                () => Volatile.Read(ref receivedCount) == chunkCount,
                TimeSpan.FromSeconds(5));

            Assert.True(drained, $"Expected {chunkCount} chunks to drain from queued transport output.");
            Assert.True(scrollService.HandleOutputCallCount > 0);
            Assert.True(
                scrollService.HandleOutputCallCount < chunkCount,
                $"Expected batched post-processing. HandleOutputCallCount={scrollService.HandleOutputCallCount}, chunks={chunkCount}.");
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupControlAsync(control);
        }
    }

    [AvaloniaFact]
    public async Task Control_TransportOutputBurst_CoalescesScrollInvalidatedNotifications()
    {
        FakeTransport transport = new();
        CountingTerminalScrollService scrollService = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed,
            scrollService);

        int scrollInvalidatedCount = 0;
        ((ILogicalScrollable)control).ScrollInvalidated += (_, _) => Interlocked.Increment(ref scrollInvalidatedCount);

        const int chunkCount = 512;
        try
        {
            await control.StartSessionAsync(new FakeTransportOptions("fake"));

            await Task.Run(() =>
            {
                for (int i = 0; i < chunkCount; i++)
                {
                    transport.RaiseData(Encoding.UTF8.GetBytes($"chunk-{i}\n"));
                }
            });

            bool notificationsObserved = await WaitUntilAsync(
                () => scrollService.HandleOutputCallCount > 0 && Volatile.Read(ref scrollInvalidatedCount) > 0,
                TimeSpan.FromSeconds(5));

            Assert.True(notificationsObserved, "Expected transport output to trigger scroll invalidation notifications.");
            int observedScrollInvalidated = Volatile.Read(ref scrollInvalidatedCount);
            Assert.True(
                observedScrollInvalidated <= scrollService.HandleOutputCallCount,
                $"Expected scroll invalidation notifications to stay within output finalize batches. " +
                $"ScrollInvalidated={observedScrollInvalidated}, HandleOutput={scrollService.HandleOutputCallCount}.");
            Assert.True(
                observedScrollInvalidated < chunkCount,
                $"Expected scroll invalidation notifications to be coalesced beyond raw transport chunks. " +
                $"ScrollInvalidated={observedScrollInvalidated}, chunks={chunkCount}.");
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupControlAsync(control);
        }
    }

    [AvaloniaFact]
    public async Task Control_TransportOutputBurst_YieldsAcrossUiDispatches()
    {
        FakeTransport transport = new();
        CountingTerminalScrollService scrollService = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed,
            scrollService);

        int receivedCount = 0;
        control.DataReceived += (_, _) => Interlocked.Increment(ref receivedCount);

        const int chunkCount = 64;
        try
        {
            await control.StartSessionAsync(new FakeTransportOptions("fake"));

            await Task.Run(() =>
            {
                for (int i = 0; i < chunkCount; i++)
                {
                    transport.RaiseData(Encoding.UTF8.GetBytes($"yield-{i}\n"));
                }
            });

            bool drained = await WaitUntilAsync(
                () => Volatile.Read(ref receivedCount) == chunkCount,
                TimeSpan.FromSeconds(5));

            Assert.True(drained, $"Expected {chunkCount} chunks to drain from queued transport output.");
            Assert.True(
                scrollService.HandleOutputCallCount > 1,
                $"Expected output draining to yield across multiple UI callbacks. HandleOutputCallCount={scrollService.HandleOutputCallCount}.");
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupControlAsync(control);
        }
    }

    [AvaloniaFact]
    public async Task Control_TransportOutputBurst_BackpressuresProducer_WhenUiThreadCannotDrain()
    {
        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed);

        const int burstCount = 20;
        byte[] chunk = new byte[16 * 1024];
        Array.Fill(chunk, (byte)'x');

        try
        {
            await control.StartSessionAsync(new FakeTransportOptions("fake"));

            int sentCount = 0;
            Task producer = Task.Run(() =>
            {
                for (int i = 0; i < burstCount; i++)
                {
                    Interlocked.Increment(ref sentCount);
                    transport.RaiseData(chunk);
                }
            });

            Assert.True(
                SpinWait.SpinUntil(() => Volatile.Read(ref sentCount) > 0, millisecondsTimeout: 1000),
                "Expected producer to start sending transport chunks.");

            // Simulate the UI thread being occupied so queued output cannot drain immediately.
            Thread.Sleep(150);

            Assert.False(
                producer.IsCompleted,
                "Expected producer to block when pending output reaches backpressure watermark.");
            Assert.True(
                Volatile.Read(ref sentCount) < burstCount,
                "Expected producer to stall before sending the full burst while UI draining is blocked.");

            bool completedAfterDrain = await WaitUntilAsync(
                () => producer.IsCompleted,
                TimeSpan.FromSeconds(10));

            Assert.True(completedAfterDrain, "Expected producer to complete once UI drain catches up under the tighter managed output backlog limits.");
            await producer;
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupControlAsync(control);
        }
    }

    [AvaloniaFact]
    public async Task Control_StopPty_UnblocksProducer_WhenBackpressureWaitIsActive()
    {
        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed);

        const int burstCount = 20;
        byte[] chunk = new byte[16 * 1024];
        Array.Fill(chunk, (byte)'x');

        try
        {
            await control.StartSessionAsync(new FakeTransportOptions("fake"));

            int sentCount = 0;
            Task producer = Task.Run(() =>
            {
                for (int i = 0; i < burstCount; i++)
                {
                    Interlocked.Increment(ref sentCount);
                    transport.RaiseData(chunk);
                }
            });

            Assert.True(
                SpinWait.SpinUntil(() => Volatile.Read(ref sentCount) > 0, millisecondsTimeout: 1000),
                "Expected producer to start sending transport chunks.");

            Thread.Sleep(150);
            Assert.False(
                producer.IsCompleted,
                "Expected producer to be backpressured before StopPty.");

            Stopwatch stopLatency = Stopwatch.StartNew();
            control.StopPty();
            stopLatency.Stop();

            bool producerUnblocked = await Task.WhenAny(producer, Task.Delay(TimeSpan.FromSeconds(2))) == producer;
            Assert.True(producerUnblocked, "Expected StopPty to unblock any producer waiting in output backpressure.");
            Assert.True(
                stopLatency.Elapsed < TimeSpan.FromSeconds(3),
                $"Expected StopPty to complete promptly while producer is blocked. Elapsed={stopLatency.Elapsed}.");
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupControlAsync(control);
        }
    }

    [AvaloniaFact]
    public void Control_RequestClose_RaisesCloseRequested()
    {
        TerminalControl control = new();
        int closeCount = 0;
        control.CloseRequested += (_, _) => closeCount++;

        control.RequestClose();
        control.RequestClose();

        Assert.Equal(2, closeCount);
    }

    [AvaloniaFact]
    public void Control_HasSelection_ReflectsRendererSelectionState()
    {
        TerminalControl control = new();
        Assert.False(control.HasSelection);

        Assert.NotNull(control.Renderer);
        control.Renderer!.SelectionStart = (2, 3);
        control.Renderer.SelectionEnd = (8, 3);

        Assert.True(control.HasSelection);

        control.Renderer.SelectionEnd = null;
        Assert.False(control.HasSelection);
    }

    [AvaloniaFact]
    public void Control_SetContentScale_UsesScaleSinkWhenAvailable()
    {
        var control = new TerminalControl();
        var endpoint = new FakeEndpoint();
        control.AttachEndpoint(endpoint);

        control.SetContentScale(1.25, 1.5);

        Assert.Equal(1.25, endpoint.ScaleX);
        Assert.Equal(1.5, endpoint.ScaleY);
    }

    [AvaloniaFact]
    public void Control_VtProcessorPreferenceChange_RecreatesProcessor_WhenIdle()
    {
        TrackingVtProcessorFactory factory = new();
        TerminalControl control = new(
            new TerminalSessionService(),
            new DefaultTerminalInputAdapter(),
            new DefaultTerminalSelectionService(),
            new DefaultTerminalScrollService(),
            factory,
            new DefaultPtyFactory());

        Assert.Equal(VtProcessorPreference.Auto, factory.Preferences[0]);

        control.VtProcessorPreference = VtProcessorPreference.Managed;

        Assert.Equal(VtProcessorPreference.Managed, factory.Preferences[^1]);
        Assert.True(factory.CreateCallCount >= 2);
    }

    [AvaloniaFact]
    public void Control_RuntimeFontSizeChange_UpdatesRenderer()
    {
        var control = new TerminalControl();
        var originalRenderer = control.Renderer;

        control.TerminalFontSize = 18;

        Assert.NotNull(control.Renderer);
        Assert.NotSame(originalRenderer, control.Renderer);
        Assert.Equal(18f, control.Renderer!.FontSize);
    }

    [AvaloniaFact]
    public void Control_RuntimeFontFamilyChange_RecreatesRenderer()
    {
        var control = new TerminalControl();
        var originalRenderer = control.Renderer;

        control.FontFamilyName = "Courier New";

        Assert.NotNull(control.Renderer);
        Assert.NotSame(originalRenderer, control.Renderer);
    }

    [AvaloniaFact]
    public void Control_RuntimeColorChange_UpdatesScreenDefaults()
    {
        var control = new TerminalControl();
        Color foreground = Color.FromRgb(0x12, 0x34, 0x56);
        Color background = Color.FromRgb(0xAB, 0xCD, 0xEF);

        control.DefaultForeground = foreground;
        control.DefaultBackground = background;

        Assert.NotNull(control.Screen);
        Assert.Equal(ColorToArgb(foreground), control.Screen!.DefaultForeground);
        Assert.Equal(ColorToArgb(background), control.Screen.DefaultBackground);
    }

    [AvaloniaFact]
    public void Control_LegacyColorProperties_UpdateNeutralTheme()
    {
        var control = new TerminalControl();
        Color foreground = Color.FromRgb(0x22, 0x44, 0x66);
        Color background = Color.FromRgb(0x11, 0x33, 0x55);

        control.DefaultForeground = foreground;
        control.DefaultBackground = background;

        Assert.NotNull(control.Theme);
        Assert.Equal(ColorToArgb(foreground), control.Theme!.DefaultForeground);
        Assert.Equal(ColorToArgb(background), control.Theme.DefaultBackground);
    }

    [AvaloniaFact]
    public void Control_ApplyTheme_UpdatesLegacyDefaultProperties()
    {
        var control = new TerminalControl();
        TerminalTheme theme = TerminalTheme.Dark
            .WithDefaultForeground(0xFF102030u)
            .WithDefaultBackground(0xFF405060u)
            .WithCursorColor(0xFF708090u);

        control.ApplyTheme(theme);

        Assert.Equal(0xFF102030u, ColorToArgb(control.DefaultForeground));
        Assert.Equal(0xFF405060u, ColorToArgb(control.DefaultBackground));
        Assert.NotNull(control.Theme);
        Assert.Equal(0xFF708090u, control.Theme!.CursorColor);
    }

    [AvaloniaFact]
    public void Control_ApplyTheme_MarksRowsDirtyWithoutResizing()
    {
        TerminalControl control = new()
        {
            Columns = 80,
            Rows = 24,
            VtProcessorPreference = VtProcessorPreference.Managed,
        };

        Assert.NotNull(control.Screen);
        TerminalScreen screen = control.Screen!;
        int columnsBefore = control.Columns;
        int rowsBefore = control.Rows;

        for (int row = 0; row < screen.ViewportRows; row++)
        {
            screen.GetViewportRow(row).IsDirty = false;
        }

        Assert.False(screen.HasDirtyRows());

        TerminalTheme theme = TerminalTheme.Light
            .WithDefaultForeground(0xFF0F172Au)
            .WithDefaultBackground(0xFFE2E8F0u)
            .WithCursorColor(0xFF2563EBu);

        control.ApplyTheme(theme);

        Assert.Equal(columnsBefore, control.Columns);
        Assert.Equal(rowsBefore, control.Rows);
        Assert.Equal(theme.DefaultForeground, screen.DefaultForeground);
        Assert.Equal(theme.DefaultBackground, screen.DefaultBackground);
        Assert.True(screen.HasDirtyRows());
    }

    [AvaloniaFact]
    public void Control_ApplyTheme_WithCurrentTheme_ReappliesAndInvalidatesRows()
    {
        TerminalControl control = new()
        {
            Columns = 80,
            Rows = 24,
            VtProcessorPreference = VtProcessorPreference.Managed,
        };

        Assert.NotNull(control.Screen);
        Assert.NotNull(control.Theme);
        TerminalScreen screen = control.Screen!;
        TerminalTheme theme = control.Theme!;

        for (int row = 0; row < screen.ViewportRows; row++)
        {
            screen.GetViewportRow(row).IsDirty = false;
        }

        Assert.False(screen.HasDirtyRows());

        control.ApplyTheme(theme);

        Assert.True(screen.HasDirtyRows());
        Assert.Equal(theme.DefaultForeground, screen.DefaultForeground);
        Assert.Equal(theme.DefaultBackground, screen.DefaultBackground);
    }

    [AvaloniaFact]
    public void Control_ApplyTheme_PropagatesToThemeSinkProcessor()
    {
        ThemeTrackingVtProcessorFactory factory = new();
        TerminalControl control = new(
            new TerminalSessionService(),
            new DefaultTerminalInputAdapter(),
            new DefaultTerminalSelectionService(),
            new DefaultTerminalScrollService(),
            factory,
            new DefaultPtyFactory());

        TerminalTheme theme = TerminalTheme.Dark.WithDefaultForeground(0xFFAABBCCu);
        control.ApplyTheme(theme);

        Assert.NotNull(factory.LastProcessor);
        Assert.NotNull(factory.LastProcessor!.LastAppliedTheme);
        Assert.Equal(0xFFAABBCCu, factory.LastProcessor.LastAppliedTheme!.DefaultForeground);
    }

    [AvaloniaFact]
    public void Control_RuntimeGridChange_ResizesScreen()
    {
        var control = new TerminalControl();

        control.Columns = 132;
        control.Rows = 40;

        Assert.NotNull(control.Screen);
        Assert.Equal(132, control.Screen!.Columns);
        Assert.Equal(40, control.Screen.ViewportRows);
    }

    [AvaloniaFact]
    public void Control_Arrange_TinyBounds_DoesNotCollapseGrid()
    {
        TerminalControl control = new();

        control.Measure(new Size(960, 640));
        control.Arrange(new Rect(0, 0, 960, 640));

        Assert.NotNull(control.Screen);
        int columnsBefore = control.Screen!.Columns;
        int rowsBefore = control.Screen.ViewportRows;
        Assert.True(columnsBefore > 1);
        Assert.True(rowsBefore > 1);

        control.Measure(new Size(1, 1));
        control.Arrange(new Rect(0, 0, 1, 1));

        Assert.Equal(columnsBefore, control.Screen.Columns);
        Assert.Equal(rowsBefore, control.Screen.ViewportRows);
    }

    [AvaloniaFact]
    public void Control_HorizontalResizeShrink_ReflowsBufferedContent()
    {
        TerminalControl control = new()
        {
            Columns = 80,
            Rows = 24,
            VtProcessorPreference = VtProcessorPreference.Managed,
        };

        string line = "COLUMN-000-010-020-030-040-050-060-070-END";
        control.WriteOutput(Encoding.UTF8.GetBytes(line));

        Assert.True(ContainsScreenText(control, "070-END"));

        control.Columns = 32;

        Assert.Equal(32, control.Screen!.Columns);
        Assert.True(ContainsScreenText(control, "070-END"));

        control.Columns = 80;

        Assert.Equal(80, control.Screen.Columns);
        Assert.True(ContainsScreenText(control, "070-END"));
    }

    [AvaloniaFact]
    public void Control_HorizontalResizeShrinkWithoutReflow_HidesAndRestoresBufferedContent()
    {
        TerminalControl control = new()
        {
            Columns = 80,
            Rows = 24,
            ReflowOnResize = false,
            VtProcessorPreference = VtProcessorPreference.Managed,
        };

        string line = "COLUMN-000-010-020-030-040-050-060-070-END";
        control.WriteOutput(Encoding.UTF8.GetBytes(line));

        Assert.True(ContainsScreenText(control, "070-END"));

        control.Columns = 32;

        Assert.Equal(32, control.Screen!.Columns);
        Assert.False(ContainsScreenText(control, "070-END"));

        control.Columns = 80;

        Assert.Equal(80, control.Screen.Columns);
        Assert.True(ContainsScreenText(control, "070-END"));
    }

    [AvaloniaFact]
    public async Task Control_Arrange_PixelOnlyResize_DoesNotPropagateTransportResize()
    {
        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed);

        try
        {
            await control.StartSessionAsync(new FakeTransportOptions("fake"));

            control.Measure(new Size(960, 640));
            control.Arrange(new Rect(0, 0, 960, 640));
            HeadlessTerminalTestCleanup.RunDispatcherJobs();

            Assert.NotNull(control.Renderer);
            double cellWidth = control.Renderer!.CellWidth;
            double cellHeight = control.Renderer.CellHeight;
            int columnsBefore = control.Columns;
            int rowsBefore = control.Rows;
            int resizeCountBefore = transport.Resizes.Count;
            Size pixelOnlySize = new(
                (columnsBefore * cellWidth) + (cellWidth * 0.5),
                (rowsBefore * cellHeight) + (cellHeight * 0.5));

            control.Measure(pixelOnlySize);
            control.Arrange(new Rect(pixelOnlySize));
            HeadlessTerminalTestCleanup.RunDispatcherJobs();

            Assert.Equal(columnsBefore, control.Columns);
            Assert.Equal(rowsBefore, control.Rows);
            Assert.Equal(resizeCountBefore, transport.Resizes.Count);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupControlAsync(control);
        }
    }

    [AvaloniaFact]
    public void Control_Arrange_PixelOnlyResize_NotifiesVtProcessorWithUpdatedPixels()
    {
        ResizeTrackingVtProcessorFactory factory = new();
        TerminalControl control = new(
            new TerminalSessionService(),
            new DefaultTerminalInputAdapter(),
            new DefaultTerminalSelectionService(),
            new DefaultTerminalScrollService(),
            factory,
            new DefaultPtyFactory())
        {
            VtProcessorPreference = VtProcessorPreference.Managed,
        };

        control.Measure(new Size(960, 640));
        control.Arrange(new Rect(0, 0, 960, 640));
        HeadlessTerminalTestCleanup.RunDispatcherJobs();

        Assert.NotNull(factory.LastProcessor);
        ResizeTrackingVtProcessor processor = factory.LastProcessor!;
        int resizeCountBefore = processor.ResizeNotifications.Count;

        Assert.NotNull(control.Renderer);
        double cellWidth = control.Renderer!.CellWidth;
        double cellHeight = control.Renderer.CellHeight;
        int columnsBefore = control.Columns;
        int rowsBefore = control.Rows;
        Size pixelOnlySize = new(
            (columnsBefore * cellWidth) + (cellWidth * 0.5),
            (rowsBefore * cellHeight) + (cellHeight * 0.5));

        control.Measure(pixelOnlySize);
        control.Arrange(new Rect(pixelOnlySize));
        HeadlessTerminalTestCleanup.RunDispatcherJobs();

        Assert.Equal(columnsBefore, control.Columns);
        Assert.Equal(rowsBefore, control.Rows);
        Assert.True(processor.ResizeNotifications.Count > resizeCountBefore);

        TerminalSessionDimensions lastResize = processor.ResizeNotifications[^1];
        Assert.Equal(columnsBefore, lastResize.Columns);
        Assert.Equal(rowsBefore, lastResize.Rows);
        int expectedWidthPixels = Math.Max(1, (int)Math.Round(control.Bounds.Width));
        int expectedHeightPixels = Math.Max(1, (int)Math.Round(control.Bounds.Height));
        Assert.Equal(expectedWidthPixels, lastResize.WidthPixels);
        Assert.Equal(expectedHeightPixels, lastResize.HeightPixels);
    }

    [AvaloniaFact]
    public void Control_RuntimeAutoScrollEnable_ScrollsToBottom()
    {
        var control = new TerminalControl
        {
            AutoScroll = false,
        };

        Assert.NotNull(control.ScrollData);
        control.ScrollData!.CellHeight = 16;
        control.ScrollData.Viewport = 160;
        control.ScrollData.Extent = 960;
        control.ScrollData.Offset = 120;
        Assert.True(control.ScrollData.Offset < control.ScrollData.MaxOffset);

        control.AutoScroll = true;

        Assert.Equal(control.ScrollData.MaxOffset, control.ScrollData.Offset, precision: 6);
    }

    [AvaloniaFact]
    public void Control_InvalidateTerminal_DoesNotThrow()
    {
        var control = new TerminalControl();
        control.InvalidateTerminal();
    }

    [AvaloniaFact]
    public void Control_ScrollByRows_DoesNotThrow()
    {
        var control = new TerminalControl();
        control.ScrollByRows(5);
        control.ScrollByRows(-5);
    }

    [AvaloniaFact]
    public void Control_ScrollToBottom_DoesNotThrow()
    {
        var control = new TerminalControl();
        control.ScrollToBottom();
    }

    [AvaloniaFact]
    public void Control_ClearSelection_DoesNotThrow()
    {
        var control = new TerminalControl();
        control.ClearSelection();
    }

    [AvaloniaFact]
    public void Control_CanBeAddedToWindow()
    {
        var control = new TerminalControl();
        var window = new Window { Content = control };

        window.Show();

        Assert.NotNull(window.Content);
        Assert.IsType<TerminalControl>(window.Content);

        window.Close();
        HeadlessTerminalTestCleanup.RunDispatcherJobs();
    }

    [AvaloniaFact]
    public void Control_DataReceived_EventCanBeSubscribed()
    {
        var control = new TerminalControl();
        var received = false;

        control.DataReceived += (_, _) => received = true;

        // Write output triggers the event
        control.WriteOutput("Hello"u8);
        Assert.True(received);
    }

    [AvaloniaFact]
    public void Control_TerminalResized_EventCanBeSubscribed()
    {
        var control = new TerminalControl();
        int? newCols = null;
        int? newRows = null;

        control.TerminalResized += (_, args) =>
        {
            newCols = args.Columns;
            newRows = args.Rows;
        };

        // The resize event fires during layout when columns/rows change
        // We can't easily trigger that in headless, but we verify subscription works
        Assert.Null(newCols);
        Assert.Null(newRows);
    }

    [AvaloniaFact]
    public void Control_WriteOutput_UpdatesScreen()
    {
        var control = new TerminalControl();
        var eventFired = false;

        control.DataReceived += (_, args) =>
        {
            eventFired = true;
            Assert.True(args.Data.Length > 0);
        };

        control.WriteOutput("Test output"u8);
        Assert.True(eventFired);
    }

    [AvaloniaFact]
    public async Task Control_WriteOutput_FromBackgroundThread_IsQueuedToUiThread()
    {
        TerminalControl control = new()
        {
            VtProcessorPreference = VtProcessorPreference.Managed,
        };

        object sync = new();
        int receivedCount = 0;
        StringBuilder payloads = new();

        control.DataReceived += (_, args) =>
        {
            lock (sync)
            {
                receivedCount++;
                payloads.Append(Encoding.UTF8.GetString(args.Data.Span));
            }
        };

        const int writes = 64;
        await Task.Run(() =>
        {
            for (int i = 0; i < writes; i++)
            {
                control.WriteOutput(Encoding.UTF8.GetBytes($"bg-{i}\n"));
            }
        });

        bool drained = await WaitUntilAsync(
            () =>
            {
                lock (sync)
                {
                    return receivedCount == writes;
                }
            },
            TimeSpan.FromSeconds(5));

        Assert.True(drained, $"Expected {writes} queued background writes to drain on UI thread.");
        lock (sync)
        {
            Assert.Contains("bg-0", payloads.ToString(), StringComparison.Ordinal);
            Assert.Contains($"bg-{writes - 1}", payloads.ToString(), StringComparison.Ordinal);
        }
    }

    [AvaloniaFact]
    public async Task Control_ManagedTransportOutput_ParsesOffUiThread_WhileDataReceivedRemainsOnUiThread()
    {
        FakeTransport transport = new();
        ThreadTrackingVtProcessorFactory factory = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            factory,
            VtProcessorPreference.Managed);

        int uiThreadId = Environment.CurrentManagedThreadId;
        int? dataReceivedThreadId = null;
        control.DataReceived += (_, _) => dataReceivedThreadId = Environment.CurrentManagedThreadId;

        try
        {
            await control.StartSessionAsync(new FakeTransportOptions("fake"));

            await Task.Run(() => transport.RaiseData(Encoding.UTF8.GetBytes("thread-check\n")));

            bool processed = await WaitUntilAsync(
                () => factory.LastProcessor is not null && factory.LastProcessor.HasRecordedThread,
                TimeSpan.FromSeconds(5));

            Assert.True(processed, "Expected managed transport output to reach the VT processor.");
            Assert.NotNull(factory.LastProcessor);

            bool eventRaised = await WaitUntilAsync(
                () => dataReceivedThreadId.HasValue,
                TimeSpan.FromSeconds(5));

            Assert.True(eventRaised, "Expected DataReceived to be raised for managed transport output.");
            Assert.NotEqual(uiThreadId, factory.LastProcessor!.LastProcessThreadId);
            Assert.Equal(uiThreadId, dataReceivedThreadId);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupControlAsync(control);
        }
    }

    [AvaloniaFact]
    public void Control_WriteOutput_UpdatesScrollExtent_WithoutResize()
    {
        TerminalControl control = new()
        {
            VtProcessorPreference = VtProcessorPreference.Managed,
        };

        Assert.NotNull(control.ScrollData);
        Assert.NotNull(control.Screen);
        double initialExtent = control.ScrollData!.Extent;
        int initialRows = control.Rows;
        int initialTotalRows = control.Screen!.TotalRows;

        for (int i = 0; i < 128; i++)
        {
            control.WriteOutput("line\n"u8);
        }

        Assert.Equal(initialRows, control.Rows); // no resize side effects
        Assert.True(control.Screen.TotalRows > initialTotalRows);
        Assert.True(control.ScrollData.Extent > initialExtent);
        Assert.True(control.ScrollData.MaxOffset > 0);
    }

    [AvaloniaFact]
    public void Control_ScrollingBack_HidesCursorUntilBottom()
    {
        TerminalControl control = new()
        {
            VtProcessorPreference = VtProcessorPreference.Managed,
        };

        Assert.NotNull(control.ScrollData);
        Assert.NotNull(control.Screen);
        Assert.NotNull(control.Renderer);

        for (int i = 0; i < 128; i++)
        {
            control.WriteOutput("line\n"u8);
        }

        TerminalScreen screen = control.Screen!;
        SkiaTerminalRenderer renderer = control.Renderer!;

        Assert.True(control.ScrollData!.MaxOffset > 0);
        Assert.True(renderer.CursorVisible);

        ((IScrollable)control).Offset = new Vector(0, 0);

        Assert.True(screen.ScrollOffset > 0);
        Assert.False(renderer.CursorVisible);

        control.ScrollToBottom();

        Assert.Equal(0, screen.ScrollOffset);
        Assert.True(renderer.CursorVisible);
    }

    [AvaloniaFact]
    public void Control_ScrollingBack_HidesCursorEvenWhenCursorRowIsInViewportRange()
    {
        TerminalControl control = new()
        {
            VtProcessorPreference = VtProcessorPreference.Managed,
        };

        Assert.NotNull(control.ScrollData);
        Assert.NotNull(control.Screen);
        Assert.NotNull(control.Renderer);

        for (int i = 0; i < 128; i++)
        {
            control.WriteOutput("line\n"u8);
        }

        TerminalScreen screen = control.Screen!;
        SkiaTerminalRenderer renderer = control.Renderer!;
        Assert.True(control.ScrollData!.MaxOffset > 0);

        // Keep the cursor on the first viewport row so it still lands in-range
        // when we move one row into scrollback.
        control.WriteOutput("\x1b[1;1H"u8);
        Assert.Equal(0, renderer.CursorRow);
        Assert.True(renderer.CursorVisible);

        control.ScrollByRows(-1);

        Assert.True(screen.ScrollOffset > 0);
        Assert.True((uint)renderer.CursorRow < (uint)screen.ViewportRows);
        Assert.False(renderer.CursorVisible);

        control.ScrollToBottom();

        Assert.Equal(0, screen.ScrollOffset);
        Assert.True(renderer.CursorVisible);
    }

    [AvaloniaFact]
    public void Control_WriteOutputWhileScrolledBack_PreservesVisibleScrollbackRows()
    {
        TerminalControl control = new()
        {
            VtProcessorPreference = VtProcessorPreference.Managed,
        };

        Assert.NotNull(control.ScrollData);
        Assert.NotNull(control.Screen);

        for (int i = 0; i < 64; i++)
        {
            control.WriteOutput(Encoding.UTF8.GetBytes($"LINE-{i:000}\n"));
        }

        TerminalScreen screen = control.Screen!;
        Assert.True(control.ScrollData!.MaxOffset > 0);

        control.ScrollByRows(-3);

        Assert.True(screen.ScrollOffset > 0);
        string[] beforeRows = ReadVisibleAsciiRows(screen);
        int totalRowsBeforeOutput = screen.TotalRows;

        control.WriteOutput("SCROLLBACK-MUTATION-SENTINEL\n"u8);

        string[] afterRows = ReadVisibleAsciiRows(screen);
        Assert.Equal(beforeRows, afterRows);
        Assert.True(screen.TotalRows > totalRowsBeforeOutput);

        control.ScrollToBottom();

        Assert.True(ContainsScreenText(control, "SCROLLBACK-MUTATION-SENTINEL"));
    }

    [AvaloniaFact]
    public void Control_AlternateScreenResize_DoesNotExposePrimaryScrollback()
    {
        TerminalControl control = new()
        {
            Columns = 18,
            Rows = 5,
            VtProcessorPreference = VtProcessorPreference.Managed,
        };

        Assert.NotNull(control.ScrollData);
        Assert.NotNull(control.Screen);
        TerminalScreen screen = control.Screen!;

        for (int i = 0; i < 64; i++)
        {
            control.WriteOutput(Encoding.UTF8.GetBytes($"HIST-{i:00}\r\n"));
        }

        Assert.True(control.ScrollData!.MaxOffset > 0);

        ((IScrollable)control).Offset = new Vector(0, 0);
        Assert.True(screen.ScrollOffset > 0);

        control.WriteOutput(Encoding.UTF8.GetBytes("\x1b[?1049h\x1b[2J\x1b[HALT-00\r\nALT-01\r\nALT-02\r\nALT-03"));

        Assert.Equal(0, screen.ScrollOffset);
        Assert.Equal(0, control.ScrollData.MaxOffset);
        AssertVisibleAlternateScreenRowsOnly(screen);

        control.Rows = 3;

        Assert.Equal(0, screen.ScrollOffset);
        Assert.Equal(0, control.ScrollData.MaxOffset);
        AssertVisibleAlternateScreenRowsOnly(screen);

        control.Rows = 7;

        Assert.Equal(0, screen.ScrollOffset);
        Assert.Equal(0, control.ScrollData.MaxOffset);
        AssertVisibleAlternateScreenRowsOnly(screen);
    }

    [AvaloniaFact]
    public void Control_OffsetScroll_MarksViewportRowsDirty()
    {
        TerminalControl control = new()
        {
            VtProcessorPreference = VtProcessorPreference.Managed,
        };

        Assert.NotNull(control.ScrollData);
        Assert.NotNull(control.Screen);
        TerminalScreen screen = control.Screen!;

        for (int i = 0; i < 128; i++)
        {
            control.WriteOutput("line\n"u8);
        }

        Assert.True(control.ScrollData!.MaxOffset > 0);

        for (int row = 0; row < screen.ViewportRows; row++)
        {
            screen.GetViewportRow(row).IsDirty = false;
        }

        ((IScrollable)control).Offset = new Vector(0, 0);

        bool hasDirtyRow = false;
        for (int row = 0; row < screen.ViewportRows; row++)
        {
            if (screen.GetViewportRow(row).IsDirty)
            {
                hasDirtyRow = true;
                break;
            }
        }

        Assert.True(hasDirtyRow);
    }

    [AvaloniaFact]
    public void Control_OffsetScroll_DoesNotDirtyEntireScrollback()
    {
        TerminalControl control = new()
        {
            VtProcessorPreference = VtProcessorPreference.Managed,
        };

        Assert.NotNull(control.ScrollData);
        Assert.NotNull(control.Screen);
        TerminalScreen screen = control.Screen!;

        for (int i = 0; i < 512; i++)
        {
            control.WriteOutput("line\n"u8);
        }

        Assert.True(control.ScrollData!.MaxOffset > 0);
        Assert.True(screen.TotalRows > screen.ViewportRows * 2);

        for (int row = 0; row < screen.TotalRows; row++)
        {
            screen.GetRow(row).IsDirty = false;
        }

        ((IScrollable)control).Offset = new Vector(0, 0);

        int dirtyRows = 0;
        for (int row = 0; row < screen.TotalRows; row++)
        {
            if (screen.GetRow(row).IsDirty)
            {
                dirtyRows++;
            }
        }

        Assert.InRange(dirtyRows, 1, screen.ViewportRows);
    }

    [AvaloniaFact]
    public void Control_ManagedCursorStyle_UpdatesRendererCursorStyle()
    {
        TerminalControl control = new()
        {
            VtProcessorPreference = VtProcessorPreference.Managed,
        };

        Assert.NotNull(control.Renderer);
        SkiaTerminalRenderer renderer = control.Renderer!;

        control.WriteOutput("\x1b[6 q"u8);
        Assert.Equal(CursorStyle.Bar, renderer.CursorStyle);

        control.WriteOutput("\x1b[3 q"u8);
        Assert.Equal(CursorStyle.Underline, renderer.CursorStyle);

        control.WriteOutput("\x1b[1 q"u8);
        Assert.Equal(CursorStyle.Block, renderer.CursorStyle);
    }

    [AvaloniaFact]
    public void Control_BackgroundOpacityToggle_UpdatesRendererState()
    {
        TerminalControl control = new();
        Assert.NotNull(control.Renderer);

        Assert.False(control.BackgroundOpacityEnabled);
        Assert.False(control.Renderer!.BackgroundOpacityEnabled);

        control.ToggleBackgroundOpacity();
        Assert.True(control.BackgroundOpacityEnabled);
        Assert.True(control.Renderer.BackgroundOpacityEnabled);
        Assert.True(control.Renderer.BackgroundOpacityCells);

        control.BackgroundOpacityEnabled = false;
        Assert.False(control.BackgroundOpacityEnabled);
        Assert.False(control.Renderer.BackgroundOpacityEnabled);
    }

    [AvaloniaFact]
    public void Control_SearchLifecycle_BuildsMatchesAndSelectionWithoutCallerTotals()
    {
        TerminalControl control = new()
        {
            VtProcessorPreference = VtProcessorPreference.Managed,
        };

        control.WriteOutput("needle alpha needle\nbeta needle"u8);

        control.StartSearch("needle");

        Assert.Equal("needle", control.SearchNeedle);
        Assert.Equal(3, control.SearchTotal);
        Assert.Equal(0, control.SearchSelected);

        Assert.True(control.SelectNextSearchMatch());
        Assert.Equal(1, control.SearchSelected);

        Assert.True(control.SelectPreviousSearchMatch());
        Assert.Equal(0, control.SearchSelected);

        control.EndSearch();

        Assert.Null(control.SearchNeedle);
        Assert.Equal(0, control.SearchTotal);
        Assert.Equal(-1, control.SearchSelected);
    }

    [AvaloniaFact]
    public void Control_SearchLifecycle_NativeViewportScrollsSelectedMatchIntoView()
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            return;
        }

        INativeVtProcessorProvider[] nativeProviders =
        [
            new GhosttyVtProcessorProvider(),
        ];
        TerminalControl control = CreateControlWithTransport(
            new FakeTransport(),
            new DefaultVtProcessorFactory(nativeProviders),
            VtProcessorPreference.Native);
        control.Columns = 8;
        control.Rows = 4;

        StringBuilder builder = new();
        for (int i = 0; i < 10; i++)
        {
            builder.Append("L");
            builder.Append(i.ToString("D2"));
            builder.Append('\n');
        }

        control.WriteOutput(Encoding.UTF8.GetBytes(builder.ToString()));
        Assert.NotNull(control.ScrollData);
        Assert.True(control.ScrollData!.OffsetRows > 0);

        control.StartSearch("L00");

        Assert.Equal(0, control.ScrollData.OffsetRows);
        Assert.Equal(1, control.SearchTotal);
        Assert.Equal(0, control.SearchSelected);
    }

    [AvaloniaFact]
    public void Control_SearchLifecycle_NativeViewportScroll_UsesNativeTotalRows_NotViewportMirror()
    {
        FakeSearchViewportVtProcessor processor = new(
            new TerminalViewportScrollState(TotalRows: 100, OffsetRows: 96, VisibleRows: 4),
            [new TerminalSearchMatch(10, 0, 2)]);
        TerminalControl control = CreateControlWithTransport(
            new FakeTransport(),
            new SingleProcessorFactory(processor),
            VtProcessorPreference.Native);
        control.Columns = 8;
        control.Rows = 4;

        control.StartSearch("L10");

        Assert.Equal(7UL, processor.LastSetViewportOffsetRows);
        Assert.Equal(7UL, processor.ViewportScrollState.OffsetRows);
        Assert.Equal(1, control.SearchTotal);
        Assert.Equal(0, control.SearchSelected);
    }

    [AvaloniaFact]
    public void Control_HoveredLinkUrl_NormalizesWhitespace()
    {
        TerminalControl control = new();

        control.SetHoveredLinkUrl("   ");
        Assert.Null(control.HoveredLinkUrl);

        control.SetHoveredLinkUrl("https://example.com");
        Assert.Equal("https://example.com", control.HoveredLinkUrl);
    }

    [AvaloniaFact]
    public async Task Control_ManagedCursorBlinking_TogglesVisiblePhase_AndSteadyStyleStopsBlinking()
    {
        TerminalControl control = new()
        {
            VtProcessorPreference = VtProcessorPreference.Managed,
        };

        Assert.NotNull(control.Renderer);
        SkiaTerminalRenderer renderer = control.Renderer!;
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };

        window.Show();
        control.Focus();
        HeadlessTerminalTestCleanup.RunDispatcherJobs();

        try
        {
            control.WriteOutput("X"u8);
            control.WriteOutput("\x1b[1 q"u8); // blinking block
            HeadlessTerminalTestCleanup.RunDispatcherJobs();

            bool initialVisible = renderer.CursorVisible;
            bool toggled = await WaitUntilAsync(
                () => renderer.CursorVisible != initialVisible,
                TimeSpan.FromSeconds(2));
            Assert.True(toggled);

            control.WriteOutput("\x1b[2 q"u8); // steady block
            HeadlessTerminalTestCleanup.RunDispatcherJobs();
            bool steadyVisible = renderer.CursorVisible;

            await Task.Delay(700);
            HeadlessTerminalTestCleanup.RunDispatcherJobs();
            Assert.Equal(steadyVisible, renderer.CursorVisible);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public void Control_MultipleInstances_AreIndependent()
    {
        var control1 = new TerminalControl { Columns = 80, Rows = 24 };
        var control2 = new TerminalControl { Columns = 120, Rows = 40 };

        Assert.Equal(80, control1.Columns);
        Assert.Equal(120, control2.Columns);
        Assert.Equal(24, control1.Rows);
        Assert.Equal(40, control2.Rows);

        Assert.NotSame(control1.Screen, control2.Screen);
        Assert.NotSame(control1.Renderer, control2.Renderer);
    }

    [AvaloniaFact]
    public async Task Control_CopySelection_PrefersGrapheme_AndSkipsSpacerCells()
    {
        const string familyEmoji = "\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466";

        TerminalControl control = new();
        Window window = new() { Content = control };
        window.Show();

        try
        {
            TerminalScreen? screen = control.Screen;
            SkiaTerminalRenderer? renderer = control.Renderer;
            Assert.NotNull(screen);
            Assert.NotNull(renderer);

            TerminalRow row = screen!.GetViewportRow(0);
            row[0].Codepoint = 0x1F468;
            row[0].Grapheme = familyEmoji;
            row[0].Width = 1;
            row[1].Codepoint = 0;
            row[1].Width = 0; // spacer cell should not contribute selection text

            renderer!.SelectionStart = (0, 0);
            renderer.SelectionEnd = (1, 0);

            await control.CopySelectionAsync();

            string? copied = await window.Clipboard!.TryGetTextAsync();
            Assert.Equal(familyEmoji, copied);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_CopySelection_WithNegativeStartColumn_IsClamped()
    {
        TerminalControl control = new();
        Window window = new() { Content = control };
        window.Show();

        try
        {
            TerminalScreen? screen = control.Screen;
            SkiaTerminalRenderer? renderer = control.Renderer;
            Assert.NotNull(screen);
            Assert.NotNull(renderer);

            TerminalRow row = screen!.GetViewportRow(0);
            row[0].Codepoint = 'A';

            renderer!.SelectionStart = (-5, 0);
            renderer.SelectionEnd = (0, 0);

            await control.CopySelectionAsync();

            string? copied = await window.Clipboard!.TryGetTextAsync();
            Assert.Equal("A", copied);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_PasteAsync_ManagedVt_UsesBracketedFraming_WhenModeEnabled()
    {
        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed);
        Window window = new() { Content = control };
        window.Show();

        try
        {
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            control.WriteOutput("\x1b[?2004h"u8);
            await window.Clipboard!.SetTextAsync("echo hello");
            transport.SentInputs.Clear();

            await control.PasteAsync();

            Assert.Contains(
                transport.SentInputs,
                static payload => Encoding.UTF8.GetString(payload) == "\x1b[200~echo hello\x1b[201~");
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_FocusEvents_ManagedVt_SendCsiIAndO_WhenMode1004Enabled()
    {
        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed);
        Button other = new();
        StackPanel root = new()
        {
            Children =
            {
                control,
                other,
            },
        };
        Window window = new() { Content = root };
        window.Show();

        try
        {
            await control.StartSessionAsync(new FakeTransportOptions("fake"));

            // Enable focus-event reporting (DECSET 1004) in managed VT state.
            control.WriteOutput("\x1b[?1004h"u8);
            HeadlessTerminalTestCleanup.RunDispatcherJobs();

            other.Focus();
            HeadlessTerminalTestCleanup.RunDispatcherJobs();
            transport.SentInputs.Clear();

            control.Focus();
            HeadlessTerminalTestCleanup.RunDispatcherJobs();
            other.Focus();
            HeadlessTerminalTestCleanup.RunDispatcherJobs();

            Assert.Contains(transport.SentInputs, static payload =>
                Encoding.UTF8.GetString(payload) == "\x1b[I");
            Assert.Contains(transport.SentInputs, static payload =>
                Encoding.UTF8.GetString(payload) == "\x1b[O");
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_PasteAsync_DefaultPolicy_UsesManagedPasteEncodingRules()
    {
        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed);
        Window window = new() { Content = control };
        window.Show();

        try
        {
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            await window.Clipboard!.SetTextAsync("safe\x1b[201~\u0001text");
            transport.SentInputs.Clear();

            await control.PasteAsync();

            Assert.Contains(transport.SentInputs, static payload =>
                Encoding.UTF8.GetString(payload) == "safe [201~\u0001text");
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_PasteAsync_NativeVt_UsesBracketedFraming_WhenModeEnabled()
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            return;
        }

        FakeTransport transport = new();
        INativeVtProcessorProvider[] nativeProviders =
        [
            new GhosttyVtProcessorProvider(),
        ];
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(nativeProviders),
            VtProcessorPreference.Native);
        Window window = new() { Content = control };
        window.Show();

        try
        {
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            control.WriteOutput("\x1b[?2004h"u8);
            await window.Clipboard!.SetTextAsync("printf");
            transport.SentInputs.Clear();

            await control.PasteAsync();

            Assert.Contains(
                transport.SentInputs,
                static payload => Encoding.UTF8.GetString(payload) == "\x1b[200~printf\x1b[201~");
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_NativeTransportEcho_RendersImmediately_AfterTypedInput()
    {
        if (!GhosttyVtProcessor.IsAvailable())
        {
            return;
        }

        FakeTransport transport = new();
        INativeVtProcessorProvider[] nativeProviders =
        [
            new GhosttyVtProcessorProvider(),
        ];
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(nativeProviders),
            VtProcessorPreference.Native);
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        window.Show();

        try
        {
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            control.Focus();
            HeadlessTerminalTestCleanup.RunDispatcherJobs();

            byte[] before = CaptureFramePng(window);

            window.KeyTextInput("x");

            bool echoed = await WaitUntilAsync(
                () => ContainsScreenText(control, "x"),
                TimeSpan.FromSeconds(2));
            Assert.True(echoed);

            byte[] after = CaptureFramePng(window);
            Assert.False(
                before.AsSpan().SequenceEqual(after),
                "Expected typed native transport echo to trigger a visible frame update immediately.");
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_PasteAsync_BlockUnsafePolicy_BlocksMultilinePaste()
    {
        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed);
        control.PasteSafetyPolicy = TerminalPasteSafetyPolicy.BlockUnsafe;
        Window window = new() { Content = control };
        window.Show();

        try
        {
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            await window.Clipboard!.SetTextAsync("line1\nline2");
            transport.SentInputs.Clear();

            await control.PasteAsync();

            Assert.Empty(transport.SentInputs);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_PasteAsync_ConfirmUnsafePolicy_SupportsSanitizeDecision()
    {
        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed);
        control.PasteSafetyPolicy = TerminalPasteSafetyPolicy.ConfirmUnsafe;
        control.UnsafePasteHandler = static _ =>
            ValueTask.FromResult(TerminalPasteSafetyDecision.Sanitize);
        Window window = new() { Content = control };
        window.Show();

        try
        {
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            await window.Clipboard!.SetTextAsync("safe\x1b[201~\u0001text");
            transport.SentInputs.Clear();

            await control.PasteAsync();

            Assert.Contains(transport.SentInputs, static payload =>
                Encoding.UTF8.GetString(payload) == "safe[201~text");
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_PasteAsync_LegacySelectionService_UsesInterfaceForwarder()
    {
        FakeTransport transport = new();
        LegacySelectionService selectionService = new();
        CompositeTerminalTransportFactory factory = new(
            new ITerminalTransportProvider[]
            {
                new FakeTransportProvider(transport),
            });
        TerminalControl control = new(
            new TerminalSessionService(),
            new DefaultTerminalInputAdapter(),
            selectionService,
            new DefaultTerminalScrollService(),
            new DefaultVtProcessorFactory(),
            new DefaultPtyFactory(),
            new NullSshCredentialProvider(),
            new RejectAllSshHostKeyValidator(),
            factory);

        Window window = new() { Content = control };
        window.Show();

        try
        {
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            await window.Clipboard!.SetTextAsync("legacy");
            transport.SentInputs.Clear();

            await control.PasteAsync();

            Assert.Equal(1, selectionService.LegacyPasteCallCount);
            Assert.Contains(transport.SentInputs, static payload =>
                Encoding.UTF8.GetString(payload) == "legacy");
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    private static TerminalControl CreateControlWithTransport(
        FakeTransport transport,
        IVtProcessorFactory vtProcessorFactory,
        VtProcessorPreference preference,
        ITerminalScrollService? scrollService = null)
    {
        CompositeTerminalTransportFactory factory = new(
            new ITerminalTransportProvider[]
            {
                new FakeTransportProvider(transport),
            });

        return new TerminalControl(
            new TerminalSessionService(),
            new DefaultTerminalInputAdapter(),
            new DefaultTerminalSelectionService(),
            scrollService ?? new DefaultTerminalScrollService(),
            vtProcessorFactory,
            new DefaultPtyFactory(),
            new NullSshCredentialProvider(),
            new RejectAllSshHostKeyValidator(),
            factory)
        {
            VtProcessorPreference = preference,
        };
    }

    private static byte[] CaptureFramePng(Window window)
    {
        HeadlessTerminalTestCleanup.RunDispatcherJobs();
        Bitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        using MemoryStream stream = new();
        frame!.Save(stream);
        return stream.ToArray();
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        return await HeadlessTerminalTestCleanup.WaitUntilAsync(predicate, timeout);
    }

    private static bool ContainsScreenText(TerminalControl control, string needle)
    {
        TerminalScreen? screen = control.Screen;
        if (screen is null)
        {
            return false;
        }

        for (int row = 0; row < screen.ViewportRows; row++)
        {
            TerminalRow terminalRow = screen.GetViewportRow(row);
            char[] chars = new char[terminalRow.Columns];
            for (int col = 0; col < terminalRow.Columns; col++)
            {
                int codepoint = terminalRow[col].Codepoint;
                chars[col] = codepoint <= 0 ? ' ' : codepoint <= 0x7F ? (char)codepoint : '?';
            }

            if (new string(chars).Contains(needle, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string[] ReadVisibleAsciiRows(TerminalScreen screen)
    {
        string[] rows = new string[screen.ViewportRows];
        for (int row = 0; row < screen.ViewportRows; row++)
        {
            TerminalRow terminalRow = screen.GetViewportRow(row);
            char[] chars = new char[terminalRow.Columns];
            for (int col = 0; col < terminalRow.Columns; col++)
            {
                int codepoint = terminalRow[col].Codepoint;
                chars[col] = codepoint <= 0 ? ' ' : codepoint <= 0x7F ? (char)codepoint : '?';
            }

            rows[row] = new string(chars);
        }

        return rows;
    }

    private static void AssertVisibleAlternateScreenRowsOnly(TerminalScreen screen)
    {
        string visible = string.Join('\n', ReadVisibleAsciiRows(screen));
        Assert.Contains("ALT-", visible, StringComparison.Ordinal);
        Assert.DoesNotContain("HIST-", visible, StringComparison.Ordinal);
    }

    private sealed class CountingTerminalScrollService : ITerminalScrollService
    {
        private readonly DefaultTerminalScrollService _inner = new();
        private int _handleOutputCallCount;

        public int HandleOutputCallCount => Volatile.Read(ref _handleOutputCallCount);

        public void HandleOutput(
            TerminalScrollData? scrollData,
            TerminalScreen? screen,
            bool autoScroll,
            TerminalPresenter? presenter,
            Action raiseScrollInvalidated)
        {
            Interlocked.Increment(ref _handleOutputCallCount);
            _inner.HandleOutput(scrollData, screen, autoScroll, presenter, raiseScrollInvalidated);
        }

        public void ScrollByRows(
            int rows,
            TerminalScrollData? scrollData,
            TerminalScreen? screen,
            TerminalPresenter? presenter)
        {
            _inner.ScrollByRows(rows, scrollData, screen, presenter);
        }

        public void ScrollToBottom(
            TerminalScrollData? scrollData,
            TerminalScreen? screen,
            TerminalPresenter? presenter)
        {
            _inner.ScrollToBottom(scrollData, screen, presenter);
        }

        public void HandlePointerWheel(
            PointerWheelEventArgs e,
            VirtualizedTerminalScrollViewer? scrollViewer,
            ITerminalSessionService sessionService,
            TerminalPresenter? presenter,
            Action raiseScrollInvalidated)
        {
            _inner.HandlePointerWheel(e, scrollViewer, sessionService, presenter, raiseScrollInvalidated);
        }
    }

    private static uint ColorToArgb(Color c) =>
        ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;

    private sealed class FakeEndpoint : ITerminalEndpoint, ITerminalScaleSink
    {
        public byte[]? LastInput { get; private set; }
        public bool Focused { get; private set; }
        public int WidthPx { get; private set; }
        public int HeightPx { get; private set; }
        public double ScaleX { get; private set; }
        public double ScaleY { get; private set; }

        public void SendText(ReadOnlySpan<byte> utf8)
        {
            LastInput = utf8.ToArray();
        }

        public void SetFocus(bool focused)
        {
            Focused = focused;
        }

        public void SetSize(int widthPx, int heightPx)
        {
            WidthPx = widthPx;
            HeightPx = heightPx;
        }

        public void SetContentScale(double scaleX, double scaleY)
        {
            ScaleX = scaleX;
            ScaleY = scaleY;
        }
    }

    private sealed class TrackingVtProcessorFactory : IVtProcessorFactory
    {
        public int CreateCallCount { get; private set; }
        public List<VtProcessorPreference> Preferences { get; } = [];

        public IVtProcessor Create(TerminalScreen screen, VtProcessorPreference preference)
        {
            _ = screen;
            CreateCallCount++;
            Preferences.Add(preference);
            return new TrackingVtProcessor();
        }
    }

    private sealed class SingleProcessorFactory(IVtProcessor processor) : IVtProcessorFactory
    {
        public IVtProcessor Create(TerminalScreen screen, VtProcessorPreference preference)
        {
            _ = screen;
            _ = preference;
            return processor;
        }
    }

    private sealed class TrackingVtProcessor : IVtProcessor
    {
        public int CursorCol => 0;
        public int CursorRow => 0;
        public bool CursorVisible => true;
        public bool ApplicationCursorKeys => false;
        public bool ApplicationKeypad => false;
        public bool AlternateScreen => false;
        public bool BracketedPaste => false;
        public bool Win32InputMode => false;
        public TerminalModeState ModeState => new(
            CursorVisible,
            ApplicationCursorKeys,
            ApplicationKeypad,
            AlternateScreen,
            BracketedPaste,
            Win32InputMode);

        public event EventHandler<TerminalModeState>? ModeChanged
        {
            add { }
            remove { }
        }

        public Action<byte[]>? ResponseCallback { get; set; }
        public Action? BellCallback { get; set; }
        public Action<string>? TitleCallback { get; set; }

        public void Process(ReadOnlySpan<byte> data)
        {
            _ = data;
        }

        public void NotifyResize(int columns, int rows)
        {
            _ = columns;
            _ = rows;
        }

        public void NotifyResize(int columns, int rows, int widthPx, int heightPx)
        {
            _ = columns;
            _ = rows;
            _ = widthPx;
            _ = heightPx;
        }

        public void Reset()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeSearchViewportVtProcessor(
        TerminalViewportScrollState viewportScrollState,
        IReadOnlyList<TerminalSearchMatch> matches) : IVtProcessor, ITerminalViewportScrollSource, ITerminalSearchSource
    {
        private readonly List<TerminalSearchMatch> _matches = [.. matches];

        public int CursorCol => 0;
        public int CursorRow => 0;
        public bool CursorVisible => true;
        public bool ApplicationCursorKeys => false;
        public bool ApplicationKeypad => false;
        public bool AlternateScreen => false;
        public bool BracketedPaste => false;
        public bool Win32InputMode => false;
        public TerminalModeState ModeState => new(
            CursorVisible,
            ApplicationCursorKeys,
            ApplicationKeypad,
            AlternateScreen,
            BracketedPaste,
            Win32InputMode);

        public TerminalViewportScrollState ViewportScrollState { get; private set; } = viewportScrollState;

        public ulong? LastSetViewportOffsetRows { get; private set; }

        public event EventHandler<TerminalModeState>? ModeChanged
        {
            add { }
            remove { }
        }

        public Action<byte[]>? ResponseCallback { get; set; }

        public Action? BellCallback { get; set; }

        public Action<string>? TitleCallback { get; set; }

        public void Process(ReadOnlySpan<byte> data)
        {
            _ = data;
        }

        public void NotifyResize(int columns, int rows)
        {
            _ = columns;
            _ = rows;
        }

        public void NotifyResize(int columns, int rows, int widthPx, int heightPx)
        {
            _ = columns;
            _ = rows;
            _ = widthPx;
            _ = heightPx;
        }

        public void ApplyTheme(TerminalTheme theme)
        {
            _ = theme;
        }

        public void Reset()
        {
        }

        public void Dispose()
        {
        }

        public void ScrollViewportByRows(int deltaRows)
        {
            long next = checked((long)ViewportScrollState.OffsetRows + deltaRows);
            if (next < 0)
            {
                next = 0;
            }

            ulong maxOffsetRows = ViewportScrollState.MaxOffsetRows;
            if ((ulong)next > maxOffsetRows)
            {
                next = (long)maxOffsetRows;
            }

            ViewportScrollState = ViewportScrollState with { OffsetRows = (ulong)next };
        }

        public void ScrollViewportToTop()
        {
            ViewportScrollState = ViewportScrollState with { OffsetRows = 0 };
        }

        public void ScrollViewportToBottom()
        {
            ViewportScrollState = ViewportScrollState with { OffsetRows = ViewportScrollState.MaxOffsetRows };
        }

        public void SetViewportOffsetRows(ulong offsetRows)
        {
            ulong clamped = Math.Min(offsetRows, ViewportScrollState.MaxOffsetRows);
            LastSetViewportOffsetRows = clamped;
            ViewportScrollState = ViewportScrollState with { OffsetRows = clamped };
        }

        public void PopulateSearchMatches(string needle, List<TerminalSearchMatch> destination)
        {
            _ = needle;
            destination.Clear();
            destination.AddRange(_matches);
        }
    }

    private sealed class ThreadTrackingVtProcessorFactory : IVtProcessorFactory
    {
        public ThreadTrackingVtProcessor? LastProcessor { get; private set; }

        public IVtProcessor Create(TerminalScreen screen, VtProcessorPreference preference)
        {
            _ = screen;
            _ = preference;
            ThreadTrackingVtProcessor processor = new();
            LastProcessor = processor;
            return processor;
        }
    }

    private sealed class ThreadTrackingVtProcessor : IVtProcessor
    {
        private int _lastProcessThreadId = -1;

        public int CursorCol => 0;
        public int CursorRow => 0;
        public bool CursorVisible => true;
        public bool ApplicationCursorKeys => false;
        public bool ApplicationKeypad => false;
        public bool AlternateScreen => false;
        public bool BracketedPaste => false;
        public bool Win32InputMode => false;
        public TerminalModeState ModeState => new(
            CursorVisible,
            ApplicationCursorKeys,
            ApplicationKeypad,
            AlternateScreen,
            BracketedPaste,
            Win32InputMode);

        public bool HasRecordedThread => Volatile.Read(ref _lastProcessThreadId) >= 0;

        public int LastProcessThreadId => Volatile.Read(ref _lastProcessThreadId);

        public event EventHandler<TerminalModeState>? ModeChanged
        {
            add { }
            remove { }
        }

        public Action<byte[]>? ResponseCallback { get; set; }
        public Action? BellCallback { get; set; }
        public Action<string>? TitleCallback { get; set; }

        public void Process(ReadOnlySpan<byte> data)
        {
            _ = data;
            Volatile.Write(ref _lastProcessThreadId, Environment.CurrentManagedThreadId);
        }

        public void NotifyResize(int columns, int rows)
        {
            _ = columns;
            _ = rows;
        }

        public void NotifyResize(int columns, int rows, int widthPx, int heightPx)
        {
            _ = columns;
            _ = rows;
            _ = widthPx;
            _ = heightPx;
        }

        public void Reset()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class ResizeTrackingVtProcessorFactory : IVtProcessorFactory
    {
        public ResizeTrackingVtProcessor? LastProcessor { get; private set; }

        public IVtProcessor Create(TerminalScreen screen, VtProcessorPreference preference)
        {
            _ = screen;
            _ = preference;
            ResizeTrackingVtProcessor processor = new();
            LastProcessor = processor;
            return processor;
        }
    }

    private sealed class ResizeTrackingVtProcessor : IVtProcessor
    {
        public int CursorCol => 0;
        public int CursorRow => 0;
        public bool CursorVisible => true;
        public bool ApplicationCursorKeys => false;
        public bool ApplicationKeypad => false;
        public bool AlternateScreen => false;
        public bool BracketedPaste => false;
        public bool Win32InputMode => false;
        public TerminalModeState ModeState => new(
            CursorVisible,
            ApplicationCursorKeys,
            ApplicationKeypad,
            AlternateScreen,
            BracketedPaste,
            Win32InputMode);

        public List<TerminalSessionDimensions> ResizeNotifications { get; } = [];

        public event EventHandler<TerminalModeState>? ModeChanged
        {
            add { }
            remove { }
        }

        public Action<byte[]>? ResponseCallback { get; set; }
        public Action? BellCallback { get; set; }
        public Action<string>? TitleCallback { get; set; }

        public void Process(ReadOnlySpan<byte> data)
        {
            _ = data;
        }

        public void NotifyResize(int columns, int rows)
        {
            ResizeNotifications.Add(new TerminalSessionDimensions(columns, rows, WidthPixels: 0, HeightPixels: 0));
        }

        public void NotifyResize(int columns, int rows, int widthPx, int heightPx)
        {
            ResizeNotifications.Add(new TerminalSessionDimensions(columns, rows, widthPx, heightPx));
        }

        public void Reset()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class ThemeTrackingVtProcessorFactory : IVtProcessorFactory
    {
        public ThemeTrackingVtProcessor? LastProcessor { get; private set; }

        public IVtProcessor Create(TerminalScreen screen, VtProcessorPreference preference)
        {
            _ = screen;
            _ = preference;
            ThemeTrackingVtProcessor processor = new();
            LastProcessor = processor;
            return processor;
        }
    }

    private sealed class ThemeTrackingVtProcessor : IVtProcessor, ITerminalThemeSink
    {
        public int CursorCol => 0;
        public int CursorRow => 0;
        public bool CursorVisible => true;
        public bool ApplicationCursorKeys => false;
        public bool ApplicationKeypad => false;
        public bool AlternateScreen => false;
        public bool BracketedPaste => false;
        public bool Win32InputMode => false;
        public TerminalModeState ModeState => new(
            CursorVisible,
            ApplicationCursorKeys,
            ApplicationKeypad,
            AlternateScreen,
            BracketedPaste,
            Win32InputMode);

        public TerminalTheme? LastAppliedTheme { get; private set; }

        public event EventHandler<TerminalModeState>? ModeChanged
        {
            add { }
            remove { }
        }

        public Action<byte[]>? ResponseCallback { get; set; }
        public Action? BellCallback { get; set; }
        public Action<string>? TitleCallback { get; set; }

        public void Process(ReadOnlySpan<byte> data)
        {
            _ = data;
        }

        public void NotifyResize(int columns, int rows)
        {
            _ = columns;
            _ = rows;
        }

        public void NotifyResize(int columns, int rows, int widthPx, int heightPx)
        {
            _ = columns;
            _ = rows;
            _ = widthPx;
            _ = heightPx;
        }

        public void Reset()
        {
        }

        public void ApplyTheme(TerminalTheme theme)
        {
            LastAppliedTheme = theme;
        }

        public void Dispose()
        {
        }
    }

    private sealed class LegacySelectionService : ITerminalSelectionService
    {
        public int LegacyPasteCallCount { get; private set; }

        public Task CopySelectionAsync(
            Control owner,
            ITerminalSessionService sessionService,
            TerminalScreen? screen,
            SkiaTerminalRenderer? renderer)
        {
            _ = owner;
            _ = sessionService;
            _ = screen;
            _ = renderer;
            return Task.CompletedTask;
        }

        public async Task PasteAsync(Control owner, Action<string> sendInput)
        {
            LegacyPasteCallCount++;
            var clipboard = TopLevel.GetTopLevel(owner)?.Clipboard;
            if (clipboard is null)
            {
                return;
            }

            string? text = await clipboard.TryGetTextAsync();
            if (!string.IsNullOrEmpty(text))
            {
                sendInput(text);
            }
        }

        public void ClearSelection(
            TerminalScreen? screen,
            SkiaTerminalRenderer? renderer,
            TerminalPresenter? presenter)
        {
            _ = screen;
            _ = renderer;
            _ = presenter;
        }
    }

    private sealed record FakeTransportOptions(string TransportId) : ITerminalTransportOptions
    {
        public TerminalSessionDimensions Dimensions => new(80, 24, 640, 480);
    }

    private sealed class FakeTransportProvider : ITerminalTransportProvider
    {
        private readonly ITerminalTransport _transport;

        public FakeTransportProvider(ITerminalTransport transport)
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

    private sealed class FakeTransport : ITerminalTransport
    {
        private Action<byte[], int>? _dataReceived;
        private Action<int>? _processExited;

        public event Action<byte[], int>? DataReceived
        {
            add => _dataReceived += value;
            remove => _dataReceived -= value;
        }

        public event Action<int>? ProcessExited
        {
            add => _processExited += value;
            remove => _processExited -= value;
        }

        public bool IsRunning { get; private set; }
        public bool StopCalled { get; private set; }
        public List<byte[]> SentInputs { get; } = [];
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
            byte[] data = utf8.ToArray();
            SentInputs.Add(data);
            _dataReceived?.Invoke(data, data.Length);
        }

        public void Resize(TerminalSessionDimensions dimensions)
        {
            Resizes.Add(dimensions);
        }

        public ValueTask StopAsync()
        {
            StopCalled = true;
            IsRunning = false;
            _processExited?.Invoke(0);
            return ValueTask.CompletedTask;
        }

        public void RaiseProcessExited(int exitCode)
        {
            _processExited?.Invoke(exitCode);
        }

        public void RaiseData(byte[] data)
        {
            _dataReceived?.Invoke(data, data.Length);
        }

        public void Dispose()
        {
            IsRunning = false;
        }
    }
}
