// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.Controls - Shared Ghostty input dispatch helpers.

using System.Text;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using RoyalTerminal.Avalonia.Input;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.Avalonia.Controls;

internal readonly record struct GhosttyKeyDispatchResult(
    uint MacKeycode,
    string? KeySymbol,
    bool HasText,
    bool Accepted);

[SupportedOSPlatform("macos")]
internal static class GhosttyInputPipeline
{
    public static unsafe bool HandleKeyDown(
        GhosttySurface? surface,
        KeyEventArgs e,
        Action<GhosttyKeyDispatchResult>? onDispatched = null)
    {
        if (surface is null)
        {
            return false;
        }

        uint macKeycode = MacKeyMapping.ConvertKeyToMacKeycode(e.Key);
        if (macKeycode == MacKeyMapping.Unmapped)
        {
            return false;
        }

        string? keySymbol = e.KeySymbol;
        byte[]? textBytes = null;
        if (!string.IsNullOrEmpty(keySymbol)
            && Rune.TryGetRuneAt(keySymbol, 0, out Rune firstRune)
            && firstRune.Value >= 0x20)
        {
            textBytes = Encoding.UTF8.GetBytes(keySymbol + '\0');
        }

        bool accepted;
        fixed (byte* textPtr = textBytes)
        {
            GhosttyInputKey inputKey = new()
            {
                Action = GhosttyInputAction.Press,
                Keycode = macKeycode,
                Mods = MacKeyMapping.ConvertModifiers(e.KeyModifiers),
                Text = textPtr,
                Composing = false,
            };
            accepted = surface.SendKey(inputKey);
        }

        onDispatched?.Invoke(
            new GhosttyKeyDispatchResult(
                macKeycode,
                keySymbol,
                textBytes is not null,
                accepted));

        return accepted;
    }

    public static bool HandleKeyUp(GhosttySurface? surface, KeyEventArgs e)
    {
        if (surface is null)
        {
            return false;
        }

        uint macKeycode = MacKeyMapping.ConvertKeyToMacKeycode(e.Key);
        if (macKeycode == MacKeyMapping.Unmapped)
        {
            return false;
        }

        GhosttyInputKey inputKey = new()
        {
            Action = GhosttyInputAction.Release,
            Keycode = macKeycode,
            Mods = MacKeyMapping.ConvertModifiers(e.KeyModifiers),
            Composing = false,
        };

        return surface.SendKey(inputKey);
    }

    public static unsafe bool HandleTextInput(GhosttySurface? surface, TextInputEventArgs e)
    {
        if (surface is null || string.IsNullOrEmpty(e.Text))
        {
            return false;
        }

        byte[] textBytes = Encoding.UTF8.GetBytes(e.Text + '\0');
        bool accepted;
        fixed (byte* textPtr = textBytes)
        {
            GhosttyInputKey inputKey = new()
            {
                Action = GhosttyInputAction.Press,
                Keycode = 0,
                Mods = GhosttyMods.None,
                Text = textPtr,
                Composing = false,
            };

            accepted = surface.SendKey(inputKey);
        }

        return accepted;
    }

    public static bool HandlePointerPressed(Control owner, GhosttySurface? surface, PointerPressedEventArgs e)
    {
        owner.Focus();
        if (surface is null)
        {
            return false;
        }

        Point point = e.GetPosition(owner);
        PointerPointProperties props = e.GetCurrentPoint(owner).Properties;
        GhosttyMouseButton? button = props.IsLeftButtonPressed ? GhosttyMouseButton.Left
            : props.IsRightButtonPressed ? GhosttyMouseButton.Right
            : props.IsMiddleButtonPressed ? GhosttyMouseButton.Middle
            : null;
        if (button is null)
        {
            return false;
        }

        GhosttyMods mods = MacKeyMapping.ConvertModifiers(e.KeyModifiers);
        bool accepted = surface.SendMouseButton(GhosttyMouseState.Press, button.Value, mods);
        surface.SendMousePos(point.X, point.Y, mods);
        return accepted;
    }

    public static void HandlePointerMoved(Control owner, GhosttySurface? surface, PointerEventArgs e)
    {
        if (surface is null)
        {
            return;
        }

        Point point = e.GetPosition(owner);
        surface.SendMousePos(point.X, point.Y, MacKeyMapping.ConvertModifiers(e.KeyModifiers));
    }

    public static bool HandlePointerReleased(GhosttySurface? surface, PointerReleasedEventArgs e)
    {
        if (surface is null)
        {
            return false;
        }

        GhosttyMouseButton? button = e.InitialPressMouseButton switch
        {
            MouseButton.Left => GhosttyMouseButton.Left,
            MouseButton.Right => GhosttyMouseButton.Right,
            MouseButton.Middle => GhosttyMouseButton.Middle,
            _ => null,
        };

        if (button is null)
        {
            return false;
        }

        return surface.SendMouseButton(
            GhosttyMouseState.Release,
            button.Value,
            MacKeyMapping.ConvertModifiers(e.KeyModifiers));
    }

    public static bool HandlePointerWheelChanged(GhosttySurface? surface, PointerWheelEventArgs e)
    {
        if (surface is null)
        {
            return false;
        }

        surface.SendMouseScroll(e.Delta.X, e.Delta.Y, (int)MacKeyMapping.ConvertModifiers(e.KeyModifiers));
        return true;
    }
}
