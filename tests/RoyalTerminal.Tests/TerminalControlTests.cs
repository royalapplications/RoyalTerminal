// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — Avalonia headless tests for TerminalControl.

using System.Globalization;
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
using RoyalTerminal.Shaders;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Theming;
using RoyalTerminal.Terminal.Services;
using RoyalTerminal.Terminal.Transport.Pty;
using RoyalTerminal.Terminal.Transport.Ssh;
using RoyalTerminal.Terminal.Transport.Ssh.SshNet;
using SkiaSharp;
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
        Assert.True(control.FontSubpixelPositioning);
        Assert.Equal(TerminalFontEdging.SubpixelAntialias, control.FontEdging);
        Assert.Equal(TerminalFontHinting.Slight, control.FontHinting);
        Assert.True(control.FontBaselineSnap);
        Assert.False(control.FontEmbeddedBitmaps);
        Assert.False(control.FontEmbolden);
        Assert.False(control.FontForceAutoHinting);
        Assert.False(control.FontLinearMetrics);
        Assert.Equal(80, control.Columns);
        Assert.Equal(24, control.Rows);
        Assert.Equal(10_000, control.ScrollbackLimit);
        Assert.False(control.PreserveScrollbackOnSessionStart);
        Assert.Equal(new Thickness(0), control.Padding);
        Assert.True(control.AutoScroll);
        Assert.True(control.ScrollToBottomOnInput);
        Assert.True(control.ReflowOnResize);
        Assert.False(control.SixelGraphicsEnabled);
        Assert.Null(control.ShaderSources);
        Assert.True(control.ShaderAnimationEnabled);
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
            FontSubpixelPositioning = false,
            FontEdging = TerminalFontEdging.Antialias,
            FontHinting = TerminalFontHinting.Full,
            FontBaselineSnap = false,
            FontEmbeddedBitmaps = true,
            FontEmbolden = true,
            FontForceAutoHinting = true,
            FontLinearMetrics = true,
            Columns = 120,
            Rows = 40,
            ScrollbackLimit = 50_000,
            PreserveScrollbackOnSessionStart = true,
            AutoScroll = false,
            ScrollToBottomOnInput = false,
            ReflowOnResize = false,
            SixelGraphicsEnabled = true,
        };

        Assert.Equal("JetBrains Mono", control.FontFamilyName);
        Assert.Equal(TerminalFontSource.System, control.FontSource);
        Assert.Equal(string.Empty, control.FontFilePath);
        Assert.Equal(16.0, control.TerminalFontSize);
        Assert.False(control.FontSubpixelPositioning);
        Assert.Equal(TerminalFontEdging.Antialias, control.FontEdging);
        Assert.Equal(TerminalFontHinting.Full, control.FontHinting);
        Assert.False(control.FontBaselineSnap);
        Assert.True(control.FontEmbeddedBitmaps);
        Assert.True(control.FontEmbolden);
        Assert.True(control.FontForceAutoHinting);
        Assert.True(control.FontLinearMetrics);
        Assert.Equal(120, control.Columns);
        Assert.Equal(40, control.Rows);
        Assert.Equal(50_000, control.ScrollbackLimit);
        Assert.True(control.PreserveScrollbackOnSessionStart);
        Assert.False(control.AutoScroll);
        Assert.False(control.ScrollToBottomOnInput);
        Assert.False(control.ReflowOnResize);
        Assert.True(control.SixelGraphicsEnabled);
    }

    [AvaloniaFact]
    public void Control_FontRenderingSettings_ApplyToRenderer()
    {
        var control = new TerminalControl
        {
            FontSubpixelPositioning = false,
            FontEdging = TerminalFontEdging.Alias,
            FontHinting = TerminalFontHinting.None,
            FontBaselineSnap = false,
            FontEmbeddedBitmaps = true,
            FontEmbolden = true,
            FontForceAutoHinting = true,
            FontLinearMetrics = true,
        };

        SkiaTerminalRenderer renderer = Assert.IsType<SkiaTerminalRenderer>(control.Renderer);
        TerminalFontRenderingSettings settings = renderer.FontRenderingSettings;
        Assert.False(settings.SubpixelPositioning);
        Assert.Equal(TerminalFontEdging.Alias, settings.Edging);
        Assert.Equal(TerminalFontHinting.None, settings.Hinting);
        Assert.False(settings.BaselineSnap);
        Assert.True(settings.EmbeddedBitmaps);
        Assert.True(settings.Embolden);
        Assert.True(settings.ForceAutoHinting);
        Assert.True(settings.LinearMetrics);
    }

    [AvaloniaFact]
    public void Control_ScrollbackLimit_AppliesToScreenAfterConstruction()
    {
        var control = new TerminalControl
        {
            ScrollbackLimit = 50_000,
        };

        Assert.NotNull(control.Screen);
        Assert.Equal(50_000, control.Screen!.ScrollbackLimit);
    }

    [AvaloniaFact]
    public void Control_ScrollbackLimit_CoercesNegativeValues()
    {
        var control = new TerminalControl
        {
            ScrollbackLimit = -1,
        };

        Assert.Equal(0, control.ScrollbackLimit);
        Assert.NotNull(control.Screen);
        Assert.Equal(0, control.Screen!.ScrollbackLimit);
    }

    [AvaloniaFact]
    public void Control_SixelGraphicsEnabled_RendersManagedRasterPayload_WhenEnabledAfterCreation()
    {
        var control = new TerminalControl
        {
            VtProcessorPreference = VtProcessorPreference.Managed,
        };

        control.SixelGraphicsEnabled = true;
        control.WriteOutput(Encoding.ASCII.GetBytes("\u001bPq#1;2;100;0;0#1@\u001b\\"));

        TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
        Assert.True(screen.HasRasterGraphics);
        ReadOnlySpan<TerminalRasterImagePlacement> placements = screen.GetRasterImagePlacements();
        Assert.Equal(1, placements.Length);
        Assert.True(screen.TryGetRasterImageSource(placements[0].ImageId, out TerminalRasterImageSource? source));
        Assert.Equal(TerminalRasterImageProtocol.Sixel, source!.Protocol);
    }

    [AvaloniaFact]
    public void Control_SixelGraphicsEnabled_DisabledAfterRender_ClearsManagedRasterPayload()
    {
        var control = new TerminalControl
        {
            VtProcessorPreference = VtProcessorPreference.Managed,
            SixelGraphicsEnabled = true,
        };
        control.WriteOutput(Encoding.ASCII.GetBytes("\u001bPq#1;2;100;0;0#1@\u001b\\"));

        TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
        Assert.True(screen.HasRasterGraphics);

        control.SixelGraphicsEnabled = false;

        Assert.False(screen.HasRasterGraphics);
        Assert.True(screen.GetRasterImagePlacements().IsEmpty);
    }

    [AvaloniaFact]
    public async Task Control_SixelGraphicsEnabled_RendersManagedRasterPayload_ToPixels()
    {
        var control = new TerminalControl
        {
            VtProcessorPreference = VtProcessorPreference.Managed,
            SixelGraphicsEnabled = true,
            Width = 800,
            Height = 480,
        };
        Window window = new()
        {
            Content = control,
            Width = 800,
            Height = 480,
        };
        window.Show();

        try
        {
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            control.WriteOutput(CreateSolidRedSixel(width: 80, bands: 10));

            TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
            SkiaTerminalRenderer renderer = Assert.IsType<SkiaTerminalRenderer>(control.Renderer);
            int width = Math.Max(1, (int)Math.Ceiling(screen.Columns * renderer.CellWidth));
            int height = Math.Max(1, (int)Math.Ceiling(screen.ViewportRows * renderer.CellHeight));
            using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888));
            surface.Canvas.Clear(SKColors.Black);

            lock (screen.SyncRoot)
            {
                renderer.RenderFull(surface.Canvas, screen);
            }

            using SKImage snapshot = surface.Snapshot();
            using SKPixmap pixels = snapshot.PeekPixels();
            Assert.NotNull(pixels);
            Assert.True(
                ContainsRedPixel(pixels, maxX: 100, maxY: 80),
                "Rendered sixel raster should produce visible red pixels.");
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_SixelGraphicsEnabled_RetainsManagedRasterPayload_AfterResize()
    {
        var control = new TerminalControl
        {
            VtProcessorPreference = VtProcessorPreference.Managed,
            SixelGraphicsEnabled = true,
            Width = 800,
            Height = 480,
        };
        Window window = new()
        {
            Content = control,
            Width = 800,
            Height = 480,
        };
        window.Show();

        try
        {
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            control.WriteOutput(CreateSolidRedSixel(width: 80, bands: 10));

            TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
            Assert.True(screen.HasRasterGraphics);

            window.Width = 520;
            control.Width = 520;
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();

            Assert.True(screen.HasRasterGraphics);
            SkiaTerminalRenderer renderer = Assert.IsType<SkiaTerminalRenderer>(control.Renderer);
            int width = Math.Max(1, (int)Math.Ceiling(screen.Columns * renderer.CellWidth));
            int height = Math.Max(1, (int)Math.Ceiling(screen.ViewportRows * renderer.CellHeight));
            using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888));
            surface.Canvas.Clear(SKColors.Black);

            lock (screen.SyncRoot)
            {
                renderer.RenderFull(surface.Canvas, screen);
            }

            using SKImage snapshot = surface.Snapshot();
            using SKPixmap pixels = snapshot.PeekPixels();
            Assert.NotNull(pixels);
            Assert.True(
                ContainsRedPixel(pixels, maxX: 100, maxY: 80),
                "Rendered sixel raster should stay visible after terminal resize.");
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
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
    public async Task Control_StartSessionAsync_WithPreserveScrollback_KeepsPreviousHistory()
    {
        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed);
        control.Columns = 16;
        control.Rows = 3;
        control.ScrollbackLimit = 20;

        await control.StartSessionAsync(new FakeTransportOptions("fake"));
        control.WriteOutput("\x1b[31mFIRST\x1b[0m\r\nSECOND"u8);
        control.StopPty();
        await HeadlessTerminalTestCleanup.DrainDispatcherAsync();

        await control.StartSessionAsync(new FakeTransportOptions("fake"), preserveScrollback: true);

        TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
        lock (screen.SyncRoot)
        {
            Assert.True(screen.TotalRows > screen.ViewportRows);
            int rowIndex = FindAbsoluteRowContaining(screen, "FIRST");
            Assert.True(rowIndex >= 0, ReadAllRows(screen));
            Assert.NotEqual(screen.DefaultForeground, screen.GetRow(rowIndex)[0].Foreground);
            Assert.DoesNotContain("FIRST", ReadViewportTextRange(screen, 0, screen.ViewportRows - 1), StringComparison.Ordinal);
        }
    }

    [AvaloniaFact]
    public async Task Control_StartSessionAsync_WithPreserveScrollback_StaysAtLiveBottom_WhenAncestorReplaysOldOffset()
    {
        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed);
        control.Columns = 16;
        control.Rows = 3;
        control.ScrollbackLimit = 20;

        ScrollViewer scrollViewer = new()
        {
            Content = control,
        };
        Window window = new()
        {
            Content = scrollViewer,
            Width = 640,
            Height = 240,
        };
        window.Show();

        try
        {
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            control.Focus();
            HeadlessTerminalTestCleanup.RunDispatcherJobs();

            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            PopulateScrollableNormalBuffer(control);
            control.StopPty();
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();

            await control.StartSessionAsync(new FakeTransportOptions("fake"), preserveScrollback: true);

            // Avalonia can replay an older top-anchored offset after the logical extent grows.
            // The restart path must keep the terminal pinned to the live bottom.
            await Dispatcher.UIThread.InvokeAsync(
                () => ((IScrollable)control).Offset = new Vector(0, 0),
                DispatcherPriority.Input);
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();

            TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
            TerminalScrollData scrollData = Assert.IsType<TerminalScrollData>(control.ScrollData);
            SkiaTerminalRenderer renderer = Assert.IsType<SkiaTerminalRenderer>(control.Renderer);

            lock (screen.SyncRoot)
            {
                Assert.Equal(0, screen.ScrollOffset);
                Assert.True(screen.TotalRows > screen.ViewportRows);
            }

            Assert.True(scrollData.IsAtBottom);
            Assert.True(Math.Abs(scrollViewer.Offset.Y - scrollData.MaxOffset) < 0.5);
            Assert.True(renderer.CursorVisible);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_StartSessionAsync_DefaultClearsPreviousHistory()
    {
        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed);
        control.Columns = 16;
        control.Rows = 3;
        control.ScrollbackLimit = 20;

        await control.StartSessionAsync(new FakeTransportOptions("fake"));
        control.WriteOutput("FIRST\r\nSECOND"u8);
        control.StopPty();
        await HeadlessTerminalTestCleanup.DrainDispatcherAsync();

        await control.StartSessionAsync(new FakeTransportOptions("fake"));

        TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
        lock (screen.SyncRoot)
        {
            Assert.Equal(screen.ViewportRows, screen.TotalRows);
            Assert.DoesNotContain("FIRST", ReadAllRows(screen), StringComparison.Ordinal);
            Assert.Equal(0, screen.MaxScrollOffset);
        }
    }

    [AvaloniaFact]
    public async Task Control_StartSessionAsync_WithCanceledToken_DoesNotClearExistingBuffer()
    {
        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed);
        control.Columns = 16;
        control.Rows = 3;
        control.ScrollbackLimit = 20;
        control.WriteOutput("KEEP\r\nVISIBLE"u8);

        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await control.StartSessionAsync(new FakeTransportOptions("fake"), cts.Token));

        TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
        lock (screen.SyncRoot)
        {
            Assert.Contains("KEEP", ReadAllRows(screen), StringComparison.Ordinal);
            Assert.Contains("VISIBLE", ReadAllRows(screen), StringComparison.Ordinal);
        }

        Assert.False(transport.IsRunning);
    }

    [AvaloniaFact]
    public async Task Control_StartSessionAsync_IgnoresStaleProcessExitPostFromPreviousSession()
    {
        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed);
        control.Columns = 16;
        control.Rows = 3;

        await control.StartSessionAsync(new FakeTransportOptions("fake"));
        transport.RaiseProcessExited(0);
        transport.Dispose();

        await control.StartSessionAsync(new FakeTransportOptions("fake"));
        await HeadlessTerminalTestCleanup.DrainDispatcherAsync();

        Assert.True(control.HasActiveSession);
        Assert.Equal("fake", control.ActiveTransportId);
        TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
        lock (screen.SyncRoot)
        {
            Assert.DoesNotContain("[Process exited", ReadAllRows(screen), StringComparison.Ordinal);
        }
    }

    [AvaloniaFact]
    public async Task Control_ClearScrollback_PreservesViewportAndDropsHistory()
    {
        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed);
        control.Columns = 16;
        control.Rows = 4;
        control.ScrollbackLimit = 20;

        await control.StartSessionAsync(new FakeTransportOptions("fake"));
        PopulateScrollableNormalBuffer(control);

        control.ClearScrollback();

        TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
        lock (screen.SyncRoot)
        {
            Assert.Equal(screen.ViewportRows, screen.TotalRows);
            Assert.Equal(0, screen.MaxScrollOffset);
            Assert.Contains("LINE-063", ReadAllRows(screen), StringComparison.Ordinal);
        }
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
    public async Task Control_TransportOutputLargeRead_SplitsIntoBoundedChunks()
    {
        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed);

        object sync = new();
        List<int> chunkLengths = [];
        control.DataReceived += (_, args) =>
        {
            lock (sync)
            {
                chunkLengths.Add(args.Data.Length);
            }
        };

        byte[] payload = new byte[10_000];
        Array.Fill(payload, (byte)'x');

        try
        {
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            await Task.Run(() => transport.RaiseData(payload));

            bool drained = await WaitUntilAsync(
                () =>
                {
                    lock (sync)
                    {
                        return chunkLengths.Sum() == payload.Length;
                    }
                },
                TimeSpan.FromSeconds(5));

            Assert.True(drained, "Expected all split transport chunks to reach DataReceived.");
            lock (sync)
            {
                Assert.Equal([4096, 4096, 1808], chunkLengths);
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
    public void Control_ScrollbackLimitChange_RecreatesIdleProcessorWithConfiguredLimit()
    {
        TrackingVtProcessorFactory factory = new();
        TerminalControl control = new(
            new TerminalSessionService(),
            new DefaultTerminalInputAdapter(),
            new DefaultTerminalSelectionService(),
            new DefaultTerminalScrollService(),
            factory,
            new DefaultPtyFactory());

        control.ScrollbackLimit = 12_345;

        Assert.NotNull(control.Screen);
        Assert.Equal(12_345, control.Screen!.ScrollbackLimit);
        Assert.True(factory.CreateCallCount >= 2);
        Assert.Equal(12_345, factory.ScrollbackLimits[^1]);
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
    public async Task Control_WindowsPtyManagedResize_UsesLocalReflowLikeWindowsTerminal()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed,
            transportId: TerminalTransportIds.Pty);
        control.Columns = 80;
        control.Rows = 24;
        control.ReflowOnResize = true;

        try
        {
            await control.StartSessionAsync(new FakeTransportOptions(TerminalTransportIds.Pty));

            string line = "COLUMN-000-010-020-030-040-050-060-070-END";
            control.WriteOutput(Encoding.UTF8.GetBytes(line));

            Assert.True(ContainsScreenText(control, "070-END"));

            control.Columns = 32;

            Assert.Equal(32, control.Screen!.Columns);
            Assert.True(ContainsScreenText(control, "070-END"));
            await AssertTransportResizeAsync(transport, resize => resize.Columns == 32);

            control.Columns = 80;

            Assert.Equal(80, control.Screen.Columns);
            Assert.True(ContainsScreenText(control, "070-END"));
            await AssertTransportResizeAsync(transport, resize => resize.Columns == 80);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupControlAsync(control);
        }
    }

    [AvaloniaFact]
    public async Task Control_WindowsPtyManagedResize_PreservesPowerShellTableAndCursor()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed,
            transportId: TerminalTransportIds.Pty);
        control.Columns = 133;
        control.Rows = 33;
        control.ReflowOnResize = true;

        string[] names =
        [
            ".android",
            ".cargo",
            ".codex",
            ".config",
            ".copilot",
            ".dbus-keyrings",
            ".dotnet",
            ".gnupg",
            ".ms-ad",
            ".nuget",
            ".rustup",
            ".skiko",
            ".templateengine",
            ".vscode",
            ".vscode-shared",
            "Contacts",
            "Documents",
            "dotnet",
            "dotTraceSnapshots",
            "Downloads",
            "Dropbox",
            "Favorites",
            "GitHub",
            "iCloudDrive",
            "iCloudPhotos",
            "Links",
            "Music",
            "OneDrive",
            "Pictures",
            "Saved Games",
            "Searches",
            "source",
            "Videos",
        ];
        string[] files =
        [
            ".bash_history",
            ".gitconfig",
            ".lesshst",
            "dotnet-install.sh",
            "java_error_in_rider_12460.log",
            "java_error_in_rider64_26852.log",
            "java_error_in_rider64.hprof",
            "Nowy dokument 1.2023_08_21_12_08_40.0.svg",
        ];

        try
        {
            await control.StartSessionAsync(new FakeTransportOptions(TerminalTransportIds.Pty));

            control.WriteOutput(Encoding.UTF8.GetBytes(BuildPowerShellLsStyleOutput(names, files)));

            foreach (int width in new[] { 120, 100, 80, 60, 51 })
            {
                control.Columns = width;
                control.Rows = 31;
            }

            foreach (int width in new[] { 60, 80, 100, 120, 133 })
            {
                control.Columns = width;
                control.Rows = 33;
            }

            TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
            string screenText = ReadAllRows(screen);
            foreach (string name in names)
            {
                Assert.Contains(name, screenText, StringComparison.Ordinal);
            }

            foreach (string file in files)
            {
                Assert.Contains(file, screenText, StringComparison.Ordinal);
            }

            AssertPowerShellHomeRowsNotDuplicated(names, screenText);
            AssertPowerShellHomeRowsNotDuplicated(files, screenText);
            Assert.DoesNotContain("iCloudhotos", screenText, StringComparison.Ordinal);
            Assert.DoesNotContain(Environment.NewLine + "usic", screenText, StringComparison.Ordinal);
            Assert.DoesNotContain("Saved Games                                                    Searches", screenText, StringComparison.Ordinal);
            SkiaTerminalRenderer renderer = Assert.IsType<SkiaTerminalRenderer>(control.Renderer);
            Assert.InRange(renderer.CursorRow, 0, control.Rows - 1);
            Assert.DoesNotContain(transport.Resizes, resize => resize.Columns == 51);
            await AssertTransportResizeAsync(transport, resize => resize.Columns == 133);
            Assert.DoesNotContain(transport.Resizes, resize => resize.Columns == 51);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupControlAsync(control);
        }
    }

    [AvaloniaFact]
    public async Task Control_WindowsPtyManagedResizeInsideScrollViewer_KeepsLiveBottomOnVerticalResize()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed,
            transportId: TerminalTransportIds.Pty);
        control.ReflowOnResize = true;
        control.AutoScroll = true;

        ScrollViewer scrollViewer = new()
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = control,
        };
        Window window = new()
        {
            Content = scrollViewer,
        };
        window.Show();

        string[] names =
        [
            ".android",
            ".cargo",
            ".codex",
            ".config",
            ".copilot",
            ".dbus-keyrings",
            ".dotnet",
            ".gnupg",
            ".ms-ad",
            ".nuget",
            ".rustup",
            ".skiko",
            ".templateengine",
            ".vscode",
            ".vscode-shared",
            "Contacts",
            "Documents",
            "dotnet",
            "dotTraceSnapshots",
            "Downloads",
            "Dropbox",
            "Favorites",
            "GitHub",
            "iCloudDrive",
            "iCloudPhotos",
            "Links",
            "Music",
            "OneDrive",
            "Pictures",
            "Saved Games",
            "Searches",
            "source",
            "Videos",
        ];
        string[] files =
        [
            ".bash_history",
            ".gitconfig",
            ".lesshst",
            "dotnet-install.sh",
            "java_error_in_rider_12460.log",
            "java_error_in_rider64_26852.log",
            "java_error_in_rider64.hprof",
            "Nowy dokument 1.2023_08_21_12_08_40.0.svg",
        ];

        try
        {
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            SkiaTerminalRenderer renderer = Assert.IsType<SkiaTerminalRenderer>(control.Renderer);
            ArrangeScrollViewerToGrid(scrollViewer, window, renderer, columns: 133, rows: 33);
            await control.StartSessionAsync(new FakeTransportOptions(TerminalTransportIds.Pty));
            control.WriteOutput(Encoding.UTF8.GetBytes(BuildPowerShellLsStyleOutput(names, files)));
            control.ScrollToBottom();
            HeadlessTerminalTestCleanup.RunDispatcherJobs();

            TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
            Assert.Equal(0, screen.ScrollOffset);

            StringBuilder resizeTrace = new();
            foreach (int rows in new[] { 31, 27, 33, 47, 41 })
            {
                resizeTrace.AppendLine(
                    $"before rows={rows}: screen={screen.ScrollOffset}, data={control.ScrollData?.Offset}/{control.ScrollData?.MaxOffset}, viewer={scrollViewer.Offset}, controlRows={control.Rows}");
                ArrangeScrollViewerToGrid(scrollViewer, window, renderer, columns: 133, rows);
                HeadlessTerminalTestCleanup.RunDispatcherJobs();
                resizeTrace.AppendLine(
                    $"after rows={rows}: screen={screen.ScrollOffset}, data={control.ScrollData?.Offset}/{control.ScrollData?.MaxOffset}, viewer={scrollViewer.Offset}, controlRows={control.Rows}");
                Assert.True(
                    screen.ScrollOffset == 0,
                    $"The managed screen must remain pinned to the live bottom while the containing ScrollViewer resizes vertically. Trace:{Environment.NewLine}{resizeTrace}");
                Assert.True(
                    control.ScrollData?.IsAtBottom == true,
                    $"Expected logical scroll data at bottom after rows={rows}. Offset={control.ScrollData?.Offset}, Max={control.ScrollData?.MaxOffset}, ScrollViewerOffset={scrollViewer.Offset}.");
            }

            string viewport = DumpViewportRows(screen);
            Assert.Contains("PS C:\\Users\\wiesl>", viewport, StringComparison.Ordinal);
            Assert.Contains("java_error_in_rider64.hprof", viewport, StringComparison.Ordinal);
            Assert.DoesNotContain(Environment.NewLine + "usic", ReadAllRows(screen), StringComparison.Ordinal);
            AssertPowerShellHomeRowsNotDuplicated(names, ReadAllRows(screen));
            AssertPowerShellHomeRowsNotDuplicated(files, ReadAllRows(screen));
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_WindowsPtyResize_CoalescesTransportUpdatesAndFlushesBeforeInput()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        FakeTransport transport = new()
        {
            EchoInput = false,
        };
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed,
            transportId: TerminalTransportIds.Pty);
        control.Columns = 133;
        control.Rows = 33;
        control.ReflowOnResize = true;

        try
        {
            await control.StartSessionAsync(new FakeTransportOptions(TerminalTransportIds.Pty));

            foreach (int width in new[] { 120, 100, 80, 60, 51, 60, 80, 100, 120, 133 })
            {
                control.Columns = width;
            }

            Assert.Empty(transport.Resizes);

            control.SendInput("ls\r");

            TerminalSessionDimensions resize = Assert.Single(transport.Resizes);
            Assert.Equal(133, resize.Columns);
            Assert.Equal(33, resize.Rows);
            Assert.DoesNotContain(transport.Resizes, dimensions => dimensions.Columns == 51);
            Assert.Single(transport.SentInputs);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupControlAsync(control);
        }
    }

    [AvaloniaFact]
    public void Control_ManagedResize_PreservesLiveBottomWhenRowsShrinkAndGrow()
    {
        TerminalControl control = new()
        {
            Columns = 40,
            Rows = 4,
            AutoScroll = true,
            ReflowOnResize = true,
            VtProcessorPreference = VtProcessorPreference.Managed,
        };

        control.WriteOutput(Encoding.UTF8.GetBytes(
            "line-00\r\nline-01\r\nline-02\r\nline-03\r\nline-04\r\nline-05\r\nPROMPT"));
        control.ScrollToBottom();

        TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
        Assert.True(control.ScrollData!.IsAtBottom);
        Assert.Equal(0, screen.ScrollOffset);

        control.Rows = 3;

        Assert.True(control.ScrollData.IsAtBottom);
        Assert.Equal(0, screen.ScrollOffset);
        Assert.Contains("PROMPT", DumpViewportRows(screen), StringComparison.Ordinal);

        control.Rows = 4;

        Assert.True(control.ScrollData.IsAtBottom);
        Assert.Equal(0, screen.ScrollOffset);
        Assert.Contains("PROMPT", DumpViewportRows(screen), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Control_ManagedResize_ShrinkFromNonScrollableViewportStaysAtLiveBottom()
    {
        TerminalControl control = new()
        {
            Columns = 40,
            Rows = 20,
            AutoScroll = true,
            ReflowOnResize = true,
            VtProcessorPreference = VtProcessorPreference.Managed,
        };

        control.WriteOutput(Encoding.UTF8.GetBytes(
            "line-00\r\nline-01\r\nline-02\r\nline-03\r\nline-04\r\nline-05\r\nPROMPT"));

        TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
        int oldRows = Math.Max(8, screen.TotalRows);
        control.Rows = oldRows;
        control.ScrollToBottom();
        Assert.False(control.ScrollData!.CanScroll);
        Assert.True(control.ScrollData.IsAtBottom);
        Assert.Equal(0, screen.ScrollOffset);

        control.Rows = oldRows - 4;

        string shrinkViewport = DumpViewportRows(screen);
        Assert.True(control.ScrollData.IsAtBottom);
        Assert.Equal(0, screen.ScrollOffset);
        Assert.Contains("line-04", shrinkViewport, StringComparison.Ordinal);
        Assert.Contains("line-05", shrinkViewport, StringComparison.Ordinal);
        Assert.Contains("PROMPT", shrinkViewport, StringComparison.Ordinal);

        control.Rows = oldRows;

        string grownViewport = DumpViewportRows(screen);
        Assert.True(control.ScrollData.IsAtBottom);
        Assert.Equal(0, screen.ScrollOffset);
        Assert.Contains("line-00", grownViewport, StringComparison.Ordinal);
        Assert.Contains("PROMPT", grownViewport, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Control_WindowsPtyRowsIncrease_ExpandsFromLiveBottom()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed,
            transportId: TerminalTransportIds.Pty);
        control.Columns = 20;
        control.Rows = 3;
        control.ReflowOnResize = true;

        try
        {
            await control.StartSessionAsync(new FakeTransportOptions(TerminalTransportIds.Pty));

            control.WriteOutput(Encoding.UTF8.GetBytes(
                "line-00\r\nline-01\r\nline-02\r\nline-03\r\nline-04\r\nline-05\r\nPROMPT"));
            control.ScrollToBottom();

            TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
            Assert.Contains("line-04", ReadRowText(screen.GetViewportRow(0)), StringComparison.Ordinal);
            Assert.Contains("line-05", ReadRowText(screen.GetViewportRow(1)), StringComparison.Ordinal);
            Assert.Contains("PROMPT", ReadRowText(screen.GetViewportRow(2)), StringComparison.Ordinal);

            control.Rows = 5;

            Assert.Contains("line-02", ReadRowText(screen.GetViewportRow(0)), StringComparison.Ordinal);
            Assert.Contains("line-03", ReadRowText(screen.GetViewportRow(1)), StringComparison.Ordinal);
            Assert.Contains("line-04", ReadRowText(screen.GetViewportRow(2)), StringComparison.Ordinal);
            Assert.Contains("line-05", ReadRowText(screen.GetViewportRow(3)), StringComparison.Ordinal);
            Assert.Contains("PROMPT", ReadRowText(screen.GetViewportRow(4)), StringComparison.Ordinal);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupControlAsync(control);
        }
    }

    [AvaloniaFact]
    public async Task Control_WindowsPtyRowsDecrease_AfterRowsIncrease_RemainsAtLiveBottom()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed,
            transportId: TerminalTransportIds.Pty);
        control.Columns = 12;
        control.Rows = 3;
        control.ReflowOnResize = true;

        try
        {
            await control.StartSessionAsync(new FakeTransportOptions(TerminalTransportIds.Pty));

            control.WriteOutput(Encoding.UTF8.GetBytes("old0\r\nold1\r\nold2\r\nMusic\r\nPrompt"));
            control.ScrollToBottom();

            TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
            Assert.Contains("old2", ReadRowText(screen.GetViewportRow(0)), StringComparison.Ordinal);
            Assert.Contains("Music", ReadRowText(screen.GetViewportRow(1)), StringComparison.Ordinal);
            Assert.Contains("Prompt", ReadRowText(screen.GetViewportRow(2)), StringComparison.Ordinal);

            control.Rows = 5;
            control.Rows = 4;

            Assert.Contains("old1", ReadRowText(screen.GetViewportRow(0)), StringComparison.Ordinal);
            Assert.Contains("old2", ReadRowText(screen.GetViewportRow(1)), StringComparison.Ordinal);
            Assert.Contains("Music", ReadRowText(screen.GetViewportRow(2)), StringComparison.Ordinal);
            Assert.Contains("Prompt", ReadRowText(screen.GetViewportRow(3)), StringComparison.Ordinal);

            control.Rows = 3;

            string screenText = ReadAllRows(screen);
            Assert.Contains("old2", ReadRowText(screen.GetViewportRow(0)), StringComparison.Ordinal);
            Assert.Contains("Music", ReadRowText(screen.GetViewportRow(1)), StringComparison.Ordinal);
            Assert.Contains("Prompt", ReadRowText(screen.GetViewportRow(2)), StringComparison.Ordinal);
            Assert.Equal(1, CountOccurrences(screenText, "Music"));
            Assert.Equal(1, CountOccurrences(screenText, "Prompt"));
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupControlAsync(control);
        }
    }

    [AvaloniaFact]
    public async Task Control_WindowsPtyManagedVerticalResize_SuppressesConptyViewportRepaint()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed,
            transportId: TerminalTransportIds.Pty);
        control.Columns = 133;
        control.Rows = 33;
        control.ReflowOnResize = true;
        control.AutoScroll = true;

        string[] names =
        [
            ".android",
            ".cargo",
            ".codex",
            ".config",
            ".copilot",
            ".dbus-keyrings",
            ".dotnet",
            ".gnupg",
            ".ms-ad",
            ".nuget",
            ".rustup",
            ".skiko",
            ".templateengine",
            ".vscode",
            ".vscode-shared",
            "Contacts",
            "Documents",
            "dotnet",
            "dotTraceSnapshots",
            "Downloads",
            "Dropbox",
            "Favorites",
            "GitHub",
            "iCloudDrive",
            "iCloudPhotos",
            "Links",
            "Music",
            "OneDrive",
            "Pictures",
            "Saved Games",
            "Searches",
            "source",
            "Videos",
        ];
        string[] files =
        [
            ".bash_history",
            ".gitconfig",
            ".lesshst",
            "dotnet-install.sh",
            "java_error_in_rider_12460.log",
            "java_error_in_rider64_26852.log",
            "java_error_in_rider64.hprof",
            "Nowy dokument 1.2023_08_21_12_08_40.0.svg",
        ];

        try
        {
            await control.StartSessionAsync(new FakeTransportOptions(TerminalTransportIds.Pty));

            control.WriteOutput(Encoding.UTF8.GetBytes(BuildPowerShellLsStyleOutput(names, files)));
            control.ScrollToBottom();

            TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
            control.Rows = 4;
            control.WriteOutput(Encoding.UTF8.GetBytes(BuildConptyViewportResizeRepaint(includeWindowSize: true)));

            control.Rows = 40;
            control.WriteOutput(Encoding.UTF8.GetBytes(BuildConptyViewportResizeRepaint(includeWindowSize: false)));

            string allRows = ReadAllRows(screen);
            foreach (string name in names)
            {
                Assert.Contains(name, allRows, StringComparison.Ordinal);
            }

            foreach (string file in files)
            {
                Assert.Contains(file, allRows, StringComparison.Ordinal);
            }

            string viewport = DumpViewportRows(screen);
            Assert.Contains("PS C:\\Users\\wiesl>", viewport, StringComparison.Ordinal);
            Assert.Contains("java_error_in_rider64.hprof", viewport, StringComparison.Ordinal);
            Assert.DoesNotContain(Environment.NewLine + "usic", allRows, StringComparison.Ordinal);
            Assert.DoesNotContain("iCloudhotos", allRows, StringComparison.Ordinal);
            AssertPowerShellHomeRowsNotDuplicated(names, allRows);
            AssertPowerShellHomeRowsNotDuplicated(files, allRows);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupControlAsync(control);
        }
    }

    [AvaloniaFact]
    public async Task Control_WindowsPtyManagedRealPwshResize_PreservesPowerShellTableAndCursor()
    {
        if (!OperatingSystem.IsWindows() || !TryResolvePwshPath(out string? pwshPath))
        {
            return;
        }

        CompositeTerminalTransportFactory factory = new(
            new ITerminalTransportProvider[]
            {
                new PtyTerminalTransportProvider(ptyFactory: new DefaultPtyFactory()),
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
            factory)
        {
            VtProcessorPreference = VtProcessorPreference.Managed,
            ReflowOnResize = true,
            AutoScroll = true,
        };
        Window window = new()
        {
            Content = control,
        };
        window.Show();

        try
        {
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            SkiaTerminalRenderer renderer = Assert.IsType<SkiaTerminalRenderer>(control.Renderer);
            ArrangeControlToGrid(control, window, renderer, columns: 133, rows: 33);

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            (int widthPx, int heightPx) = CalculateGridPixels(renderer, columns: 133, rows: 33);
            PtyTransportOptions options = new(
                Command: new TerminalCommandSpec(
                    pwshPath!,
                    [
                        "-NoLogo",
                        "-NoExit",
                    ]),
                WorkingDirectory: home,
                Environment: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["POWERSHELL_UPDATECHECK"] = "Off",
                },
                Dimensions: new TerminalSessionDimensions(133, 33, widthPx, heightPx));

            const string marker = "__RT_DONE__";
            await control.StartSessionAsync(options);
            control.SendInput("ls; Write-Output '" + marker + "'\r");

            Assert.True(
                await WaitUntilAsync(
                    () => ReadAllRowsLocked(control).Contains(marker, StringComparison.Ordinal),
                    TimeSpan.FromSeconds(10)),
                "PowerShell output marker was not observed. Current screen: " + ReadAllRowsLocked(control));
            await WaitForStableScreenAsync(control, TimeSpan.FromSeconds(2));

            string beforeResize = ReadAllRowsLocked(control);
            if (!beforeResize.Contains("iCloudPhotos", StringComparison.Ordinal) ||
                !beforeResize.Contains("Music", StringComparison.Ordinal))
            {
                return;
            }

            string[] expectedRows = GetKnownPowerShellHomeRowsPresentIn(beforeResize);

            foreach (int width in new[] { 120, 100, 80, 60, 51 })
            {
                ArrangeControlToGrid(control, window, renderer, columns: width, rows: 31);
                await WaitForStableScreenAsync(control, TimeSpan.FromMilliseconds(750));
            }

            foreach (int width in new[] { 60, 80, 100, 120, 133 })
            {
                ArrangeControlToGrid(control, window, renderer, columns: width, rows: 33);
                await WaitForStableScreenAsync(control, TimeSpan.FromMilliseconds(750));
            }

            string afterResize = ReadAllRowsLocked(control);
            AssertPowerShellHomeRowsPreserved(expectedRows, afterResize);
            AssertPowerShellHomeRowsNotDuplicated(expectedRows, afterResize);
            Assert.DoesNotContain("iCloudhotos", afterResize, StringComparison.Ordinal);
            Assert.DoesNotContain(Environment.NewLine + "usic", afterResize, StringComparison.Ordinal);
            Assert.DoesNotContain("Saved Games                                                    Searches", afterResize, StringComparison.Ordinal);

            Assert.True(
                control.TryExportSnapshot(
                    TerminalSnapshotExportFormat.StyledVt,
                    CreateStyledSnapshotOptions(),
                    out string styledSnapshot));
            AssertPowerShellHomeRowsPreserved(expectedRows, styledSnapshot);
            AssertPowerShellHomeRowsNotDuplicated(expectedRows, styledSnapshot);
            Assert.DoesNotContain("iCloudhotos", styledSnapshot, StringComparison.Ordinal);
            Assert.DoesNotContain(Environment.NewLine + "usic", styledSnapshot, StringComparison.Ordinal);
            Assert.InRange(renderer.CursorRow, 0, control.Rows - 1);

            const string secondMarker = "__RT_DONE_SECOND__";
            control.SendInput("ls; Write-Output '" + secondMarker + "'\r");
            Assert.True(
                await WaitUntilAsync(
                    () => ReadAllRowsLocked(control).Contains(secondMarker, StringComparison.Ordinal),
                    TimeSpan.FromSeconds(10)),
                "Second PowerShell output marker was not observed. Current screen: " + ReadAllRowsLocked(control));
            await WaitForStableScreenAsync(control, TimeSpan.FromSeconds(2));

            Assert.True(
                control.TryExportSnapshot(
                    TerminalSnapshotExportFormat.PlainText,
                    new TerminalSnapshotExportOptions(Unwrap: true, TrimTrailingWhitespace: true),
                    out string twoRunSnapshot));
            AssertPowerShellHomeRowsPreservedAtLeast(expectedRows, twoRunSnapshot, minimumOccurrences: 2);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_WindowsPtyManagedRapidRealPwshResize_PreservesDeterministicPowerShellTableRows()
    {
        if (!OperatingSystem.IsWindows() || !TryResolvePwshPath(out string? pwshPath))
        {
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), "RoyalTerminal.PwshResize." + Guid.NewGuid().ToString("N"));
        string[] names =
        [
            ".android",
            ".cargo",
            ".codex",
            ".config",
            ".copilot",
            ".dbus-keyrings",
            ".dotnet",
            ".gnupg",
            ".ms-ad",
            ".nuget",
            ".rustup",
            ".skiko",
            ".templateengine",
            ".vscode",
            ".vscode-shared",
            "Contacts",
            "Documents",
            "dotnet",
            "dotTraceSnapshots",
            "Downloads",
            "Dropbox",
            "Favorites",
            "GitHub",
            "iCloudDrive",
            "iCloudPhotos",
            "Links",
            "Music",
            "OneDrive",
            "Pictures",
            "Saved Games",
            "Searches",
            "source",
            "Videos",
        ];
        string[] files =
        [
            ".bash_history",
            ".gitconfig",
            ".lesshst",
            "dotnet-install.sh",
            "java_error_in_rider_12460.log",
            "java_error_in_rider64_26852.log",
            "java_error_in_rider64.hprof",
            "Nowy dokument 1.2023_08_21_12_08_40.0.svg",
        ];

        CreatePowerShellLsFixture(root, names, files);

        CompositeTerminalTransportFactory factory = new(
            new ITerminalTransportProvider[]
            {
                new PtyTerminalTransportProvider(ptyFactory: new DefaultPtyFactory()),
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
            factory)
        {
            VtProcessorPreference = VtProcessorPreference.Managed,
            ReflowOnResize = true,
            AutoScroll = true,
        };
        Window window = new()
        {
            Content = control,
        };
        window.Show();

        try
        {
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            SkiaTerminalRenderer renderer = Assert.IsType<SkiaTerminalRenderer>(control.Renderer);
            ArrangeControlToGrid(control, window, renderer, columns: 133, rows: 33);

            (int widthPx, int heightPx) = CalculateGridPixels(renderer, columns: 133, rows: 33);
            PtyTransportOptions options = new(
                Command: new TerminalCommandSpec(
                    pwshPath!,
                    [
                        "-NoLogo",
                        "-NoExit",
                    ]),
                WorkingDirectory: root,
                Environment: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["POWERSHELL_UPDATECHECK"] = "Off",
                },
                Dimensions: new TerminalSessionDimensions(133, 33, widthPx, heightPx));

            const string marker = "__RT_RAPID_DONE__";
            await control.StartSessionAsync(options);
            control.SendInput("$PSStyle.OutputRendering='Ansi'; Get-ChildItem; Write-Output '" + marker + "'\r");

            Assert.True(
                await WaitUntilAsync(
                    () => ReadAllRowsLocked(control).Contains(marker, StringComparison.Ordinal),
                    TimeSpan.FromSeconds(10)),
                "PowerShell output marker was not observed. Current screen: " + ReadAllRowsLocked(control));
            await WaitForStableScreenAsync(control, TimeSpan.FromSeconds(2));

            string beforeResize = ReadAllRowsLocked(control);
            AssertPowerShellHomeRowsPreserved(names, beforeResize);
            AssertPowerShellHomeRowsPreserved(files, beforeResize);

            for (int cycle = 0; cycle < 3; cycle++)
            {
                foreach (int width in new[] { 120, 100, 80, 60, 51, 60, 80, 100, 120, 133 })
                {
                    int rows = width <= 51 ? 31 : 33;
                    ArrangeControlToGrid(control, window, renderer, columns: width, rows);
                }
            }

            await WaitForStableScreenAsync(control, TimeSpan.FromSeconds(2));

            Assert.True(
                control.TryExportSnapshot(
                    TerminalSnapshotExportFormat.PlainText,
                    new TerminalSnapshotExportOptions(Unwrap: true, TrimTrailingWhitespace: true),
                    out string snapshot));
            AssertPowerShellHomeRowsPreserved(names, snapshot);
            AssertPowerShellHomeRowsPreserved(files, snapshot);
            AssertPowerShellHomeRowsNotDuplicated(names, snapshot);
            AssertPowerShellHomeRowsNotDuplicated(files, snapshot);
            Assert.DoesNotContain("iCloudhotos", snapshot, StringComparison.Ordinal);
            Assert.DoesNotContain(Environment.NewLine + "usic", snapshot, StringComparison.Ordinal);
            Assert.DoesNotContain("Saved Games                                                    Searches", snapshot, StringComparison.Ordinal);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
            TryDeleteDirectory(root);
        }
    }

    [AvaloniaFact]
    public async Task Control_WindowsPtyNativeResize_KeepsLocalProcessorReflowEnabled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        FakeTransport transport = new();
        FakeSearchViewportVtProcessor processor = new(
            new TerminalViewportScrollState(TotalRows: 24, OffsetRows: 0, VisibleRows: 24),
            []);
        TerminalControl control = CreateControlWithTransport(
            transport,
            new SingleProcessorFactory(processor),
            VtProcessorPreference.Native,
            transportId: TerminalTransportIds.Pty);
        control.Columns = 80;
        control.Rows = 24;
        control.ReflowOnResize = true;

        try
        {
            await control.StartSessionAsync(new FakeTransportOptions(TerminalTransportIds.Pty));

            control.Columns = 32;

            Assert.True(processor.LocalReflowOnResize);
            await AssertTransportResizeAsync(transport, resize => resize.Columns == 32);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupControlAsync(control);
        }
    }

    [AvaloniaFact]
    public async Task Control_WindowsPtyNativeResize_PreservesPreResizeMirrorHiddenTail()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        FakeTransport transport = new();
        FakeSearchViewportVtProcessor processor = new(
            new TerminalViewportScrollState(TotalRows: 24, OffsetRows: 0, VisibleRows: 24),
            []);
        TerminalControl control = CreateControlWithTransport(
            transport,
            new SingleProcessorFactory(processor),
            VtProcessorPreference.Native,
            transportId: TerminalTransportIds.Pty);
        control.Columns = 80;
        control.Rows = 24;
        control.ReflowOnResize = true;

        try
        {
            await control.StartSessionAsync(new FakeTransportOptions(TerminalTransportIds.Pty));

            string line = "COLUMN-000-010-020-030-040-050-060-070-END";
            WriteAscii(control.Screen!.GetViewportRow(0), line);

            Assert.True(ContainsScreenText(control, "070-END"));

            control.Columns = 32;

            Assert.Equal(32, control.Screen.Columns);
            Assert.False(ContainsScreenText(control, "070-END"));

            control.Columns = 80;

            Assert.Equal(80, control.Screen.Columns);
            Assert.True(ContainsScreenText(control, "070-END"));
            Assert.DoesNotContain(transport.Resizes, resize => resize.Columns == 32);
            await AssertTransportResizeAsync(transport, resize => resize.Columns == 80);
            Assert.DoesNotContain(transport.Resizes, resize => resize.Columns == 32);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupControlAsync(control);
        }
    }

    [AvaloniaFact]
    public async Task Control_WindowsPtyNativeResize_SuppressesStyledConptyViewportRepaint()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        FakeTransport transport = new();
        FakeSearchViewportVtProcessor processor = new(
            new TerminalViewportScrollState(TotalRows: 80, OffsetRows: 47, VisibleRows: 33),
            []);
        TerminalControl control = CreateControlWithTransport(
            transport,
            new SingleProcessorFactory(processor),
            VtProcessorPreference.Native,
            transportId: TerminalTransportIds.Pty);
        control.Columns = 133;
        control.Rows = 33;
        control.ReflowOnResize = true;
        control.AutoScroll = true;

        try
        {
            await control.StartSessionAsync(new FakeTransportOptions(TerminalTransportIds.Pty));

            control.Columns = 51;
            control.Rows = 31;
            control.WriteOutput(Encoding.UTF8.GetBytes(BuildConptyStyledViewportResizeRepaint(includeWindowSize: true)));

            Assert.Equal(0, processor.ProcessCallCount);

            control.WriteOutput("actual-output"u8.ToArray());

            Assert.Equal(1, processor.ProcessCallCount);
            Assert.Equal("actual-output", Encoding.UTF8.GetString(processor.LastProcessedData!));
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupControlAsync(control);
        }
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
    public void Control_Arrange_PixelOnlyResize_KeepsVtProcessorOnRenderedGridPixels()
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
        Assert.True(resizeCountBefore > 0);

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
        Assert.Equal(resizeCountBefore, processor.ResizeNotifications.Count);

        TerminalSessionDimensions lastResize = processor.ResizeNotifications[^1];
        Assert.Equal(columnsBefore, lastResize.Columns);
        Assert.Equal(rowsBefore, lastResize.Rows);
        int expectedWidthPixels = Math.Max(1, (int)Math.Round(columnsBefore * cellWidth));
        int expectedHeightPixels = Math.Max(1, (int)Math.Round(rowsBefore * cellHeight));
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
    public async Task Control_ManagedTransportResize_DrainsQueuedOutputBeforeResize()
    {
        FakeTransport transport = new();
        ResizeOrderingVtProcessorFactory factory = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            factory,
            VtProcessorPreference.Managed,
            transportId: TerminalTransportIds.Pty);
        control.Columns = 133;
        control.Rows = 33;
        control.ReflowOnResize = true;

        try
        {
            await control.StartSessionAsync(new FakeTransportOptions(TerminalTransportIds.Pty));
            Assert.NotNull(factory.LastProcessor);

            byte[] payload = Encoding.UTF8.GetBytes(new string('x', 12_288));
            Task outputTask = Task.Run(() => transport.RaiseData(payload));

            Assert.True(
                factory.LastProcessor!.FirstProcessStarted.Wait(TimeSpan.FromSeconds(5)),
                "Expected background managed output processing to start.");

            control.Columns = 51;
            await outputTask;

            Assert.True(
                await WaitUntilAsync(
                    () => factory.LastProcessor!.ProcessedChunkCount >= 3,
                    TimeSpan.FromSeconds(5)),
                "Expected all queued managed output chunks to be processed.");

            IReadOnlyList<string> events = factory.LastProcessor.EventsSnapshot;
            int resizeIndex = IndexOfEvent(events, "resize:51");
            int thirdProcessIndex = IndexOfEvent(events, "process:3");

            Assert.True(resizeIndex >= 0, "Expected resize notification for 51 columns. Events: " + string.Join(", ", events));
            Assert.True(thirdProcessIndex >= 0, "Expected third queued output chunk to be processed. Events: " + string.Join(", ", events));
            Assert.True(
                thirdProcessIndex < resizeIndex,
                "Resize must not reflow before already queued managed output has been parsed. Events: " + string.Join(", ", events));
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
    public async Task Control_TextInputWhileScrolledBack_ScrollsToBottom()
    {
        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed);
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

            PopulateScrollableNormalBuffer(control);
            TerminalScreen screen = control.Screen!;
            control.ScrollByRows(-3);
            Assert.True(screen.ScrollOffset > 0);

            window.KeyTextInput("x");

            Assert.Equal(0, screen.ScrollOffset);
            Assert.Contains(transport.SentInputs, static payload =>
                payload.Length == 1 && payload[0] == (byte)'x');
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_EscapeWhileScrolledBack_ScrollsToBottomWithoutSendingEscape()
    {
        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed);
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

            PopulateScrollableNormalBuffer(control);
            TerminalScreen screen = control.Screen!;
            control.ScrollByRows(-3);
            Assert.True(screen.ScrollOffset > 0);
            transport.SentInputs.Clear();

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

            Assert.Equal(0, screen.ScrollOffset);
            Assert.Empty(transport.SentInputs);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_EscapeWhileScrolledBack_WithInputSink_SuppressesPressAndRelease()
    {
        FakeInputEndpoint endpoint = new();
        TerminalControl control = new();
        control.AttachEndpoint(endpoint);
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        window.Show();

        try
        {
            control.Focus();
            HeadlessTerminalTestCleanup.RunDispatcherJobs();

            PopulateScrollableNormalBuffer(control);
            TerminalScreen screen = control.Screen!;
            control.ScrollByRows(-3);
            Assert.True(screen.ScrollOffset > 0);
            endpoint.KeyEvents.Clear();

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            RaiseEscapeKeyUp(control);

            Assert.Equal(0, screen.ScrollOffset);
            Assert.Empty(endpoint.KeyEvents);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control, control.DetachEndpoint);
        }
    }

    [AvaloniaFact]
    public async Task Control_EscapeWithActiveSelection_ClearsSelectionWithoutSendingEscape()
    {
        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed);
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

            SkiaTerminalRenderer renderer = Assert.IsType<SkiaTerminalRenderer>(control.Renderer);
            renderer.SelectionStart = (1, 0);
            renderer.SelectionEnd = (2, 0);
            renderer.SelectionIsRectangle = true;
            Assert.True(control.HasSelection);
            transport.SentInputs.Clear();

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

            Assert.Null(renderer.SelectionStart);
            Assert.Null(renderer.SelectionEnd);
            Assert.False(renderer.SelectionIsRectangle);
            Assert.False(control.HasSelection);
            Assert.Empty(transport.SentInputs);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_EscapeAtBottom_SendsEscape()
    {
        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed);
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

            PopulateScrollableNormalBuffer(control);
            TerminalScreen screen = control.Screen!;
            control.ScrollToBottom();
            Assert.Equal(0, screen.ScrollOffset);
            transport.SentInputs.Clear();

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

            Assert.Equal(0, screen.ScrollOffset);
            Assert.Contains(transport.SentInputs, static payload =>
                payload.Length == 1 && payload[0] == 0x1B);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_EscapeInAlternateScreen_SendsEscape()
    {
        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed);
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

            PopulateScrollableNormalBuffer(control);
            TerminalScreen screen = control.Screen!;
            control.ScrollByRows(-3);
            Assert.True(screen.ScrollOffset > 0);

            control.WriteOutput("\x1b[?1049h\x1b[2JALT-00\r\nALT-01"u8);
            Assert.True(screen.AlternateBufferActive);
            Assert.Equal(0, screen.ScrollOffset);
            transport.SentInputs.Clear();

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

            Assert.True(screen.AlternateBufferActive);
            Assert.Equal(0, screen.ScrollOffset);
            Assert.Contains(transport.SentInputs, static payload =>
                payload.Length == 1 && payload[0] == 0x1B);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_EscapeAtBottom_WithInputSink_SendsPressAndRelease()
    {
        FakeInputEndpoint endpoint = new();
        TerminalControl control = new();
        control.AttachEndpoint(endpoint);
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = control,
        };
        window.Show();

        try
        {
            control.Focus();
            HeadlessTerminalTestCleanup.RunDispatcherJobs();

            PopulateScrollableNormalBuffer(control);
            TerminalScreen screen = control.Screen!;
            control.ScrollToBottom();
            Assert.Equal(0, screen.ScrollOffset);
            endpoint.KeyEvents.Clear();

            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            RaiseEscapeKeyUp(control);

            Assert.Equal(0, screen.ScrollOffset);
            Assert.Equal(2, endpoint.KeyEvents.Count);
            Assert.Equal(TerminalInputAction.Press, endpoint.KeyEvents[0].Action);
            Assert.Equal((uint)Key.Escape, endpoint.KeyEvents[0].KeyCode);
            Assert.Equal(TerminalInputAction.Release, endpoint.KeyEvents[1].Action);
            Assert.Equal((uint)Key.Escape, endpoint.KeyEvents[1].KeyCode);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control, control.DetachEndpoint);
        }
    }

    [AvaloniaFact]
    public async Task Control_TextInputWhileScrolledBack_WhenScrollToBottomOnInputDisabled_PreservesViewport()
    {
        FakeTransport transport = new()
        {
            EchoInput = false,
        };
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed);
        control.ScrollToBottomOnInput = false;
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

            PopulateScrollableNormalBuffer(control);
            TerminalScreen screen = control.Screen!;
            control.ScrollByRows(-3);
            int scrollOffset = screen.ScrollOffset;
            Assert.True(scrollOffset > 0);

            window.KeyTextInput("x");

            Assert.Equal(scrollOffset, screen.ScrollOffset);
            Assert.Contains(transport.SentInputs, static payload =>
                payload.Length == 1 && payload[0] == (byte)'x');
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_AfterInputScrollsToBottom_CanScrollBackAgain()
    {
        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new DefaultVtProcessorFactory(),
            VtProcessorPreference.Managed);
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

            PopulateScrollableNormalBuffer(control);
            TerminalScreen screen = control.Screen!;
            control.ScrollByRows(-3);
            Assert.True(screen.ScrollOffset > 0);

            window.KeyTextInput("x");
            Assert.Equal(0, screen.ScrollOffset);

            control.ScrollByRows(-3);

            Assert.True(screen.ScrollOffset > 0);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public void Control_NativeViewportOutputWhileScrolledBack_PreservesViewport()
    {
        FakeSearchViewportVtProcessor processor = new(
            new TerminalViewportScrollState(TotalRows: 100, OffsetRows: 96, VisibleRows: 4),
            []);
        processor.ScrollToBottomOnProcess = true;
        TerminalControl control = CreateControlWithTransport(
            new FakeTransport(),
            new SingleProcessorFactory(processor),
            VtProcessorPreference.Native);

        control.WriteOutput("initial\n"u8);
        control.ScrollByRows(-3);
        Assert.Equal(93UL, processor.ViewportScrollState.OffsetRows);

        control.WriteOutput("output\n"u8);

        Assert.Equal(93UL, processor.ViewportScrollState.OffsetRows);
    }

    [AvaloniaFact]
    public async Task Control_NativeViewportWheelInsideScrollViewer_ScrollsAwayFromBottom()
    {
        FakeSearchViewportVtProcessor processor = new(
            new TerminalViewportScrollState(TotalRows: 100, OffsetRows: 96, VisibleRows: 4),
            []);
        TerminalControl control = CreateControlWithTransport(
            new FakeTransport(),
            new SingleProcessorFactory(processor),
            VtProcessorPreference.Native);
        ScrollViewer scrollViewer = new()
        {
            Width = 640,
            Height = 400,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = control,
        };
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = scrollViewer,
        };
        window.Show();

        try
        {
            await WaitUntilAsync(
                () => control.Bounds.Width > 1 && control.Bounds.Height > 1,
                TimeSpan.FromSeconds(2));

            control.WriteOutput("initial\n"u8);
            control.ScrollToBottom();
            HeadlessTerminalTestCleanup.RunDispatcherJobs();
            Assert.Equal(processor.ViewportScrollState.MaxOffsetRows, processor.ViewportScrollState.OffsetRows);

            Point? translated = control.TranslatePoint(
                new Point(control.Bounds.Width * 0.5, control.Bounds.Height * 0.5),
                window);
            Assert.True(translated.HasValue);
            Point point = translated.Value;
            window.MouseWheel(point, new Vector(0, 1), RawInputModifiers.None);
            HeadlessTerminalTestCleanup.RunDispatcherJobs();

            Assert.True(processor.ScrollViewportByRowsCallCount > 0);
            Assert.True(
                processor.ViewportScrollState.OffsetRows < processor.ViewportScrollState.MaxOffsetRows,
                $"Expected wheel-up to leave bottom. State={processor.ViewportScrollState}, Calls={processor.ScrollViewportByRowsCallCount}, LastSet={processor.LastSetViewportOffsetRows}, ScrollViewerOffset={scrollViewer.Offset}.");
            Assert.True(
                scrollViewer.Offset.Y < scrollViewer.Extent.Height - scrollViewer.Viewport.Height,
                $"Expected ScrollViewer offset to leave bottom. Offset={scrollViewer.Offset}, Extent={scrollViewer.Extent}, Viewport={scrollViewer.Viewport}.");
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_NativeViewportTextInputAfterWheelScroll_UpdatesScrollViewerToBottom()
    {
        FakeSearchViewportVtProcessor processor = new(
            new TerminalViewportScrollState(TotalRows: 100, OffsetRows: 96, VisibleRows: 4),
            []);
        FakeTransport transport = new();
        TerminalControl control = CreateControlWithTransport(
            transport,
            new SingleProcessorFactory(processor),
            VtProcessorPreference.Native);
        ScrollViewer scrollViewer = new()
        {
            Width = 640,
            Height = 400,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = control,
        };
        Window window = new()
        {
            Width = 640,
            Height = 400,
            Content = scrollViewer,
        };
        window.Show();

        try
        {
            await control.StartSessionAsync(new FakeTransportOptions("fake"));
            control.Focus();
            await WaitUntilAsync(
                () => control.Bounds.Width > 1 && control.Bounds.Height > 1,
                TimeSpan.FromSeconds(2));

            control.WriteOutput("initial\n"u8);
            control.ScrollToBottom();
            HeadlessTerminalTestCleanup.RunDispatcherJobs();

            Point? translated = control.TranslatePoint(
                new Point(control.Bounds.Width * 0.5, control.Bounds.Height * 0.5),
                window);
            Assert.True(translated.HasValue);
            window.MouseWheel(translated.Value, new Vector(0, 1), RawInputModifiers.None);
            HeadlessTerminalTestCleanup.RunDispatcherJobs();
            Assert.True(processor.ViewportScrollState.OffsetRows < processor.ViewportScrollState.MaxOffsetRows);
            Assert.True(scrollViewer.Offset.Y < scrollViewer.Extent.Height - scrollViewer.Viewport.Height);

            window.KeyTextInput("x");
            HeadlessTerminalTestCleanup.RunDispatcherJobs();

            Assert.Equal(processor.ViewportScrollState.MaxOffsetRows, processor.ViewportScrollState.OffsetRows);
            Assert.True(
                Math.Abs(scrollViewer.Offset.Y - (scrollViewer.Extent.Height - scrollViewer.Viewport.Height)) < 1d,
                $"Expected ScrollViewer offset at bottom. Offset={scrollViewer.Offset}, Extent={scrollViewer.Extent}, Viewport={scrollViewer.Viewport}.");
            Assert.Contains(transport.SentInputs, static payload =>
                payload.Length == 1 && payload[0] == (byte)'x');
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
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
    public void Control_AlternateScreenExit_DropsTuiArtifactsFromPrimaryScrollback()
    {
        TerminalControl control = new()
        {
            Columns = 24,
            Rows = 5,
            VtProcessorPreference = VtProcessorPreference.Managed,
        };

        Assert.NotNull(control.ScrollData);
        Assert.NotNull(control.Screen);
        TerminalScreen screen = control.Screen!;

        for (int i = 0; i < 16; i++)
        {
            control.WriteOutput(Encoding.UTF8.GetBytes($"MAIN-{i:00}\r\n"));
        }

        int primaryRows = screen.TotalRows;
        double primaryMaxOffset = control.ScrollData!.MaxOffset;

        control.WriteOutput("\x1b[?1049h\x1b[2JLeft File Command Options Right\r\nALT-PANEL\r\nALT-STATUS"u8);

        Assert.True(screen.AlternateBufferActive);
        Assert.Contains("ALT-", string.Join('\n', ReadVisibleAsciiRows(screen)), StringComparison.Ordinal);
        Assert.Equal(0, control.ScrollData.MaxOffset);

        control.WriteOutput("\x1b[?1049l"u8);

        string visible = string.Join('\n', ReadVisibleAsciiRows(screen));
        Assert.False(screen.AlternateBufferActive);
        Assert.Equal(primaryRows, screen.TotalRows);
        Assert.Equal(primaryMaxOffset, control.ScrollData.MaxOffset);
        Assert.DoesNotContain("ALT-", visible, StringComparison.Ordinal);
        Assert.DoesNotContain("Left File", visible, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Control_AlternateScreenProcessorState_SuspendsTextHighlightingWithoutScreenFlag()
    {
        // Reference terminals treat alternate-screen content as the active app-owned buffer.
        // xterm.js exposes normal/alternate buffers separately, Windows Terminal fixes the
        // viewport to the alt buffer, and Ghostty formats only the active screen.
        AlternateScreenOnlyVtProcessor processor = new();
        TerminalControl control = CreateControlWithTransport(
            new FakeTransport(),
            new SingleProcessorFactory(processor),
            VtProcessorPreference.Managed);
        control.TextHighlightingMode = TerminalTextHighlightingMode.Realtime;
        control.TextHighlightRules =
        [
            new TerminalTextHighlightRule
            {
                Name = "Uppercase",
                Pattern = "[A-Z]{2,}",
                Background = 0xFFFFFFCC,
            },
        ];

        Assert.NotNull(control.Renderer);
        Assert.NotNull(control.Screen);
        Assert.False(control.Screen!.AlternateBufferActive);
        Assert.Equal(TerminalTextHighlightingMode.Realtime, control.Renderer!.TextHighlightingMode);

        control.WriteOutput("alt-on"u8);

        Assert.True(processor.AlternateScreen);
        Assert.False(control.Screen.AlternateBufferActive);
        Assert.Equal(TerminalTextHighlightingMode.Realtime, control.TextHighlightingMode);
        Assert.Equal(TerminalTextHighlightingMode.Disabled, control.Renderer.TextHighlightingMode);

        control.WriteOutput("alt-off"u8);

        Assert.False(processor.AlternateScreen);
        Assert.Equal(TerminalTextHighlightingMode.Realtime, control.TextHighlightingMode);
        Assert.Equal(TerminalTextHighlightingMode.Realtime, control.Renderer.TextHighlightingMode);
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

        Assert.Equal(10UL, processor.LastSetViewportOffsetRows);
        Assert.Equal(10UL, processor.ViewportScrollState.OffsetRows);
        Assert.Equal(1, control.SearchTotal);
        Assert.Equal(0, control.SearchSelected);
    }

    [AvaloniaFact]
    public void Control_FontSizeChange_NativeViewportScroll_UsesNativeTotalRows_NotViewportMirror()
    {
        FakeSearchViewportVtProcessor processor = new(
            new TerminalViewportScrollState(TotalRows: 100, OffsetRows: 80, VisibleRows: 4),
            []);
        TerminalControl control = CreateControlWithTransport(
            new FakeTransport(),
            new SingleProcessorFactory(processor),
            VtProcessorPreference.Native);
        control.Columns = 8;
        control.Rows = 4;

        Assert.NotNull(control.ScrollData);
        processor.SetViewportState(new TerminalViewportScrollState(TotalRows: 100, OffsetRows: 80, VisibleRows: 4));
        double originalCellHeight = control.ScrollData!.CellHeight;
        control.ScrollData.Viewport = 4 * originalCellHeight;
        control.ScrollData.Extent = 100 * originalCellHeight;
        control.ScrollData.Offset = 80 * originalCellHeight;
        processor.ClearLastSetViewportOffsetRows();

        control.TerminalFontSize = 15;

        Assert.Null(processor.LastSetViewportOffsetRows);
        Assert.Equal(100 * control.ScrollData.CellHeight, control.ScrollData.Extent);
        Assert.Equal(80 * control.ScrollData.CellHeight, control.ScrollData.Offset);
    }

    [AvaloniaFact]
    public void Control_FontSizeChange_NativeViewportScroll_PreservesLiveBottom()
    {
        FakeSearchViewportVtProcessor processor = new(
            new TerminalViewportScrollState(TotalRows: 100, OffsetRows: 0, VisibleRows: 4),
            []);
        TerminalControl control = CreateControlWithTransport(
            new FakeTransport(),
            new SingleProcessorFactory(processor),
            VtProcessorPreference.Native);
        control.Columns = 8;
        control.Rows = 4;

        Assert.NotNull(control.ScrollData);
        double cellHeight = control.ScrollData!.CellHeight;
        control.ScrollData.Viewport = 4 * cellHeight;
        control.ScrollData.Extent = 100 * cellHeight;
        control.ScrollData.ScrollToBottom();

        control.TerminalFontSize = 15;
        control.Measure(new Size(960, 640));
        control.Arrange(new Rect(0, 0, 960, 640));

        Assert.True(processor.ScrolledToBottom);
        Assert.Equal(processor.ViewportScrollState.MaxOffsetRows, processor.ViewportScrollState.OffsetRows);
        Assert.True(control.ScrollData.IsAtBottom);
    }

    [AvaloniaFact]
    public void Control_ArrangeAfterFontSizeChange_NativeViewportScroll_PreservesLiveBottom()
    {
        FakeSearchViewportVtProcessor processor = new(
            new TerminalViewportScrollState(TotalRows: 100, OffsetRows: 96, VisibleRows: 4),
            [])
        {
            ResetViewportToTopOnResize = true,
        };
        TerminalControl control = CreateControlWithTransport(
            new FakeTransport(),
            new SingleProcessorFactory(processor),
            VtProcessorPreference.Native);
        control.Columns = 8;
        control.Rows = 4;

        Assert.NotNull(control.ScrollData);
        double cellHeight = control.ScrollData!.CellHeight;
        control.ScrollData.Viewport = 4 * cellHeight;
        control.ScrollData.Extent = 100 * cellHeight;
        control.ScrollData.ScrollToBottom();

        control.TerminalFontSize = 15;
        processor.ScrolledToBottom = false;
        control.Measure(new Size(960, 640));
        control.Arrange(new Rect(0, 0, 960, 640));

        Assert.True(processor.ScrolledToBottom);
        Assert.Equal(processor.ViewportScrollState.MaxOffsetRows, processor.ViewportScrollState.OffsetRows);
        Assert.True(control.ScrollData.IsAtBottom);
    }

    [AvaloniaFact]
    public void Control_ArrangeAfterFontSizeChange_NativeViewportScroll_DefersBottomUntilNativeResize()
    {
        FakeSearchViewportVtProcessor processor = new(
            new TerminalViewportScrollState(TotalRows: 56, OffsetRows: 8, VisibleRows: 48),
            []);
        TerminalControl control = CreateControlWithTransport(
            new FakeTransport(),
            new SingleProcessorFactory(processor),
            VtProcessorPreference.Native);
        control.Columns = 130;
        control.Rows = 48;

        Assert.NotNull(control.ScrollData);
        double cellHeight = control.ScrollData!.CellHeight;
        control.ScrollData.Viewport = 48 * cellHeight;
        control.ScrollData.Extent = 56 * cellHeight;
        control.ScrollData.ScrollToBottom();

        control.TerminalFontSize = 15;
        processor.ScrolledToBottom = false;
        control.Measure(new Size(1172, 890));
        control.Arrange(new Rect(0, 0, 1172, 890));

        Assert.True(processor.ScrolledToBottom);
        Assert.Equal(processor.ViewportScrollState.MaxOffsetRows, processor.ViewportScrollState.OffsetRows);
        Assert.True(control.ScrollData.IsAtBottom);
    }

    [AvaloniaFact]
    public void Control_ArrangeAfterFontSizeChange_UsesRowAlignedScrollViewport()
    {
        FakeSearchViewportVtProcessor processor = new(
            new TerminalViewportScrollState(TotalRows: 99, OffsetRows: 55, VisibleRows: 44),
            []);
        TerminalControl control = CreateControlWithTransport(
            new FakeTransport(),
            new SingleProcessorFactory(processor),
            VtProcessorPreference.Native);
        control.Columns = 130;
        control.Rows = 44;
        control.TerminalFontSize = 15;

        control.Measure(new Size(1172, 890));
        control.Arrange(new Rect(0, 0, 1172, 890));

        Assert.NotNull(control.ScrollData);
        Assert.Equal(control.Rows * control.ScrollData!.CellHeight, control.ScrollData.Viewport, precision: 6);
        Assert.True(control.Bounds.Height > control.ScrollData.Viewport);
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
    public async Task Control_CopySelection_RectangularSelection_UsesColumnBand()
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

            SetViewportRowText(screen!, rowIndex: 0, "ABCD");
            SetViewportRowText(screen!, rowIndex: 1, "EFGH");

            renderer!.SelectionStart = (1, 0);
            renderer.SelectionEnd = (3, 1);
            renderer.SelectionIsRectangle = true;

            await control.CopySelectionAsync();

            string? copied = await window.Clipboard!.TryGetTextAsync();
            Assert.Equal($"BC{Environment.NewLine}FG", copied);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_CopySelection_RectangularSelection_NormalizesReversedColumns()
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

            SetViewportRowText(screen!, rowIndex: 0, "ABCD");
            SetViewportRowText(screen!, rowIndex: 1, "EFGH");

            renderer!.SelectionStart = (3, 0);
            renderer.SelectionEnd = (1, 1);
            renderer.SelectionIsRectangle = true;

            await control.CopySelectionAsync();

            string? copied = await window.Clipboard!.TryGetTextAsync();
            Assert.Equal($"BC{Environment.NewLine}FG", copied);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_CopySelection_UsesRendererSelectionInsteadOfActiveExporter()
    {
        TrackingVtProcessor processor = new()
        {
            SelectionExportText = "WHOLE TERMINAL TEXT",
        };
        TerminalControl control = CreateControlWithTransport(
            new FakeTransport(),
            new SingleProcessorFactory(processor),
            VtProcessorPreference.Managed);
        Window window = new() { Content = control };
        window.Show();

        try
        {
            TerminalScreen? screen = control.Screen;
            SkiaTerminalRenderer? renderer = control.Renderer;
            Assert.NotNull(screen);
            Assert.NotNull(renderer);

            SetViewportRowText(screen!, rowIndex: 0, "ABCD");
            SetViewportRowText(screen!, rowIndex: 1, "EFGH");

            renderer!.SelectionStart = (1, 0);
            renderer.SelectionEnd = (3, 1);
            renderer.SelectionIsRectangle = true;

            await control.CopySelectionAsync();

            string? copied = await window.Clipboard!.TryGetTextAsync();
            Assert.Equal($"BC{Environment.NewLine}FG", copied);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_SelectAll_UsesLinearEndExclusiveRendererSelection()
    {
        TerminalControl control = new()
        {
            Columns = 4,
            Rows = 1,
        };
        Window window = new() { Content = control };
        window.Show();

        try
        {
            TerminalScreen? screen = control.Screen;
            SkiaTerminalRenderer? renderer = control.Renderer;
            Assert.NotNull(screen);
            Assert.NotNull(renderer);
            renderer!.SelectionIsRectangle = true;

            control.SelectAll();

            Assert.Equal((0, 0), renderer.SelectionStart);
            Assert.Equal((screen!.Columns, screen.ViewportRows - 1), renderer.SelectionEnd);
            Assert.False(renderer.SelectionIsRectangle);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_CopySelection_RendererSelectionDoesNotFallbackToSessionSelection()
    {
        FakeInputEndpoint endpoint = new()
        {
            HasSelection = true,
            SelectionText = "SESSION-SELECTION",
        };
        TerminalControl control = new();
        control.AttachEndpoint(endpoint);
        Window window = new() { Content = control };
        window.Show();

        try
        {
            SkiaTerminalRenderer renderer = Assert.IsType<SkiaTerminalRenderer>(control.Renderer);
            renderer.SelectionStart = (0, 999);
            renderer.SelectionEnd = (0, 999);
            await window.Clipboard!.SetTextAsync("before");

            await control.CopySelectionAsync();

            string? copied = await window.Clipboard!.TryGetTextAsync();
            Assert.Equal("before", copied);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control, control.DetachEndpoint);
        }
    }

    [AvaloniaFact]
    public async Task Control_CopySelection_SelectionRowsOutsideViewport_UsesScrollbackRows()
    {
        TerminalControl control = new()
        {
            VtProcessorPreference = VtProcessorPreference.Managed,
        };
        Window window = new() { Content = control };
        window.Show();

        try
        {
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            PopulateScrollableNormalBuffer(control);
            control.ScrollByRows(-5);

            TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
            SkiaTerminalRenderer renderer = Assert.IsType<SkiaTerminalRenderer>(control.Renderer);
            int topRow = screen.ViewportTopAbsoluteRow;
            Assert.True(topRow > 0);

            renderer.SelectionStart = (0, -1);
            renderer.SelectionEnd = (4, 1);
            renderer.SelectionIsRectangle = true;

            await control.CopySelectionAsync();

            string expected = string.Join(
                Environment.NewLine,
                ReadRowPrefix(screen.GetRow(topRow - 1), 4),
                ReadRowPrefix(screen.GetRow(topRow), 4),
                ReadRowPrefix(screen.GetRow(topRow + 1), 4));
            string? copied = await window.Clipboard!.TryGetTextAsync();
            Assert.Equal(expected, copied);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_AltDragSelection_SetsRectangularSelection()
    {
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
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            SkiaTerminalRenderer renderer = Assert.IsType<SkiaTerminalRenderer>(control.Renderer);
            Point start = new(renderer.CellWidth * 1.5, renderer.CellHeight * 0.5);
            Point end = new(renderer.CellWidth * 3.5, renderer.CellHeight * 1.5);

            window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton | RawInputModifiers.Alt);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton | RawInputModifiers.Alt);
            window.MouseUp(end, MouseButton.Left, RawInputModifiers.Alt);
            HeadlessTerminalTestCleanup.RunDispatcherJobs();

            Assert.Equal((1, 0), renderer.SelectionStart);
            Assert.Equal((3, 1), renderer.SelectionEnd);
            Assert.True(renderer.SelectionIsRectangle);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_RectangularSelection_ScrollsWithOriginalRows()
    {
        TerminalControl control = new()
        {
            Width = 640,
            Height = 200,
            VtProcessorPreference = VtProcessorPreference.Managed,
        };
        Window window = new()
        {
            Width = 640,
            Height = 200,
            Content = control,
        };
        window.Show();

        try
        {
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            PopulateScrollableNormalBuffer(control);
            control.ScrollToBottom();

            TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
            SkiaTerminalRenderer renderer = Assert.IsType<SkiaTerminalRenderer>(control.Renderer);
            Assert.True(screen.ViewportRows > 3);

            const int startColumn = 5;
            const int endColumnExclusive = 8;
            const int startViewportRow = 1;
            const int endViewportRow = 2;
            int selectedTopAbsoluteRow = screen.ViewportTopAbsoluteRow;
            string expected = string.Join(
                Environment.NewLine,
                ReadRowSlice(screen.GetRow(selectedTopAbsoluteRow + startViewportRow), startColumn, endColumnExclusive),
                ReadRowSlice(screen.GetRow(selectedTopAbsoluteRow + endViewportRow), startColumn, endColumnExclusive));

            renderer.SelectionStart = (startColumn, startViewportRow);
            renderer.SelectionEnd = (endColumnExclusive, endViewportRow);
            renderer.SelectionIsRectangle = true;

            Assert.Equal((startColumn, startViewportRow), renderer.SelectionStart);
            Assert.Equal((endColumnExclusive, endViewportRow), renderer.SelectionEnd);
            Assert.True(renderer.SelectionIsRectangle);

            int scrollRows = Math.Min(screen.MaxScrollOffset, screen.ViewportRows + 2);
            Assert.True(scrollRows > screen.ViewportRows);

            control.ScrollByRows(-scrollRows);

            int rowDelta = selectedTopAbsoluteRow - screen.ViewportTopAbsoluteRow;
            Assert.True(rowDelta > 0);
            Assert.Equal((startColumn, startViewportRow + rowDelta), renderer.SelectionStart);
            Assert.Equal((endColumnExclusive, endViewportRow + rowDelta), renderer.SelectionEnd);
            (int Column, int Row) shiftedSelectionStart = renderer.SelectionStart.GetValueOrDefault();
            Assert.True(shiftedSelectionStart.Row >= screen.ViewportRows);

            await control.CopySelectionAsync();
            string? copiedWhileOffscreen = await window.Clipboard!.TryGetTextAsync();
            Assert.Equal(expected, copiedWhileOffscreen);

            control.ScrollToBottom();

            Assert.Equal((startColumn, startViewportRow), renderer.SelectionStart);
            Assert.Equal((endColumnExclusive, endViewportRow), renderer.SelectionEnd);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_RectangularSelection_TracksOriginalContentAcrossReflowResize()
    {
        TerminalControl control = new()
        {
            VtProcessorPreference = VtProcessorPreference.Managed,
        };
        Window window = new()
        {
            Width = 160,
            Height = 80,
            Content = control,
        };
        window.Show();

        try
        {
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
            SkiaTerminalRenderer renderer = Assert.IsType<SkiaTerminalRenderer>(control.Renderer);
            ArrangeControlToGrid(control, window, renderer, columns: 12, rows: 3);
            Assert.Equal(12, screen.Columns);

            SetViewportRowText(screen, rowIndex: 0, "ABCDEFGHIJKL");

            renderer.SelectionStart = (8, 0);
            renderer.SelectionEnd = (12, 0);
            renderer.SelectionIsRectangle = true;

            ArrangeControlToGrid(control, window, renderer, columns: 6, rows: 3);

            Assert.Equal(6, screen.Columns);
            Assert.True(screen.GetViewportRow(0).WrapsToNext);
            Assert.Equal((2, 1), renderer.SelectionStart);
            Assert.Equal((6, 1), renderer.SelectionEnd);

            await control.CopySelectionAsync();
            string? copiedAfterShrink = await window.Clipboard!.TryGetTextAsync();
            Assert.Equal("IJKL", copiedAfterShrink);

            ArrangeControlToGrid(control, window, renderer, columns: 12, rows: 3);

            Assert.Equal(12, screen.Columns);
            Assert.Equal((8, 0), renderer.SelectionStart);
            Assert.Equal((12, 0), renderer.SelectionEnd);

            await control.CopySelectionAsync();
            string? copiedAfterRestore = await window.Clipboard!.TryGetTextAsync();
            Assert.Equal("IJKL", copiedAfterRestore);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_NativeViewportSelectionResize_PreservesNativeAbsoluteRows()
    {
        FakeSearchViewportVtProcessor processor = new(
            new TerminalViewportScrollState(TotalRows: 120, OffsetRows: 80, VisibleRows: 24),
            []);
        TerminalControl control = CreateControlWithTransport(
            new FakeTransport(),
            new SingleProcessorFactory(processor),
            VtProcessorPreference.Native);
        control.Columns = 80;
        control.Rows = 24;
        Window window = new() { Content = control };
        window.Show();

        try
        {
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
            SkiaTerminalRenderer renderer = Assert.IsType<SkiaTerminalRenderer>(control.Renderer);
            processor.SetViewportState(new TerminalViewportScrollState(
                TotalRows: 120,
                OffsetRows: 80,
                VisibleRows: (ulong)screen.ViewportRows));

            renderer.SelectionStart = (1, 2);
            renderer.SelectionEnd = (4, 2);

            control.Columns = 40;

            Assert.Equal((1, 2), renderer.SelectionStart);
            Assert.Equal((4, 2), renderer.SelectionEnd);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_NativeViewportSelectionResize_ReflowsVisibleSelectionAnchors()
    {
        FakeSearchViewportVtProcessor processor = new(
            new TerminalViewportScrollState(TotalRows: 120, OffsetRows: 80, VisibleRows: 3),
            []);
        TerminalControl control = CreateControlWithTransport(
            new FakeTransport(),
            new SingleProcessorFactory(processor),
            VtProcessorPreference.Native);
        Window window = new()
        {
            Width = 160,
            Height = 80,
            Content = control,
        };
        window.Show();

        try
        {
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
            SkiaTerminalRenderer renderer = Assert.IsType<SkiaTerminalRenderer>(control.Renderer);
            ArrangeControlToGrid(control, window, renderer, columns: 12, rows: 3);
            processor.SetViewportState(new TerminalViewportScrollState(
                TotalRows: 120,
                OffsetRows: 80,
                VisibleRows: (ulong)screen.ViewportRows));
            SetViewportRowText(screen, rowIndex: 0, "ABCDEFGHIJKL");

            renderer.SelectionStart = (8, 0);
            renderer.SelectionEnd = (12, 0);

            ArrangeControlToGrid(control, window, renderer, columns: 6, rows: 3);

            (int Column, int Row) selectionStart = renderer.SelectionStart.GetValueOrDefault();
            (int Column, int Row) selectionEnd = renderer.SelectionEnd.GetValueOrDefault();
            Assert.Equal(2, selectionStart.Column);
            Assert.Equal(6, selectionEnd.Column);
            Assert.Equal(selectionStart.Row, selectionEnd.Row);
            Assert.Equal(
                "IJKL",
                ReadRowSlice(screen.GetViewportRow(selectionStart.Row), selectionStart.Column, selectionEnd.Column));
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_NativeViewportRectangularSelectionResize_TracksSelectionSpanningMoreThanViewport()
    {
        FakeSearchViewportVtProcessor processor = new(
            new TerminalViewportScrollState(TotalRows: 14, OffsetRows: 5, VisibleRows: 5),
            []);
        TerminalControl control = CreateControlWithTransport(
            new FakeTransport(),
            new SingleProcessorFactory(processor),
            VtProcessorPreference.Native);
        Window window = new()
        {
            Width = 320,
            Height = 120,
            Content = control,
        };
        window.Show();

        try
        {
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
            SkiaTerminalRenderer renderer = Assert.IsType<SkiaTerminalRenderer>(control.Renderer);
            ArrangeControlToGrid(control, window, renderer, columns: 24, rows: 5);
            processor.SetViewportState(new TerminalViewportScrollState(
                TotalRows: 14,
                OffsetRows: 5,
                VisibleRows: (ulong)screen.ViewportRows));

            TerminalScreen nativeSnapshot = new(24, 14, scrollbackLimit: 0);
            for (int row = 0; row < nativeSnapshot.TotalRows; row++)
            {
                SetRowText(nativeSnapshot.GetRow(row), $"ROW{row:00}-ABCDEFGHIJKLMNO");
            }

            processor.ScreenSnapshot = nativeSnapshot;
            for (int row = 0; row < screen.ViewportRows; row++)
            {
                screen.GetViewportRow(row).CopyFrom(
                    nativeSnapshot.GetRow(5 + row),
                    screen.DefaultForeground,
                    screen.DefaultBackground);
            }

            renderer.SelectionStart = (6, -4);
            renderer.SelectionEnd = (24, 8);
            renderer.SelectionIsRectangle = true;

            ArrangeControlToGrid(control, window, renderer, columns: 12, rows: 5);

            TerminalHighlightSpan[] spans = renderer.GetSelectionSpans().ToArray();
            string diagnostics =
                $"SelectionStart={renderer.SelectionStart}, SelectionEnd={renderer.SelectionEnd}\n" +
                DumpViewportRows(screen);
            Assert.NotEmpty(spans);
            Assert.True(
                spans.Any(static span => span.StartColumn == 0),
                diagnostics);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_NativeViewportSelectionResize_UsesFinalViewportAfterPreservingBottom()
    {
        FakeSearchViewportVtProcessor processor = new(
            new TerminalViewportScrollState(TotalRows: 120, OffsetRows: 117, VisibleRows: 3),
            [])
        {
            ResetViewportToTopOnResize = true,
        };
        TerminalControl control = CreateControlWithTransport(
            new FakeTransport(),
            new SingleProcessorFactory(processor),
            VtProcessorPreference.Native);
        Window window = new()
        {
            Width = 160,
            Height = 80,
            Content = control,
        };
        window.Show();

        try
        {
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
            SkiaTerminalRenderer renderer = Assert.IsType<SkiaTerminalRenderer>(control.Renderer);
            ArrangeControlToGrid(control, window, renderer, columns: 12, rows: 3);
            processor.SetViewportState(new TerminalViewportScrollState(
                TotalRows: 120,
                OffsetRows: 117,
                VisibleRows: (ulong)screen.ViewportRows));
            control.ScrollToBottom();
            SetViewportRowText(screen, rowIndex: 1, "SELECTED");

            renderer.SelectionStart = (0, 1);
            renderer.SelectionEnd = (8, 1);

            ArrangeControlToGrid(control, window, renderer, columns: 6, rows: 3);

            Assert.True(processor.ScrolledToBottom);
            Assert.Equal(processor.ViewportScrollState.MaxOffsetRows, processor.ViewportScrollState.OffsetRows);
            (int Column, int Row) selectionStart = renderer.SelectionStart.GetValueOrDefault();
            (int Column, int Row) selectionEnd = renderer.SelectionEnd.GetValueOrDefault();
            Assert.InRange(selectionStart.Row, 0, screen.ViewportRows - 1);
            Assert.InRange(selectionEnd.Row, 0, screen.ViewportRows - 1);
            Assert.Contains("SELECT", ReadViewportTextRange(screen, selectionStart.Row, selectionEnd.Row), StringComparison.Ordinal);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_NativeGhosttySelectionResize_KeepsSelectionOnOriginalLines()
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
            Width = 480,
            Height = 220,
            Content = control,
        };
        window.Show();

        try
        {
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
            SkiaTerminalRenderer renderer = Assert.IsType<SkiaTerminalRenderer>(control.Renderer);
            ArrangeControlToGrid(control, window, renderer, columns: 40, rows: 10);

            StringBuilder builder = new();
            builder.Append("header-");
            builder.Append('A', 30);
            builder.Append("\r\n");
            for (int i = 0; i < 6; i++)
            {
                builder.Append("pre-");
                builder.Append(i);
                builder.Append("\r\n");
            }

            for (int i = 0; i < 4; i++)
            {
                builder.Append("SEL-");
                builder.Append(i);
                builder.Append("\r\n");
            }

            builder.Append("prompt\r\n");
            control.WriteOutput(Encoding.UTF8.GetBytes(builder.ToString()));
            control.ScrollToBottom();

            int selectionStartRow = FindViewportRowContaining(screen, "SEL-0");
            int selectionEndRow = FindViewportRowContaining(screen, "SEL-3");
            Assert.True(selectionStartRow >= 0);
            Assert.True(selectionEndRow > selectionStartRow);
            renderer.SelectionStart = (0, selectionStartRow);
            renderer.SelectionEnd = (5, selectionEndRow);

            ArrangeControlToGrid(control, window, renderer, columns: 20, rows: 10);

            (int Column, int Row) mappedStart = renderer.SelectionStart.GetValueOrDefault();
            (int Column, int Row) mappedEnd = renderer.SelectionEnd.GetValueOrDefault();
            string mappedStartText = ReadRowText(screen.GetViewportRow(mappedStart.Row));
            string mappedEndText = ReadRowText(screen.GetViewportRow(mappedEnd.Row));
            string diagnostics =
                $"InitialStart={selectionStartRow}, InitialEnd={selectionEndRow}, MappedStart={mappedStart}, MappedEnd={mappedEnd}\n" +
                DumpViewportRows(screen);
            Assert.True(
                mappedStartText.Contains("SEL-0", StringComparison.Ordinal),
                diagnostics);
            Assert.True(
                mappedEndText.Contains("SEL-3", StringComparison.Ordinal),
                diagnostics);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_NativeGhosttySelectionResize_WithLsStyleOutputKeepsSelectedFiles()
    {
        await RunNativeGhosttyLsStyleSelectionResizeTest(reverseSelection: false);
    }

    [AvaloniaFact]
    public async Task Control_NativeGhosttySelectionResize_WithReverseLsStyleOutputKeepsSelectedFiles()
    {
        await RunNativeGhosttyLsStyleSelectionResizeTest(reverseSelection: true);
    }

    [AvaloniaFact]
    public async Task Control_NativeGhosttySelectionResize_WithContinuousLsStyleOutputKeepsSelectedFiles()
    {
        await RunNativeGhosttyLsStyleSelectionResizeTest(reverseSelection: false, resizeColumns: [110, 100, 92, 84]);
    }

    [AvaloniaFact]
    public async Task Control_NativeGhosttySelectionResize_ShrinkThenExpandKeepsSelectedFiles()
    {
        await RunNativeGhosttyLsStyleSelectionResizeTest(reverseSelection: false, resizeColumns: [84, 92, 100, 110, 119]);
    }

    [AvaloniaFact]
    public async Task Control_NativeGhosttySelectionResize_ReverseShrinkThenExpandKeepsSelectedFiles()
    {
        await RunNativeGhosttyLsStyleSelectionResizeTest(reverseSelection: true, resizeColumns: [84, 92, 100, 110, 119]);
    }

    [AvaloniaFact]
    public async Task Control_NativeGhosttyRectangularSelectionResize_ShrinkKeepsSelectedColumnsOnWrappedNames()
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
            Width = 1040,
            Height = 600,
            Content = control,
        };
        window.Show();

        try
        {
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
            SkiaTerminalRenderer renderer = Assert.IsType<SkiaTerminalRenderer>(control.Renderer);
            ArrangeControlToGrid(control, window, renderer, columns: 97, rows: 33);

            string[] selectedNames =
            [
                ".zcompdump.MacBook-Pro.local.47585",
                ".zcompdump.MacBook-Pro.local.5576",
                ".zcompdump.MacBook-Pro.local.6133",
                ".zcompdump.MacBook-Pro.local.71593",
                ".zcompdump.MacBook-Pro.local.73447",
                ".zcompdump.MacBook-Pro.local.78198",
                ".zcompdump.MacBook-Pro.local.79864",
            ];
            string output = BuildZcompdumpStyleOutput(selectedNames);
            control.WriteOutput(Encoding.UTF8.GetBytes(output));
            control.ScrollToBottom();

            int firstRow = FindViewportRowContaining(screen, selectedNames[0]);
            int lastRow = FindViewportRowContaining(screen, selectedNames[^1]);
            Assert.True(firstRow >= 0);
            Assert.True(lastRow > firstRow);
            int filenameColumn = ReadRowText(screen.GetViewportRow(firstRow)).IndexOf(selectedNames[0], StringComparison.Ordinal);
            Assert.True(filenameColumn > 0);

            renderer.SelectionStart = (filenameColumn, firstRow);
            renderer.SelectionEnd = (97, lastRow);
            renderer.SelectionIsRectangle = true;

            ArrangeControlToGrid(control, window, renderer, columns: 87, rows: 33);

            TerminalHighlightSpan[] spans = renderer.GetSelectionSpans().ToArray();
            string diagnostics =
                $"FilenameColumn={filenameColumn}, FirstRow={firstRow}, LastRow={lastRow}, SelectionStart={renderer.SelectionStart}, SelectionEnd={renderer.SelectionEnd}\n" +
                DumpViewportRows(screen);
            Assert.NotEmpty(spans);

            int mappedFirstRow = FindViewportRowContaining(screen, selectedNames[0][..^5]);
            Assert.True(mappedFirstRow >= 0, diagnostics);
            Assert.Contains(
                spans,
                span => span.Row == mappedFirstRow &&
                    span.StartColumn == filenameColumn &&
                    span.EndColumn == screen.Columns - 1);
            Assert.Contains(
                spans,
                span => span.Row == mappedFirstRow + 1 &&
                    span.StartColumn == 0 &&
                    span.EndColumn >= 4);

            await control.CopySelectionAsync();
            string? copied = await window.Clipboard!.TryGetTextAsync();
            Assert.NotNull(copied);
            foreach (string selectedName in selectedNames)
            {
                Assert.Contains(selectedName, copied, StringComparison.Ordinal);
            }

            Assert.DoesNotContain("wieslawsoltes", copied, StringComparison.Ordinal);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    private static async Task RunNativeGhosttyLsStyleSelectionResizeTest(bool reverseSelection)
    {
        await RunNativeGhosttyLsStyleSelectionResizeTest(reverseSelection, resizeColumns: [84]);
    }

    private static async Task RunNativeGhosttyLsStyleSelectionResizeTest(
        bool reverseSelection,
        IReadOnlyList<int> resizeColumns)
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
            Width = 1280,
            Height = 600,
            Content = control,
        };
        window.Show();

        try
        {
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
            SkiaTerminalRenderer renderer = Assert.IsType<SkiaTerminalRenderer>(control.Renderer);
            ArrangeControlToGrid(control, window, renderer, columns: 119, rows: 33);

            control.WriteOutput(Encoding.UTF8.GetBytes(BuildLsStyleOutput()));
            control.ScrollToBottom();

            const string firstSelectedFile = "java_error_in_rider_2125.log";
            const string lastSelectedFile = "java_error_in_rider_86673.log";
            int selectionStartRow = FindViewportRowContaining(screen, firstSelectedFile);
            int selectionEndRow = FindViewportRowContaining(screen, lastSelectedFile);
            Assert.True(selectionStartRow >= 0);
            Assert.True(selectionEndRow > selectionStartRow);

            if (reverseSelection)
            {
                renderer.SelectionStart = (119, selectionEndRow);
                renderer.SelectionEnd = (0, selectionStartRow);
            }
            else
            {
                renderer.SelectionStart = (0, selectionStartRow);
                renderer.SelectionEnd = (119, selectionEndRow);
            }

            StringBuilder resizeTrace = new();
            AppendSelectionResizeTrace(resizeTrace, 119, screen, renderer);
            for (int i = 0; i < resizeColumns.Count; i++)
            {
                ArrangeControlToGrid(control, window, renderer, columns: resizeColumns[i], rows: 33);
                AppendSelectionResizeTrace(resizeTrace, resizeColumns[i], screen, renderer);
            }

            (int Column, int Row) mappedStart = renderer.SelectionStart.GetValueOrDefault();
            (int Column, int Row) mappedEnd = renderer.SelectionEnd.GetValueOrDefault();
            int mappedTopRow = Math.Min(mappedStart.Row, mappedEnd.Row);
            int mappedBottomRow = Math.Max(mappedStart.Row, mappedEnd.Row);
            string mappedTopText = ReadRowText(screen.GetViewportRow(mappedTopRow));
            string selectedText = ReadViewportTextRange(screen, mappedTopRow, mappedBottomRow);
            string unwrappedSelectedText = selectedText.Replace("\n", string.Empty, StringComparison.Ordinal);
            string diagnostics =
                $"InitialStart={selectionStartRow}, InitialEnd={selectionEndRow}, MappedStart={mappedStart}, MappedEnd={mappedEnd}\n" +
                resizeTrace +
                DumpViewportRows(screen);
            Assert.True(
                mappedTopText.Contains("java_error_in_rider_2125", StringComparison.Ordinal),
                diagnostics);
            Assert.True(
                unwrappedSelectedText.Contains(firstSelectedFile, StringComparison.Ordinal),
                diagnostics);
            Assert.True(
                unwrappedSelectedText.Contains(lastSelectedFile, StringComparison.Ordinal),
                diagnostics);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_DragSelectionAboveViewport_AutoScrollsUp()
    {
        TerminalControl control = new()
        {
            Width = 640,
            Height = 400,
            VtProcessorPreference = VtProcessorPreference.Managed,
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
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            PopulateScrollableNormalBuffer(control);
            control.ScrollToBottom();

            TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
            SkiaTerminalRenderer renderer = Assert.IsType<SkiaTerminalRenderer>(control.Renderer);
            Assert.Equal(0, screen.ScrollOffset);

            Point start = new(renderer.CellWidth * 2.5, renderer.CellHeight * (screen.ViewportRows - 1.5));
            Point above = new(renderer.CellWidth * 2.5, -renderer.CellHeight);

            window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(above, RawInputModifiers.LeftMouseButton);

            bool scrolled = await WaitUntilAsync(
                () => screen.ScrollOffset > 0,
                TimeSpan.FromSeconds(2));
            Assert.True(scrolled);

            window.MouseUp(above, MouseButton.Left, RawInputModifiers.None);
            HeadlessTerminalTestCleanup.RunDispatcherJobs();

            Assert.NotNull(renderer.SelectionStart);
            Assert.NotNull(renderer.SelectionEnd);
            Assert.True(renderer.SelectionStart!.Value.Row > renderer.SelectionEnd!.Value.Row);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_DragSelectionBelowViewport_AutoScrollsDown()
    {
        TerminalControl control = new()
        {
            Width = 640,
            Height = 400,
            VtProcessorPreference = VtProcessorPreference.Managed,
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
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            PopulateScrollableNormalBuffer(control);
            control.ScrollByRows(-10);

            TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
            SkiaTerminalRenderer renderer = Assert.IsType<SkiaTerminalRenderer>(control.Renderer);
            int initialScrollOffset = screen.ScrollOffset;
            Assert.True(initialScrollOffset > 0);

            Point start = new(renderer.CellWidth * 2.5, renderer.CellHeight * 0.5);
            Point below = new(renderer.CellWidth * 2.5, control.Bounds.Height + renderer.CellHeight);

            window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(below, RawInputModifiers.LeftMouseButton);

            bool scrolled = await WaitUntilAsync(
                () => screen.ScrollOffset < initialScrollOffset,
                TimeSpan.FromSeconds(2));
            Assert.True(scrolled);

            window.MouseUp(below, MouseButton.Left, RawInputModifiers.None);
            HeadlessTerminalTestCleanup.RunDispatcherJobs();

            Assert.NotNull(renderer.SelectionStart);
            Assert.NotNull(renderer.SelectionEnd);
            Assert.True(renderer.SelectionStart!.Value.Row < renderer.SelectionEnd!.Value.Row);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_DragSelectionAboveViewport_WithNativeViewport_KeepsAnchorAbsolute()
    {
        FakeSearchViewportVtProcessor processor = new(
            new TerminalViewportScrollState(TotalRows: 120, OffsetRows: 80, VisibleRows: 24),
            []);
        TerminalControl control = CreateControlWithTransport(
            new FakeTransport(),
            new SingleProcessorFactory(processor),
            VtProcessorPreference.Native);
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
            bool layoutReady = await WaitUntilAsync(
                () => control.Bounds.Width > 1 && control.Bounds.Height > 1,
                TimeSpan.FromSeconds(2));
            Assert.True(layoutReady);

            TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
            SkiaTerminalRenderer renderer = Assert.IsType<SkiaTerminalRenderer>(control.Renderer);
            processor.SetViewportState(new TerminalViewportScrollState(
                TotalRows: (ulong)screen.ViewportRows + 100,
                OffsetRows: 80,
                VisibleRows: (ulong)screen.ViewportRows));
            ulong initialOffset = processor.ViewportScrollState.OffsetRows;
            int startRow = Math.Max(1, screen.ViewportRows - 2);

            Point start = new(renderer.CellWidth * 2.5, renderer.CellHeight * (startRow + 0.5));
            Point above = new(renderer.CellWidth * 2.5, -renderer.CellHeight);

            window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(above, RawInputModifiers.LeftMouseButton);

            bool scrolled = await WaitUntilAsync(
                () => processor.ViewportScrollState.OffsetRows < initialOffset,
                TimeSpan.FromSeconds(2));
            Assert.True(scrolled);

            window.MouseUp(above, MouseButton.Left, RawInputModifiers.None);
            HeadlessTerminalTestCleanup.RunDispatcherJobs();

            int scrolledRows = checked((int)(initialOffset - processor.ViewportScrollState.OffsetRows));
            Assert.Equal(startRow + scrolledRows, renderer.SelectionStart?.Row);
        }
        finally
        {
            await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control);
        }
    }

    [AvaloniaFact]
    public async Task Control_DragSelectionAboveViewport_AtNativeTop_SkipsAutoScrollRequests()
    {
        FakeSearchViewportVtProcessor processor = new(
            new TerminalViewportScrollState(TotalRows: 120, OffsetRows: 0, VisibleRows: 24),
            []);
        TerminalControl control = CreateControlWithTransport(
            new FakeTransport(),
            new SingleProcessorFactory(processor),
            VtProcessorPreference.Native);
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
            bool layoutReady = await WaitUntilAsync(
                () => control.Bounds.Width > 1 && control.Bounds.Height > 1,
                TimeSpan.FromSeconds(2));
            Assert.True(layoutReady);

            TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
            SkiaTerminalRenderer renderer = Assert.IsType<SkiaTerminalRenderer>(control.Renderer);
            processor.SetViewportState(new TerminalViewportScrollState(
                TotalRows: (ulong)screen.ViewportRows + 100,
                OffsetRows: 0,
                VisibleRows: (ulong)screen.ViewportRows));

            Point start = new(renderer.CellWidth * 2.5, renderer.CellHeight * 1.5);
            Point above = new(renderer.CellWidth * 2.5, -renderer.CellHeight);

            window.MouseDown(start, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseMove(above, RawInputModifiers.LeftMouseButton);

            bool requestObserved = await WaitUntilAsync(
                () => processor.ScrollViewportByRowsCallCount > 0,
                TimeSpan.FromMilliseconds(200));
            Assert.False(requestObserved);

            window.MouseUp(above, MouseButton.Left, RawInputModifiers.None);
            HeadlessTerminalTestCleanup.RunDispatcherJobs();
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
            renderer.SelectionEnd = (1, 0);

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
        ITerminalScrollService? scrollService = null,
        string transportId = "fake")
    {
        CompositeTerminalTransportFactory factory = new(
            new ITerminalTransportProvider[]
            {
                new FakeTransportProvider(transport, transportId),
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

    private static void PopulateScrollableNormalBuffer(TerminalControl control)
    {
        for (int i = 0; i < 64; i++)
        {
            control.WriteOutput(Encoding.UTF8.GetBytes($"LINE-{i:000}\n"));
        }

        Assert.NotNull(control.Screen);
        Assert.NotNull(control.ScrollData);
        Assert.True(control.ScrollData!.MaxOffset > 0);
    }

    private static void RaiseEscapeKeyUp(TerminalControl control)
    {
        KeyEventArgs keyUp = new()
        {
            RoutedEvent = InputElement.KeyUpEvent,
            Source = control,
            Key = Key.Escape,
            KeyModifiers = KeyModifiers.None,
        };
        control.RaiseEvent(keyUp);
    }

    private static void SetViewportRowText(TerminalScreen screen, int rowIndex, string text)
    {
        SetRowText(screen.GetViewportRow(rowIndex), text);
    }

    private static void SetRowText(TerminalRow row, string text)
    {
        int columnCount = Math.Min(text.Length, row.Columns);
        for (int column = 0; column < columnCount; column++)
        {
            row[column].Codepoint = text[column];
            row[column].Width = 1;
        }
    }

    private static void ArrangeControlToGrid(
        TerminalControl control,
        Window window,
        SkiaTerminalRenderer renderer,
        int columns,
        int rows)
    {
        double width = (columns * renderer.CellWidth) + (renderer.CellWidth * 0.25);
        double height = (rows * renderer.CellHeight) + (renderer.CellHeight * 0.25);
        control.Width = width;
        control.Height = height;
        window.Width = width;
        window.Height = height;
        control.Measure(new Size(width, height));
        control.Arrange(new Rect(0, 0, width, height));
        HeadlessTerminalTestCleanup.RunDispatcherJobs();
    }

    private static void ArrangeScrollViewerToGrid(
        ScrollViewer scrollViewer,
        Window window,
        SkiaTerminalRenderer renderer,
        int columns,
        int rows)
    {
        double width = (columns * renderer.CellWidth) + (renderer.CellWidth * 0.25);
        double height = (rows * renderer.CellHeight) + (renderer.CellHeight * 0.25);
        scrollViewer.Width = width;
        scrollViewer.Height = height;
        window.Width = width;
        window.Height = height;
        scrollViewer.Measure(new Size(width, height));
        scrollViewer.Arrange(new Rect(0, 0, width, height));
        HeadlessTerminalTestCleanup.RunDispatcherJobs();
    }

    private static string ReadRowPrefix(TerminalRow row, int length)
    {
        StringBuilder builder = new(length);
        int columnCount = Math.Min(length, row.Columns);
        for (int column = 0; column < columnCount; column++)
        {
            int codepoint = row[column].Codepoint;
            builder.Append(codepoint <= 0 ? ' ' : (char)codepoint);
        }

        return builder.ToString();
    }

    private static string ReadRowSlice(TerminalRow row, int startColumn, int endColumnExclusive)
    {
        int columnStart = Math.Clamp(startColumn, 0, row.Columns);
        int columnEnd = Math.Clamp(endColumnExclusive, columnStart, row.Columns);
        StringBuilder builder = new(columnEnd - columnStart);
        for (int column = columnStart; column < columnEnd; column++)
        {
            int codepoint = row[column].Codepoint;
            builder.Append(codepoint <= 0 ? ' ' : (char)codepoint);
        }

        return builder.ToString();
    }

    private static int FindViewportRowContaining(TerminalScreen screen, string text)
    {
        for (int row = 0; row < screen.ViewportRows; row++)
        {
            if (ReadRowText(screen.GetViewportRow(row)).Contains(text, StringComparison.Ordinal))
            {
                return row;
            }
        }

        return -1;
    }

    private static int FindAbsoluteRowContaining(TerminalScreen screen, string text)
    {
        for (int row = 0; row < screen.TotalRows; row++)
        {
            if (ReadRowText(screen.GetRow(row)).Contains(text, StringComparison.Ordinal))
            {
                return row;
            }
        }

        return -1;
    }

    private static string ReadRowText(TerminalRow row)
    {
        StringBuilder builder = new(row.Columns);
        for (int column = 0; column < row.Columns; column++)
        {
            int codepoint = row[column].Codepoint;
            builder.Append(codepoint <= 0 ? ' ' : (char)codepoint);
        }

        return builder.ToString();
    }

    private static int IndexOfEvent(IReadOnlyList<string> events, string value)
    {
        for (int i = 0; i < events.Count; i++)
        {
            if (string.Equals(events[i], value, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static string DumpViewportRows(TerminalScreen screen)
    {
        StringBuilder builder = new();
        for (int row = 0; row < screen.ViewportRows; row++)
        {
            builder.Append(row.ToString(CultureInfo.InvariantCulture));
            builder.Append(": ");
            builder.AppendLine(ReadRowText(screen.GetViewportRow(row)));
        }

        return builder.ToString();
    }

    private static void AppendSelectionResizeTrace(
        StringBuilder builder,
        int columns,
        TerminalScreen screen,
        SkiaTerminalRenderer renderer)
    {
        (int Column, int Row) start = renderer.SelectionStart.GetValueOrDefault();
        (int Column, int Row) end = renderer.SelectionEnd.GetValueOrDefault();
        builder.Append("After ");
        builder.Append(columns.ToString(CultureInfo.InvariantCulture));
        builder.Append(": Start=");
        builder.Append(start);
        builder.Append(", End=");
        builder.Append(end);
        builder.Append(", TopRow=");
        builder.AppendLine(ReadRowText(screen.GetViewportRow(0)));
    }

    private static string ReadViewportTextRange(TerminalScreen screen, int startRow, int endRow)
    {
        int rowStart = Math.Clamp(Math.Min(startRow, endRow), 0, Math.Max(0, screen.ViewportRows - 1));
        int rowEnd = Math.Clamp(Math.Max(startRow, endRow), rowStart, Math.Max(0, screen.ViewportRows - 1));
        StringBuilder builder = new();
        for (int row = rowStart; row <= rowEnd; row++)
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(ReadRowText(screen.GetViewportRow(row)));
        }

        return builder.ToString();
    }

    private static string BuildLsStyleOutput()
    {
        string[] lines =
        [
            "lrwxr-xr-x   1 wieslawsoltes  staff      49 Feb 14  2023 Dropbox -> /Users/wieslawsoltes/Library/CloudStorage/Dropbox",
            "-rw-r--r--   1 wieslawsoltes  staff   10527 Aug 27  2025 DxfToCSharp_crash.log",
            "drwxr-xr-x@ 279 wieslawsoltes  staff    8928 May  4 14:10 GitHub",
            "drwx------@ 117 wieslawsoltes  staff    3744 Apr 20 10:32 Library",
            "drwx------+   8 wieslawsoltes  staff     256 Apr  8 15:03 Movies",
            "drwx------+   7 wieslawsoltes  staff     224 Apr 19  2024 Music",
            "-rw-r--r--@   1 wieslawsoltes  staff  114807 Jun 26  2025 P1030269.JPG",
            "drwx------    2 wieslawsoltes  staff      64 Aug 24  2022 Parallels",
            "drwx------+  11 wieslawsoltes  staff     352 Sep  5  2024 Pictures",
            "drwxr-xr-x@   2 wieslawsoltes  staff      64 Dec 10  2022 Projects",
            "drwxr-xr-x+   4 wieslawsoltes  staff     128 Feb 18  2022 Public",
            "drwxr-xr-x    7 wieslawsoltes  staff     224 Apr 19  2024 RiderProjects",
            "drwxr-xr-x@   3 wieslawsoltes  staff      96 Apr 23  2024 RiderSnapshots",
            "drwxr-xr-x+   3 wieslawsoltes  staff      96 Mar 20  2024 Sites",
            "drwxr-xr-x    3 wieslawsoltes  staff      96 Nov 14  2024 VirtualBox VMs",
            "drwxr-xr-x    6 wieslawsoltes  staff     192 Jan 21  2025 dotTraceSnapshots",
            "drwxr-xr-x@  11 wieslawsoltes  staff     352 Apr 19  2024 eDOSign",
            "-rw-r--r--    1 wieslawsoltes  staff  564097 Dec  6  2023 java_error_in_rider_2125.log",
            "-rw-r--r--    1 wieslawsoltes  staff  557858 Nov 28  2023 java_error_in_rider_44675.log",
            "-rw-r--r--    1 wieslawsoltes  staff  693764 Nov 16  2024 java_error_in_rider_86136.log",
            "-rw-r--r--    1 wieslawsoltes  staff  702616 May  8  2025 java_error_in_rider_86673.log",
            "-rw-r--r--@   1 wieslawsoltes  staff    3827 Dec  6  2023 jbr_err_pid2125.log",
            "-rw-r--r--@   1 wieslawsoltes  staff    6197 Nov 28  2023 jbr_err_pid44675.log",
            "-rw-r--r--@   1 wieslawsoltes  staff    2846 Nov 16  2024 jbr_err_pid86136.log",
            "-rw-r--r--    1 wieslawsoltes  staff       0 Apr 18  2025 jcef_61703.log",
            "-rw-r--r--    1 wieslawsoltes  staff       0 Apr 18  2025 jcef_64516.log",
            "-rw-r--r--    1 wieslawsoltes  staff       0 May 10  2025 jcef_65333.log",
            "-rw-r--r--    1 wieslawsoltes  staff       0 May 11  2025 jcef_82508.log",
            "-rw-r--r--    1 wieslawsoltes  staff       0 May  6  2025 jcef_86673.log",
            "-rw-r--r--@   1 wieslawsoltes  staff    2197 Feb 20  2024 nest-13-27-13.patch",
            "-rw-r--r--@   1 wieslawsoltes  staff 2598103 Jul  7  2025 rider64_DGIJfiYW0n.mp4",
            "drwxr-xr-x@   3 wieslawsoltes  staff      96 Feb 25 14:32 tmp",
            "wieslawsoltes@MacBook-Pro ~ %",
        ];

        StringBuilder builder = new();
        for (int i = 0; i < lines.Length; i++)
        {
            builder.Append(lines[i]);
            builder.Append("\r\n");
        }

        return builder.ToString();
    }

    private static string BuildPowerShellLsStyleOutput(IReadOnlyList<string> names, IReadOnlyList<string> files)
    {
        StringBuilder output = new();
        output.Append("PowerShell 7.5.5\r\n\r\n");
        output.Append("  \x1b[7;38;2;216;222;233;48;2;46;52;64m A new PowerShell stable release is available: v7.6.1 \x1b[0m\r\n");
        output.Append("  \x1b[7;38;2;216;222;233;48;2;46;52;64m Upgrade now, or check out the release page at:       \x1b[0m\r\n");
        output.Append("  \x1b[7;38;2;216;222;233;48;2;46;52;64m   https://aka.ms/PowerShell-Release?tag=v7.6.1       \x1b[0m\r\n\r\n");
        output.Append("PS C:\\Users\\wiesl> ls\r\n\r\n");
        output.Append("    Directory: C:\\Users\\wiesl\r\n\r\n");
        output.Append("\x1b[1;38;2;163;190;140mMode                 LastWriteTime        Length Name\x1b[0m\r\n");
        output.Append("\x1b[1;38;2;163;190;140m----                 -------------        ------ ----\x1b[0m\r\n");

        foreach (string name in names)
        {
            output.Append("d----          12.10.2025    22:36                ");
            output.Append("\x1b[1;38;2;216;222;233;48;2;129;161;193m");
            output.Append(name);
            output.Append("\x1b[0m\r\n");
        }

        output.Append("\x1b[0;38;2;216;222;233;48;2;46;52;64m");
        foreach (string file in files)
        {
            output.Append("-a---          13.10.2025    12:08          24361 ");
            output.Append(file);
            output.Append("\r\n");
        }

        output.Append("PS C:\\Users\\wiesl> ");
        return output.ToString();
    }

    private static string BuildConptyViewportResizeRepaint(bool includeWindowSize)
    {
        StringBuilder output = new();
        output.Append("\x1b[?25l");
        if (includeWindowSize)
        {
            output.Append("\x1b[8;4;133t");
        }

        output.Append("\x1b[H");
        output.Append("-a---          23.10.2023    15:47      596329185 java_error_in_rider64.hprof\x1b[K\r\n");
        output.Append("-a---          21.08.2023    12:08           1967 Nowy dokument 1.2023_08_21_12_08_40.0.svg\x1b[K\r\n");
        output.Append("\x1b[K\r\n");
        output.Append("PS C:\\Users\\wiesl>\x1b[K");
        output.Append("\r\n\x1b[K\r\n\x1b[K\r\n\x1b[K\r\n\x1b[K");
        output.Append("\x1b[4;20H\x1b[?25h");
        return output.ToString();
    }

    private static string BuildConptyStyledViewportResizeRepaint(bool includeWindowSize)
    {
        StringBuilder output = new();
        output.Append("\x1b[?25l");
        if (includeWindowSize)
        {
            output.Append("\x1b[8;31;51t");
        }

        output.Append("\x1b[44m\x1b[1m\x1b[H");
        output.Append("usic\x1b[m\x1b[K\r\n");
        output.Append("dar--          15.04.2025    02:40                \x1b[K");
        output.Append("\x1b[44m\x1b[1mOneDrive\x1b[m\x1b[K\r\n");
        output.Append("-a---          13.10.2025    12:08          24361 .bash_history\x1b[K\r\n");
        output.Append("PS C:\\Users\\wiesl>\x1b[K\x1b[1C\x1b[?25h");
        return output.ToString();
    }

    private static string[] GetKnownPowerShellHomeRowsPresentIn(string text)
    {
        string[] knownRows =
        [
            ".android",
            ".cargo",
            ".codex",
            ".config",
            ".copilot",
            ".dbus-keyrings",
            ".dotnet",
            ".gnupg",
            ".ms-ad",
            ".nuget",
            ".rustup",
            ".skiko",
            ".templateengine",
            ".vscode",
            ".vscode-shared",
            "Contacts",
            "Documents",
            "dotnet",
            "dotTraceSnapshots",
            "Downloads",
            "Dropbox",
            "Favorites",
            "GitHub",
            "iCloudDrive",
            "iCloudPhotos",
            "Links",
            "Music",
            "OneDrive",
            "Pictures",
            "Saved Games",
            "Searches",
            "source",
            "Videos",
            ".bash_history",
            ".gitconfig",
            ".lesshst",
            "dotnet-install.sh",
            "java_error_in_rider_12460.log",
            "java_error_in_rider64_26852.log",
            "java_error_in_rider64.hprof",
        ];

        List<string> present = [];
        foreach (string row in knownRows)
        {
            if (text.Contains(row, StringComparison.Ordinal))
            {
                present.Add(row);
            }
        }

        return present.ToArray();
    }

    private static void AssertPowerShellHomeRowsPreserved(IReadOnlyList<string> expectedRows, string actual)
    {
        foreach (string row in expectedRows)
        {
            Assert.Contains(row, actual, StringComparison.Ordinal);
        }
    }

    private static void AssertPowerShellHomeRowsNotDuplicated(IReadOnlyList<string> expectedRows, string actual)
    {
        foreach (string row in expectedRows)
        {
            int occurrences = CountTrimmedLineEndOccurrences(actual, row);
            Assert.True(
                occurrences <= 1,
                $"Expected at most one listing row ending with '{row}', observed {occurrences}. Snapshot: {actual}");
        }
    }

    private static void AssertPowerShellHomeRowsPreservedAtLeast(
        IReadOnlyList<string> expectedRows,
        string actual,
        int minimumOccurrences)
    {
        foreach (string row in expectedRows)
        {
            int occurrences = CountOccurrences(actual, row);
            Assert.True(
                occurrences >= minimumOccurrences,
                $"Expected at least {minimumOccurrences} occurrences of '{row}', observed {occurrences}. Snapshot: {actual}");
        }
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int startIndex = 0;
        while (startIndex < text.Length)
        {
            int index = text.IndexOf(value, startIndex, StringComparison.Ordinal);
            if (index < 0)
            {
                return count;
            }

            count++;
            startIndex = index + value.Length;
        }

        return count;
    }

    private static int CountTrimmedLineEndOccurrences(string text, string value)
    {
        int count = 0;
        string[] lines = text.Split(Environment.NewLine);
        foreach (string line in lines)
        {
            if (line.TrimEnd().EndsWith(" " + value, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static string ReadAllRows(TerminalScreen screen)
    {
        StringBuilder builder = new();
        for (int rowIndex = 0; rowIndex < screen.TotalRows; rowIndex++)
        {
            builder.AppendLine(ReadRowText(screen.GetRow(rowIndex)));
        }

        return builder.ToString();
    }

    private static string ReadAllRowsLocked(TerminalControl control)
    {
        TerminalScreen screen = Assert.IsType<TerminalScreen>(control.Screen);
        lock (screen.SyncRoot)
        {
            return ReadAllRows(screen);
        }
    }

    private static async Task WaitForStableScreenAsync(TerminalControl control, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        string previous = ReadAllRowsLocked(control);
        DateTime stableSince = DateTime.UtcNow;

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();

            string current = ReadAllRowsLocked(control);
            if (!string.Equals(current, previous, StringComparison.Ordinal))
            {
                previous = current;
                stableSince = DateTime.UtcNow;
                continue;
            }

            if (DateTime.UtcNow - stableSince >= TimeSpan.FromMilliseconds(150))
            {
                return;
            }
        }
    }

    private static (int WidthPx, int HeightPx) CalculateGridPixels(
        SkiaTerminalRenderer renderer,
        int columns,
        int rows)
    {
        return (
            Math.Max(1, (int)Math.Round(columns * renderer.CellWidth)),
            Math.Max(1, (int)Math.Round(rows * renderer.CellHeight)));
    }

    private static TerminalSnapshotExportOptions CreateStyledSnapshotOptions()
    {
        return new TerminalSnapshotExportOptions(
            Unwrap: true,
            TrimTrailingWhitespace: true,
            Extras: new TerminalSnapshotExportExtras(
                IncludeCursor: true,
                IncludeStyle: true,
                IncludeHyperlinks: true,
                IncludeKittyKeyboard: true,
                IncludeCharsets: true,
                IncludePalette: true,
                IncludeModes: true,
                IncludeScrollingRegion: true,
                IncludeTabstops: true,
                IncludeKeyboardModes: true));
    }

    private static bool TryResolvePwshPath(out string? pwshPath)
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string installedPwsh = Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe");
        if (File.Exists(installedPwsh))
        {
            pwshPath = installedPwsh;
            return true;
        }

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            string[] directories = path.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int i = 0; i < directories.Length; i++)
            {
                string candidate = Path.Combine(directories[i], "pwsh.exe");
                if (File.Exists(candidate))
                {
                    pwshPath = candidate;
                    return true;
                }
            }
        }

        pwshPath = null;
        return false;
    }

    private static void CreatePowerShellLsFixture(string root, IReadOnlyList<string> directories, IReadOnlyList<string> files)
    {
        Directory.CreateDirectory(root);
        for (int i = 0; i < directories.Count; i++)
        {
            Directory.CreateDirectory(Path.Combine(root, directories[i]));
        }

        for (int i = 0; i < files.Count; i++)
        {
            File.WriteAllText(Path.Combine(root, files[i]), "fixture");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Test cleanup is best effort because the child process can still be exiting.
        }
    }

    private static string BuildZcompdumpStyleOutput(IReadOnlyList<string> selectedNames)
    {
        string[] before =
        [
            "drwxr-xr-x    6 wieslawsoltes  staff     192 Apr 30 10:48 .swiftpm",
            "drwxr-xr-x    7 wieslawsoltes  staff     224 Nov 16  2022 .templateengine",
            "drwxr-xr-x@   3 wieslawsoltes  staff      96 Feb  4 16:28 .tmp",
            "drwxr-xr-x@   6 wieslawsoltes  staff     192 Jul 28  2025 .trae-aicc",
            "-rw-------@   1 wieslawsoltes  staff   21678 Feb 18 13:11 .viminfo",
            "drwxr-xr-x    5 wieslawsoltes  staff     160 Jan 13 11:16 .vscode",
            "drwxr-xr-x    3 wieslawsoltes  staff      96 Apr 30 20:06 .vscode-shared",
            "drwxr-xr-x    4 wieslawsoltes  staff     128 Jan 28 20:31 .walletwasabi",
            "drwxr-xr-x@   3 wieslawsoltes  staff      96 Aug 17  2025 .xcodegen",
            "-rw-r--r--@   1 wieslawsoltes  staff   49604 May  5 14:08 .zcompdump",
        ];
        int[] sizes = [16395, 17285, 37809, 30146, 37809, 36535, 41456];
        string[] times = ["14:00", "14:37", "14:37", "14:15", "14:16", "14:19", "20:21"];
        string[] after =
        [
            "-rw-r--r--@   1 wieslawsoltes  staff     303 Apr 19 19:38 .zprofile",
            "-rw-------@   1 wieslawsoltes  staff   62519 May  5 14:08 .zsh_history",
            "drwx------   11 wieslawsoltes  staff     352 Apr 30 22:48 .zsh_sessions",
            "-rw-r--r--    1 wieslawsoltes  staff      21 Sep 24  2025 .zshenv",
            "-rw-r--r--    1 wieslawsoltes  staff     208 Dec  9 11:21 .zshrc",
            "drwxrwxrwx    3 wieslawsoltes  staff      96 Dec 18  2023 Adlm",
            "drwx------@  11 wieslawsoltes  staff     352 Apr 30 20:06 Applications",
            "drwxr-xr-x@   6 wieslawsoltes  staff     192 Sep 10  2024 Applications (Parallels)",
            "drwxr-xr-x    3 wieslawsoltes  staff      96 Nov 26 14:42 Autodesk",
        ];

        StringBuilder builder = new();
        for (int i = 0; i < before.Length; i++)
        {
            builder.Append(before[i]);
            builder.Append("\r\n");
        }

        for (int i = 0; i < selectedNames.Count; i++)
        {
            builder.Append("-rw-r--r--@   1 wieslawsoltes  staff  ");
            builder.Append(sizes[i].ToString(CultureInfo.InvariantCulture).PadLeft(6));
            builder.Append(" Feb 24 ");
            builder.Append(times[i]);
            builder.Append(' ');
            builder.Append(selectedNames[i]);
            builder.Append("\r\n");
        }

        for (int i = 0; i < after.Length; i++)
        {
            builder.Append(after[i]);
            builder.Append("\r\n");
        }

        return builder.ToString();
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

    private static byte[] CreateSolidRedSixel(int width, int bands)
    {
        StringBuilder builder = new();
        builder.Append("\u001bPq\"1;1;");
        builder.Append(width.ToString(CultureInfo.InvariantCulture));
        builder.Append(';');
        builder.Append((bands * 6).ToString(CultureInfo.InvariantCulture));
        builder.Append("#1;2;100;0;0#1");
        for (int band = 0; band < bands; band++)
        {
            if (band > 0)
            {
                builder.Append('-');
            }

            builder.Append('!');
            builder.Append(width.ToString(CultureInfo.InvariantCulture));
            builder.Append('~');
        }

        builder.Append("\u001b\\");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static bool ContainsRedPixel(SKPixmap pixels, int maxX, int maxY)
    {
        int width = Math.Min(maxX, pixels.Width);
        int height = Math.Min(maxY, pixels.Height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                SKColor pixel = pixels.GetPixelColor(x, y);
                if (pixel.Red > 200 && pixel.Green < 80 && pixel.Blue < 80)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        return await HeadlessTerminalTestCleanup.WaitUntilAsync(predicate, timeout);
    }

    private static async Task AssertTransportResizeAsync(
        FakeTransport transport,
        Predicate<TerminalSessionDimensions> predicate)
    {
        Assert.True(
            await WaitUntilAsync(
                () => transport.Resizes.Exists(predicate),
                TimeSpan.FromSeconds(2)),
            "Expected transport resize was not observed. Recorded resizes: " +
            string.Join(", ", transport.Resizes.Select(resize => $"{resize.Columns}x{resize.Rows}")));
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

    private static void WriteAscii(TerminalRow row, string text)
    {
        int count = Math.Min(row.Columns, text.Length);
        for (int column = 0; column < count; column++)
        {
            row[column].Codepoint = text[column];
            row[column].Width = 1;
        }

        row.IsDirty = true;
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

    private sealed class FakeInputEndpoint : ITerminalEndpoint, ITerminalInputSink, ITerminalSelectionSource
    {
        public byte[]? LastInput { get; private set; }
        public bool Focused { get; private set; }
        public int WidthPx { get; private set; }
        public int HeightPx { get; private set; }
        public List<TerminalKeyEvent> KeyEvents { get; } = [];
        public List<string> TextInputs { get; } = [];
        public List<TerminalPointerEvent> PointerEvents { get; } = [];
        public bool HasSelection { get; init; }
        public string SelectionText { get; init; } = string.Empty;

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

        public string? ReadSelection() => SelectionText;
    }

    private sealed class TrackingVtProcessorFactory : IVtProcessorFactory
    {
        public int CreateCallCount { get; private set; }
        public List<VtProcessorPreference> Preferences { get; } = [];
        public List<int> ScrollbackLimits { get; } = [];

        public IVtProcessor Create(TerminalScreen screen, VtProcessorPreference preference)
        {
            CreateCallCount++;
            Preferences.Add(preference);
            ScrollbackLimits.Add(screen.ScrollbackLimit);
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

    private sealed class TrackingVtProcessor : IVtProcessor, ITerminalSelectionExportSource
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
        public string? SelectionExportText { get; init; }

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

        public string? ReadSelection(in TerminalSelectionRange selection)
        {
            _ = selection;
            return SelectionExportText;
        }

        public void Dispose()
        {
        }
    }

    private sealed class AlternateScreenOnlyVtProcessor : IVtProcessor
    {
        public int CursorCol => 0;
        public int CursorRow => 0;
        public bool CursorVisible => true;
        public bool ApplicationCursorKeys => false;
        public bool ApplicationKeypad => false;
        public bool AlternateScreen { get; private set; }
        public bool BracketedPaste => false;
        public bool Win32InputMode => false;
        public TerminalModeState ModeState => new(
            CursorVisible,
            ApplicationCursorKeys,
            ApplicationKeypad,
            AlternateScreen,
            BracketedPaste,
            Win32InputMode);

        public event EventHandler<TerminalModeState>? ModeChanged;

        public Action<byte[]>? ResponseCallback { get; set; }
        public Action? BellCallback { get; set; }
        public Action<string>? TitleCallback { get; set; }

        public void Process(ReadOnlySpan<byte> data)
        {
            bool? nextAlternateScreen = data.SequenceEqual("alt-on"u8)
                ? true
                : data.SequenceEqual("alt-off"u8)
                    ? false
                    : null;
            if (nextAlternateScreen is null || AlternateScreen == nextAlternateScreen.Value)
            {
                return;
            }

            AlternateScreen = nextAlternateScreen.Value;
            ModeChanged?.Invoke(this, ModeState);
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
            AlternateScreen = false;
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeSearchViewportVtProcessor(
        TerminalViewportScrollState viewportScrollState,
        IReadOnlyList<TerminalSearchMatch> matches) :
        IVtProcessor,
        ITerminalViewportScrollSource,
        ITerminalSearchSource,
        ITerminalScreenSnapshotSource,
        ITerminalResizeReflowPolicySink
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

        public bool LocalReflowOnResize { get; set; } = true;

        public ulong? LastSetViewportOffsetRows { get; private set; }

        public int ScrollViewportByRowsCallCount { get; private set; }

        public bool ScrolledToBottom { get; set; }

        public bool ScrollToBottomOnProcess { get; set; }

        public bool ResetViewportToTopOnResize { get; set; }

        public TerminalScreen? ScreenSnapshot { get; set; }

        public int ProcessCallCount { get; private set; }

        public byte[]? LastProcessedData { get; private set; }

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
            ProcessCallCount++;
            LastProcessedData = data.ToArray();
            if (ScrollToBottomOnProcess)
            {
                ScrollViewportToBottom();
            }
        }

        public void NotifyResize(int columns, int rows)
        {
            _ = columns;
            UpdateViewportRowsAfterResize(rows);
        }

        public void NotifyResize(int columns, int rows, int widthPx, int heightPx)
        {
            _ = columns;
            _ = widthPx;
            _ = heightPx;
            UpdateViewportRowsAfterResize(rows);
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
            ScrollViewportByRowsCallCount++;

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
            ScrolledToBottom = true;
            ViewportScrollState = ViewportScrollState with { OffsetRows = ViewportScrollState.MaxOffsetRows };
        }

        public void SetViewportOffsetRows(ulong offsetRows)
        {
            ulong clamped = Math.Min(offsetRows, ViewportScrollState.MaxOffsetRows);
            LastSetViewportOffsetRows = clamped;
            ViewportScrollState = ViewportScrollState with { OffsetRows = clamped };
        }

        public void ClearLastSetViewportOffsetRows()
        {
            LastSetViewportOffsetRows = null;
        }

        public void SetViewportState(TerminalViewportScrollState viewportScrollState)
        {
            ViewportScrollState = viewportScrollState;
        }

        private void UpdateViewportRowsAfterResize(int rows)
        {
            ulong visibleRows = (ulong)Math.Max(1, rows);
            ulong maxOffsetRows = ViewportScrollState.TotalRows > visibleRows
                ? ViewportScrollState.TotalRows - visibleRows
                : 0;
            ulong offsetRows = ResetViewportToTopOnResize
                ? 0
                : Math.Min(ViewportScrollState.OffsetRows, maxOffsetRows);
            ViewportScrollState = ViewportScrollState with
            {
                OffsetRows = offsetRows,
                VisibleRows = visibleRows,
            };
        }

        public void PopulateSearchMatches(string needle, List<TerminalSearchMatch> destination)
        {
            _ = needle;
            destination.Clear();
            destination.AddRange(_matches);
        }

        public bool TryCreateScreenSnapshot(
            int firstAbsoluteRow,
            int rowCount,
            int scrollbackLimit,
            out TerminalScreen snapshot)
        {
            snapshot = null!;
            if (ScreenSnapshot is null || rowCount <= 0)
            {
                return false;
            }

            int firstRow = Math.Clamp(firstAbsoluteRow, 0, Math.Max(0, ScreenSnapshot.TotalRows - 1));
            int snapshotRows = Math.Min(rowCount, ScreenSnapshot.TotalRows - firstRow);
            if (snapshotRows <= 0)
            {
                return false;
            }

            TerminalScreen result = new(ScreenSnapshot.Columns, snapshotRows, scrollbackLimit)
            {
                DefaultForeground = ScreenSnapshot.DefaultForeground,
                DefaultBackground = ScreenSnapshot.DefaultBackground,
            };

            for (int row = 0; row < snapshotRows; row++)
            {
                result.GetRow(row).CopyFrom(
                    ScreenSnapshot.GetRow(firstRow + row),
                    result.DefaultForeground,
                    result.DefaultBackground);
            }

            snapshot = result;
            return true;
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

    private sealed class ResizeOrderingVtProcessorFactory : IVtProcessorFactory
    {
        public ResizeOrderingVtProcessor? LastProcessor { get; private set; }

        public IVtProcessor Create(TerminalScreen screen, VtProcessorPreference preference)
        {
            _ = screen;
            _ = preference;
            ResizeOrderingVtProcessor processor = new();
            LastProcessor = processor;
            return processor;
        }
    }

    private sealed class ResizeOrderingVtProcessor : IVtProcessor
    {
        private readonly object _eventsSync = new();
        private int _processedChunkCount;

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

        public ManualResetEventSlim FirstProcessStarted { get; } = new();

        public int ProcessedChunkCount => Volatile.Read(ref _processedChunkCount);

        public IReadOnlyList<string> EventsSnapshot
        {
            get
            {
                lock (_eventsSync)
                {
                    return _events.ToArray();
                }
            }
        }

        private List<string> _events { get; } = [];

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
            int chunk = Interlocked.Increment(ref _processedChunkCount);
            AddEvent("process:" + chunk.ToString(CultureInfo.InvariantCulture));
            if (chunk == 1)
            {
                FirstProcessStarted.Set();
            }

            Thread.Sleep(75);
        }

        public void NotifyResize(int columns, int rows)
        {
            _ = rows;
            AddEvent("resize:" + columns.ToString(CultureInfo.InvariantCulture));
        }

        public void NotifyResize(int columns, int rows, int widthPx, int heightPx)
        {
            _ = rows;
            _ = widthPx;
            _ = heightPx;
            AddEvent("resize:" + columns.ToString(CultureInfo.InvariantCulture));
        }

        public void Reset()
        {
        }

        public void Dispose()
        {
            FirstProcessStarted.Dispose();
        }

        private void AddEvent(string value)
        {
            lock (_eventsSync)
            {
                _events.Add(value);
            }
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
        private readonly string _transportId;

        public FakeTransportProvider(ITerminalTransport transport, string transportId = "fake")
        {
            _transport = transport;
            _transportId = transportId;
        }

        public string TransportId => _transportId;

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
        public bool EchoInput { get; init; } = true;
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
            if (EchoInput)
            {
                _dataReceived?.Invoke(data, data.Length);
            }
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
