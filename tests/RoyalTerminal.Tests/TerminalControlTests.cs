// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests — Avalonia headless tests for TerminalControl.

using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Media;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;
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
}
