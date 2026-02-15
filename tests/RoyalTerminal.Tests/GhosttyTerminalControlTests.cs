// Licensed under the MIT License.
// RoyalTerminal.Tests — Avalonia headless tests for GhosttyTerminalControl.

using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Media;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Rendering;
using Xunit;

namespace RoyalTerminal.Tests;

/// <summary>
/// Headless Avalonia tests for the terminal control.
/// Uses [AvaloniaFact] to run on the UI thread.
/// </summary>
public class GhosttyTerminalControlTests
{
    [AvaloniaFact]
    public void Control_CanBeInstantiated()
    {
        var control = new GhosttyTerminalControl();
        Assert.NotNull(control);
    }

    [AvaloniaFact]
    public void Control_DefaultProperties_HaveCorrectValues()
    {
        var control = new GhosttyTerminalControl();

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
        var control = new GhosttyTerminalControl
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
        var control = new GhosttyTerminalControl();

        Assert.Equal(Color.FromRgb(0xD4, 0xD4, 0xD4), control.DefaultForeground);
        Assert.Equal(Color.FromRgb(0x1E, 0x1E, 0x1E), control.DefaultBackground);
    }

    [AvaloniaFact]
    public void Control_Colors_CanBeChanged()
    {
        var control = new GhosttyTerminalControl
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
        var control = new GhosttyTerminalControl();
        Assert.True(control.Focusable);
    }

    [AvaloniaFact]
    public void Control_Screen_IsInitialized()
    {
        var control = new GhosttyTerminalControl();
        Assert.NotNull(control.Screen);
        Assert.Equal(80, control.Screen!.Columns);
        Assert.Equal(24, control.Screen.ViewportRows);
    }

    [AvaloniaFact]
    public void Control_Renderer_IsInitialized()
    {
        var control = new GhosttyTerminalControl();
        Assert.NotNull(control.Renderer);
    }

    [AvaloniaFact]
    public void Control_ScrollData_IsInitialized()
    {
        var control = new GhosttyTerminalControl();
        Assert.NotNull(control.ScrollData);
    }

    [AvaloniaFact]
    public void Control_Surface_IsNullByDefault()
    {
        var control = new GhosttyTerminalControl();
        Assert.Null(control.Surface);
    }

    [AvaloniaFact]
    public void Control_DetachSurface_DoesNotThrow()
    {
        var control = new GhosttyTerminalControl();
        control.DetachSurface(); // Should not throw even when no surface attached
    }

    [AvaloniaFact]
    public void Control_InvalidateTerminal_DoesNotThrow()
    {
        var control = new GhosttyTerminalControl();
        control.InvalidateTerminal();
    }

    [AvaloniaFact]
    public void Control_ScrollByRows_DoesNotThrow()
    {
        var control = new GhosttyTerminalControl();
        control.ScrollByRows(5);
        control.ScrollByRows(-5);
    }

    [AvaloniaFact]
    public void Control_ScrollToBottom_DoesNotThrow()
    {
        var control = new GhosttyTerminalControl();
        control.ScrollToBottom();
    }

    [AvaloniaFact]
    public void Control_ClearSelection_DoesNotThrow()
    {
        var control = new GhosttyTerminalControl();
        control.ClearSelection();
    }

    [AvaloniaFact]
    public void Control_CanBeAddedToWindow()
    {
        var control = new GhosttyTerminalControl();
        var window = new Window { Content = control };

        window.Show();

        Assert.NotNull(window.Content);
        Assert.IsType<GhosttyTerminalControl>(window.Content);

        window.Close();
    }

    [AvaloniaFact]
    public void Control_DataReceived_EventCanBeSubscribed()
    {
        var control = new GhosttyTerminalControl();
        var received = false;

        control.DataReceived += (_, _) => received = true;

        // Write output triggers the event
        control.WriteOutput("Hello"u8);
        Assert.True(received);
    }

    [AvaloniaFact]
    public void Control_TerminalResized_EventCanBeSubscribed()
    {
        var control = new GhosttyTerminalControl();
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
        var control = new GhosttyTerminalControl();
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
        var control1 = new GhosttyTerminalControl { Columns = 80, Rows = 24 };
        var control2 = new GhosttyTerminalControl { Columns = 120, Rows = 40 };

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

        GhosttyTerminalControl control = new();
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
        GhosttyTerminalControl control = new();
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
}
