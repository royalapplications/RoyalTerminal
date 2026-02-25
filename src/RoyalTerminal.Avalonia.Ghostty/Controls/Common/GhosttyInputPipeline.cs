// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.Controls - Shared Ghostty input dispatch helpers.

using System.Text;
using System.Runtime.CompilerServices;
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
    private sealed class PointerButtonState
    {
        public bool Left;
        public bool Middle;
        public bool Right;
    }

    private static readonly ConditionalWeakTable<Control, PointerButtonState> s_pointerButtons = new();

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

        PointerButtonState trackedState = s_pointerButtons.GetOrCreateValue(owner);
        Point point = e.GetPosition(owner);
        PointerPointProperties props = e.GetCurrentPoint(owner).Properties;
        GhosttyMouseButton? button = ResolvePressedMouseButton(props);
        if (button is null && IsPrimaryPointer(e.Pointer.Type))
        {
            // Some touchpad paths report a press without explicit button metadata.
            button = GhosttyMouseButton.Left;
        }

        if (button is null)
        {
            return false;
        }

        SetPointerButtonState(trackedState, button.Value, isDown: true);
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

        PointerButtonState trackedState = s_pointerButtons.GetOrCreateValue(owner);
        PointerPointProperties props = e.GetCurrentPoint(owner).Properties;
        SyncPointerButtonState(trackedState, props, preserveWhenNoButtons: true);

        Point point = e.GetPosition(owner);
        surface.SendMousePos(point.X, point.Y, MacKeyMapping.ConvertModifiers(e.KeyModifiers));
    }

    public static bool HandlePointerReleased(Control owner, GhosttySurface? surface, PointerReleasedEventArgs e)
    {
        if (surface is null)
        {
            return false;
        }

        PointerButtonState trackedState = s_pointerButtons.GetOrCreateValue(owner);
        PointerPointProperties props = e.GetCurrentPoint(owner).Properties;
        GhosttyMouseButton? button = ConvertMouseButton(e.InitialPressMouseButton);
        if (button is null)
        {
            button = ResolveReleasedMouseButton(props);
        }
        if (button is null)
        {
            button = GetTrackedPressedButton(trackedState);
        }
        if (button is null && IsPrimaryPointer(e.Pointer.Type))
        {
            button = GhosttyMouseButton.Left;
        }

        if (button is null)
        {
            SyncPointerButtonState(trackedState, props);
            return false;
        }

        bool accepted = surface.SendMouseButton(
            GhosttyMouseState.Release,
            button.Value,
            MacKeyMapping.ConvertModifiers(e.KeyModifiers));

        SetPointerButtonState(trackedState, button.Value, isDown: false);
        return accepted;
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

    private static void SyncPointerButtonState(
        PointerButtonState trackedState,
        PointerPointProperties properties,
        bool preserveWhenNoButtons = false)
    {
        bool anyButtonPressed =
            properties.IsLeftButtonPressed ||
            properties.IsMiddleButtonPressed ||
            properties.IsRightButtonPressed;
        if (preserveWhenNoButtons && !anyButtonPressed)
        {
            return;
        }

        trackedState.Left = properties.IsLeftButtonPressed;
        trackedState.Middle = properties.IsMiddleButtonPressed;
        trackedState.Right = properties.IsRightButtonPressed;
    }

    private static void SetPointerButtonState(PointerButtonState trackedState, GhosttyMouseButton button, bool isDown)
    {
        switch (button)
        {
            case GhosttyMouseButton.Left:
                trackedState.Left = isDown;
                break;
            case GhosttyMouseButton.Middle:
                trackedState.Middle = isDown;
                break;
            case GhosttyMouseButton.Right:
                trackedState.Right = isDown;
                break;
        }
    }

    private static GhosttyMouseButton? GetTrackedPressedButton(PointerButtonState trackedState)
    {
        if (trackedState.Left)
        {
            return GhosttyMouseButton.Left;
        }

        if (trackedState.Middle)
        {
            return GhosttyMouseButton.Middle;
        }

        if (trackedState.Right)
        {
            return GhosttyMouseButton.Right;
        }

        return null;
    }

    private static bool IsPrimaryPointer(PointerType pointerType)
    {
        return pointerType is PointerType.Mouse or PointerType.Touch;
    }

    private static GhosttyMouseButton? ResolvePressedMouseButton(PointerPointProperties properties)
    {
        GhosttyMouseButton? fromUpdateKind = properties.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonPressed => GhosttyMouseButton.Left,
            PointerUpdateKind.MiddleButtonPressed => GhosttyMouseButton.Middle,
            PointerUpdateKind.RightButtonPressed => GhosttyMouseButton.Right,
            _ => null,
        };
        if (fromUpdateKind is not null)
        {
            return fromUpdateKind;
        }

        return ConvertPressedMouseButton(properties);
    }

    private static GhosttyMouseButton? ResolveReleasedMouseButton(PointerPointProperties properties)
    {
        return properties.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonReleased => GhosttyMouseButton.Left,
            PointerUpdateKind.MiddleButtonReleased => GhosttyMouseButton.Middle,
            PointerUpdateKind.RightButtonReleased => GhosttyMouseButton.Right,
            _ => null,
        };
    }

    private static GhosttyMouseButton? ConvertPressedMouseButton(PointerPointProperties properties)
    {
        if (properties.IsLeftButtonPressed)
        {
            return GhosttyMouseButton.Left;
        }

        if (properties.IsMiddleButtonPressed)
        {
            return GhosttyMouseButton.Middle;
        }

        if (properties.IsRightButtonPressed)
        {
            return GhosttyMouseButton.Right;
        }

        return null;
    }

    private static GhosttyMouseButton? ConvertMouseButton(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => GhosttyMouseButton.Left,
            MouseButton.Middle => GhosttyMouseButton.Middle,
            MouseButton.Right => GhosttyMouseButton.Right,
            _ => null,
        };
    }
}
