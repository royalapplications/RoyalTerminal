// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Terminal;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalCompositionTests
{
    [AvaloniaFact]
    public async Task FocusedControlClearsOverlayOnCommitCancelAndFocusLoss()
    {
        TerminalControl control = new() { VtProcessorPreference = VtProcessorPreference.Managed };
        Button other = new();
        Window window = new() { Width = 640, Height = 400, Content = new StackPanel { Children = { control, other } } };
        window.Show();
        try
        {
            await HeadlessTerminalTestCleanup.DrainDispatcherAsync();
            control.Focus();
            control.WriteOutput("hello"u8);
            TextInputMethodClientRequestedEventArgs args = new() { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
            control.RaiseEvent(args);
            args.Client!.SetPreeditText("日本", 1);
            Assert.NotNull(control.Renderer!.Preedit);
            Rect candidate = args.Client.CursorRectangle;
            Assert.True(candidate.Width > 0 && candidate.Height > 0);
            args.Client.SetPreeditText(string.Empty, 0); // IBus HidePreedit.
            Assert.Null(control.Renderer.Preedit);
            args.Client.SetPreeditText("a\u0301", 2);
            window.KeyTextInput("a\u0301");
            Assert.Null(control.Renderer.Preedit);
            args.Client.SetPreeditText("cancel on blur", 3);
            other.Focus();
            Assert.Null(control.Renderer.Preedit);
        }
        finally { await HeadlessTerminalTestCleanup.CleanupWindowAsync(window, control); }
    }
    [Theory]
    [InlineData("abc", 3, 0, 7, 0, 2, 0, 3, 3)]
    [InlineData("abc", 3, 7, 7, 5, 7, 0, 3, 7)]
    [InlineData("abcdefgh", 8, 3, 3, 0, 3, 4, 8, 3)]
    [InlineData("abcdefgh", 0, 3, 3, 0, 3, 0, 4, 0)]
    [InlineData("a\u0301界", 2, 0, 7, 0, 2, 0, 2, 1)]
    [InlineData("😀a", 2, 0, 7, 0, 2, 0, 2, 2)]
    [InlineData("界", 1, 0, 0, 0, -1, 1, 1, 0)]
    public void PreeditClipsWholeClustersAndUsesUtf16Caret(string text, int cursor, int column, int maximum,
        int start, int end, int offset, int limit, int caret)
    {
        TerminalPreedit preedit = new(text, cursor);
        Assert.Equal(new TerminalPreeditRange(start, end, offset, limit, caret), preedit.Range(column, maximum));
    }

    [Fact]
    public void PreeditOverridesHiddenPasswordAndBlinkButNotViewport()
    {
        Assert.Equal(CursorStyle.Block, TerminalCursorAppearance.Resolve(CursorStyle.Bar, true, true, false, false, true, false, true));
        Assert.Null(TerminalCursorAppearance.Resolve(CursorStyle.Bar, false, true, true, true, false, true, true));
    }

    [AvaloniaFact]
    public void ClientDoesNotExposeTerminalOutputAndPreservesCaretAndReset()
    {
        Rect cursor = new(10, 20, 8, 16);
        string? text = null;
        int? position = null;
        TerminalTextInputMethodClient client = new(new Border(), () => cursor, (value, offset) => (text, position) = (value, offset));
        Assert.True(client.SupportsPreedit);
        Assert.False(client.SupportsSurroundingText);
        Assert.Empty(client.SurroundingText);
        client.SetPreeditText("日本", 1);
        Assert.Equal(("日本", 1), (text, position));
        int notifications = 0, resets = 0;
        client.CursorRectangleChanged += (_, _) => notifications++;
        client.ResetRequested += (_, _) => resets++;
        client.NotifyCursorChanged(); client.NotifyCursorChanged();
        Assert.Equal(1, notifications);
        cursor = cursor.Translate(new Vector(1, 0));
        client.NotifyCursorChanged();
        Assert.Equal(2, notifications);
        client.Reset();
        Assert.Null(text);
        Assert.Equal(1, resets);
    }

    [AvaloniaFact]
    public void ControlAdvertisesAnImeClient()
    {
        TerminalControl control = new();
        TextInputMethodClientRequestedEventArgs args = new() { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
        control.RaiseEvent(args);
        Assert.NotNull(args.Client);
        Assert.Same(control, args.Client.TextViewVisual);
        Assert.True(args.Client.CursorRectangle.Width > 0);
        args.Client.SetPreeditText("ignored while unfocused", 2);
        args.Client.SetPreeditText(null);
    }

    [Fact]
    public void OverlayRendersWithoutChangingTerminalOrSnapshot()
    {
        TerminalScreen screen = new(5, 2);
        using BasicVtProcessor processor = new(screen);
        processor.Process("hello"u8);
        byte[] before = processor.GetBinarySnapshot();
        using SkiaTerminalRenderer renderer = new() { Preedit = new("a\u0301日本", 2), CursorColumn = 4, CursorRow = 0 };
        using SKSurface surface = SKSurface.Create(new SKImageInfo((int)Math.Ceiling(renderer.CellWidth * 5), (int)Math.Ceiling(renderer.CellHeight * 2)));
        renderer.RenderFull(surface.Canvas, screen);
        Assert.Equal(before, processor.GetBinarySnapshot());
        renderer.Preedit = null;
        renderer.RenderFull(surface.Canvas, screen);
        Assert.Equal(before, processor.GetBinarySnapshot());
    }

    [Fact]
    public void LayoutMetadataDistinguishesTextModifiersFromShortcuts()
    {
        KeyEventArgs azerty = new() { PhysicalKey = PhysicalKey.Q, Key = Key.A, KeySymbol = "a" };
        Assert.Equal("Q", TerminalKeyEncodingIdentity.Get(azerty, hasLayoutCodepoint: true));
        Assert.Equal("A", TerminalKeyEncodingIdentity.Get(azerty));
        static string Translate(KeyEventArgs _, KeyModifiers modifiers) => (modifiers & (KeyModifiers.Control | KeyModifiers.Alt)) ==
            (KeyModifiers.Control | KeyModifiers.Alt) ? "€" : (modifiers & KeyModifiers.Shift) != 0 ? "É" : "é";
        Assert.Equal(new TerminalKeyboardLayoutInfo('é', TerminalModifiers.Shift), TerminalKeyboardLayout.Resolve(
            new() { KeySymbol = "É", KeyModifiers = KeyModifiers.Shift }, Translate));
        Assert.Equal(new TerminalKeyboardLayoutInfo('é', TerminalModifiers.Control | TerminalModifiers.Alt), TerminalKeyboardLayout.Resolve(
            new() { KeySymbol = "€", KeyModifiers = KeyModifiers.Control | KeyModifiers.Alt }, Translate));
        Assert.Equal(new TerminalKeyboardLayoutInfo('é', TerminalModifiers.None), TerminalKeyboardLayout.Resolve(
            new() { KeySymbol = "é", KeyModifiers = KeyModifiers.Control }, Translate));
        Assert.Equal(0U, TerminalKeyboardLayout.Scalar("ab"));
        Assert.Equal(0x1F600U, TerminalKeyboardLayout.Scalar("😀"));
        Assert.Equal(0U, TerminalKeyboardLayout.Scalar("\u001b"));
    }

    [AvaloniaFact]
    public void PlatformLayoutProbeDoesNotThrowOrChangeComposition()
    {
        TerminalKeyboardLayout layout = new();
        TerminalKeyboardLayoutInfo result = layout.GetInfo(new() { Key = Key.A, PhysicalKey = PhysicalKey.A, KeySymbol = "a" });
        Assert.InRange(result.UnshiftedCodepoint, 0U, 0x10FFFFU);
        Assert.Equal(ushort.MaxValue, MacOsKeyboardLayout.ScanCode(PhysicalKey.None));
        Assert.Equal((ushort)0, MacOsKeyboardLayout.ScanCode(PhysicalKey.A));
        Assert.False(new MacOsTextInputKeySource().TryGetKey("a", out _)); // No NSApp is created in headless hosts.
        KeyEventArgs key = Assert.IsType<KeyEventArgs>(MacOsTextInputKeySource.CreateKey(0, 1U << 17, "A"));
        Assert.Equal(PhysicalKey.A, key.PhysicalKey);
        Assert.Equal(KeyModifiers.Shift, key.KeyModifiers);
        Assert.Equal("A", key.KeySymbol);
        Assert.Null(MacOsTextInputKeySource.CreateKey(ushort.MaxValue, 0, "a"));
    }
}
