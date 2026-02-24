// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — Avalonia headless tests for TerminalControl.

using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Media;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Rendering;
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
        Assert.Equal(14.0, control.TerminalFontSize);
        Assert.Equal(80, control.Columns);
        Assert.Equal(24, control.Rows);
        Assert.Equal(10_000, control.ScrollbackLimit);
        Assert.True(control.AutoScroll);
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
            TerminalFontSize = 16.0,
            Columns = 120,
            Rows = 40,
            ScrollbackLimit = 50_000,
            AutoScroll = false,
        };

        Assert.Equal("JetBrains Mono", control.FontFamilyName);
        Assert.Equal(16.0, control.TerminalFontSize);
        Assert.Equal(120, control.Columns);
        Assert.Equal(40, control.Rows);
        Assert.Equal(50_000, control.ScrollbackLimit);
        Assert.False(control.AutoScroll);
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
            window.Close();
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
            window.Close();
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
            window.Close();
            control.StopPty();
        }
    }

    [AvaloniaFact]
    public async Task Control_PasteAsync_DefaultPolicy_PreservesControlCharacters()
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
                Encoding.UTF8.GetString(payload) == "safe\x1b[201~\u0001text");
        }
        finally
        {
            window.Close();
            control.StopPty();
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
            window.Close();
            control.StopPty();
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
            window.Close();
            control.StopPty();
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
            window.Close();
            control.StopPty();
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
            window.Close();
            control.StopPty();
        }
    }

    private static TerminalControl CreateControlWithTransport(
        FakeTransport transport,
        IVtProcessorFactory vtProcessorFactory,
        VtProcessorPreference preference)
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
            new DefaultTerminalScrollService(),
            vtProcessorFactory,
            new DefaultPtyFactory(),
            new NullSshCredentialProvider(),
            new RejectAllSshHostKeyValidator(),
            factory)
        {
            VtProcessorPreference = preference,
        };
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

    private sealed class TrackingVtProcessor : IVtProcessor
    {
        public int CursorCol => 0;
        public int CursorRow => 0;
        public bool CursorVisible => true;
        public bool ApplicationCursorKeys => false;
        public bool ApplicationKeypad => false;
        public bool AlternateScreen => false;
        public bool BracketedPaste => false;
        public TerminalModeState ModeState => new(
            CursorVisible,
            ApplicationCursorKeys,
            ApplicationKeypad,
            AlternateScreen,
            BracketedPaste);

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
        public TerminalModeState ModeState => new(
            CursorVisible,
            ApplicationCursorKeys,
            ApplicationKeypad,
            AlternateScreen,
            BracketedPaste);

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
            _ = dimensions;
        }

        public ValueTask StopAsync()
        {
            StopCalled = true;
            IsRunning = false;
            _processExited?.Invoke(0);
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            IsRunning = false;
        }
    }
}
