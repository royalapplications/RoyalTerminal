// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.App - Terminal input adapter for handled Windows modifier input.

using Avalonia.Input;
using Avalonia.Input.TextInput;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;

namespace RoyalTerminal.Avalonia.App.Services;

internal sealed class HandledInputSuppressingTerminalInputAdapter : ITerminalInputAdapter, IResettableTerminalInputAdapter
{
    private const int KittyReportEvents = 0x02;
    private const int KittyReportAllKeysAsEscapeCodes = 0x08;

    private const uint EnhancedKey = 0x0100;
    private const uint LeftAltPressed = 0x0002;
    private const uint RightAltPressed = 0x0001;
    private const uint LeftCtrlPressed = 0x0008;
    private const uint RightCtrlPressed = 0x0004;
    private const uint ShiftPressed = 0x0010;

    private readonly ITerminalInputAdapter _inner;
    private KeyModifiers _kittyReportedModifierKeyDown;
    private KeyModifiers _terminalModifierKeyState;

    public HandledInputSuppressingTerminalInputAdapter(ITerminalInputAdapter inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public bool HandleKeyDown(KeyEventArgs e, ITerminalSessionService sessionService, IVtProcessor? vtProcessor)
    {
        if (e.Handled && !CanForwardHandledKey(e.Key, sessionService, vtProcessor))
            return false;

        KeyEventArgs terminalKeyEventArgs;
        if (IsIsolatedModifierKey(e.Key))
        {
            TrackTerminalModifierKeyState(e.Key, isKeyDown: true, sessionService, vtProcessor);

            if (TrySendWin32InputSequence(e, sessionService, keyDown: true))
            {
                e.Handled = true;
                return true;
            }

            if (!ShouldReportKittyIsolatedModifierKeys(sessionService, vtProcessor))
            {
                e.Handled = true;
                return false;
            }

            TrackKittyReportedModifierKeyDown(e.Key);
            terminalKeyEventArgs = CreateNormalizedKeyEventArgs(e, isKeyDown: true);
        }
        else
        {
            terminalKeyEventArgs = CreateTrackedModifierKeyEventArgs(e, sessionService, vtProcessor);
        }

        bool handled = _inner.HandleKeyDown(terminalKeyEventArgs, sessionService, vtProcessor);
        if (handled && IsIsolatedModifierKey(e.Key))
            e.Handled = true;

        return handled;
    }

    public bool HandleKeyUp(KeyEventArgs e, ITerminalSessionService sessionService)
    {
        if (e.Handled && !CanForwardHandledKeyUp(e.Key, sessionService))
            return false;

        KeyEventArgs terminalKeyEventArgs;
        if (IsIsolatedModifierKey(e.Key))
        {
            if (TrySendWin32InputSequence(e, sessionService, keyDown: false))
            {
                TrackTerminalModifierKeyState(e.Key, isKeyDown: false, sessionService, vtProcessor: null);
                e.Handled = true;
                return true;
            }

            if (!ShouldReportKittyIsolatedModifierKeys(sessionService) && !WasKittyReportedModifierKeyDown(e.Key))
            {
                e.Handled = true;
                return false;
            }

            ClearKittyReportedModifierKeyDown(e.Key);
            terminalKeyEventArgs = CreateNormalizedKeyEventArgs(e, isKeyDown: false);
            TrackTerminalModifierKeyState(e.Key, isKeyDown: false, sessionService, vtProcessor: null);
        }
        else
        {
            terminalKeyEventArgs = CreateTrackedModifierKeyEventArgs(e, sessionService, vtProcessor: null);
        }

        bool handled = _inner.HandleKeyUp(terminalKeyEventArgs, sessionService);
        if (handled && IsIsolatedModifierKey(e.Key))
            e.Handled = true;

        return handled;
    }

    public bool HandleTextInput(TextInputEventArgs e, ITerminalSessionService sessionService)
    {
        if (e.Handled)
            return false;

        return _inner.HandleTextInput(e, sessionService);
    }

    public void ResetInputState()
    {
        _kittyReportedModifierKeyDown = KeyModifiers.None;
        _terminalModifierKeyState = KeyModifiers.None;

        if (_inner is IResettableTerminalInputAdapter resettableInner)
            resettableInner.ResetInputState();
    }

    private static bool IsIsolatedModifierKey(Key key) =>
        key is Key.LeftShift
            or Key.RightShift
            or Key.LeftCtrl
            or Key.RightCtrl
            or Key.LeftAlt
            or Key.RightAlt
            or Key.LWin
            or Key.RWin;

    private static bool TrySendWin32InputSequence(KeyEventArgs e, ITerminalSessionService sessionService, bool keyDown)
    {
        if (!IsWin32InputModeEnabled(sessionService))
            return false;

        KeyModifiers modifiers = IsIsolatedModifierKey(e.Key)
            ? NormalizeModifierKeyState(e.KeyModifiers, e.Key, keyDown)
            : e.KeyModifiers;

        if (!TryCreateWin32InputSequence(e.Key, modifiers, keyDown, out string sequence))
            return false;

        sessionService.SendInput(sequence);
        return true;
    }

    private static KeyEventArgs CreateNormalizedKeyEventArgs(KeyEventArgs source, bool isKeyDown) =>
        new()
        {
            Key = source.Key,
            PhysicalKey = source.PhysicalKey,
            KeyModifiers = NormalizeModifierKeyState(source.KeyModifiers, source.Key, isKeyDown),
            KeySymbol = source.KeySymbol,
        };

    private KeyEventArgs CreateTrackedModifierKeyEventArgs(
        KeyEventArgs source,
        ITerminalSessionService sessionService,
        IVtProcessor? vtProcessor)
    {
        if (_terminalModifierKeyState == KeyModifiers.None || !ShouldForwardModifierKeyState(sessionService, vtProcessor))
            return source;

        KeyModifiers keyModifiers = source.KeyModifiers | _terminalModifierKeyState;
        if (keyModifiers == source.KeyModifiers)
            return source;

        return new KeyEventArgs
        {
            Key = source.Key,
            PhysicalKey = source.PhysicalKey,
            KeyModifiers = keyModifiers,
            KeySymbol = source.KeySymbol,
        };
    }

    private static bool IsWin32InputModeEnabled(ITerminalSessionService sessionService) =>
        sessionService.ModeSource?.ModeState.Win32InputMode == true;

    private static int GetKittyKeyboardFlags(ITerminalSessionService sessionService) =>
        sessionService.ModeSource is IKittyKeyboardStateSource kitty
            ? kitty.KittyKeyboardFlags
            : 0;

    private static int GetKittyKeyboardFlags(ITerminalSessionService sessionService, IVtProcessor? vtProcessor)
    {
        if (vtProcessor is IKittyKeyboardStateSource kittyVtProcessor)
            return kittyVtProcessor.KittyKeyboardFlags;

        return GetKittyKeyboardFlags(sessionService);
    }

    private bool CanForwardHandledKey(
        Key key,
        ITerminalSessionService sessionService,
        IVtProcessor? vtProcessor) =>
        CanForwardHandledIsolatedModifierKey(key, sessionService, vtProcessor) ||
        CanForwardHandledTrackedModifierKey(sessionService, vtProcessor);

    private bool CanForwardHandledKeyUp(Key key, ITerminalSessionService sessionService) =>
        CanForwardHandledIsolatedModifierKeyUp(key, sessionService) ||
        CanForwardHandledTrackedModifierKey(sessionService, vtProcessor: null);

    private static bool CanForwardHandledIsolatedModifierKey(
        Key key,
        ITerminalSessionService sessionService,
        IVtProcessor? vtProcessor) =>
        IsIsolatedModifierKey(key) &&
        (IsWin32InputModeEnabled(sessionService) || ShouldReportKittyIsolatedModifierKeys(sessionService, vtProcessor));

    private bool CanForwardHandledIsolatedModifierKeyUp(Key key, ITerminalSessionService sessionService) =>
        IsIsolatedModifierKey(key) &&
        (IsWin32InputModeEnabled(sessionService) ||
         ShouldReportKittyIsolatedModifierKeys(sessionService) ||
         WasKittyReportedModifierKeyDown(key));

    private bool CanForwardHandledTrackedModifierKey(ITerminalSessionService sessionService, IVtProcessor? vtProcessor) =>
        _terminalModifierKeyState != KeyModifiers.None &&
        ShouldForwardModifierKeyState(sessionService, vtProcessor);

    private static bool TryCreateWin32InputSequence(
        Key key,
        KeyModifiers modifiers,
        bool keyDown,
        out string sequence)
    {
        sequence = string.Empty;

        if (!TryGetWin32ModifierKeyInfo(key, out ushort virtualKey, out ushort scanCode, out uint keyControlState))
            return false;

        uint controlState = GetControlKeyState(key, modifiers);
        if (keyDown)
            controlState |= keyControlState;
        else
            controlState &= ~keyControlState;

        controlState |= keyControlState & EnhancedKey;
        int keyDownValue = keyDown ? 1 : 0;
        sequence = $"\u001b[{virtualKey};{scanCode};0;{keyDownValue};{controlState};1_";
        return true;
    }

    private static bool TryGetWin32ModifierKeyInfo(
        Key key,
        out ushort virtualKey,
        out ushort scanCode,
        out uint controlState)
    {
        virtualKey = 0;
        scanCode = 0;
        controlState = 0;

        switch (key)
        {
            case Key.LeftShift:
                virtualKey = 0x10;
                scanCode = 0x2A;
                controlState = ShiftPressed;
                return true;
            case Key.RightShift:
                virtualKey = 0x10;
                scanCode = 0x36;
                controlState = ShiftPressed;
                return true;
            case Key.LeftCtrl:
                virtualKey = 0x11;
                scanCode = 0x1D;
                controlState = LeftCtrlPressed;
                return true;
            case Key.RightCtrl:
                virtualKey = 0x11;
                scanCode = 0x1D;
                controlState = RightCtrlPressed | EnhancedKey;
                return true;
            case Key.LeftAlt:
                virtualKey = 0x12;
                scanCode = 0x38;
                controlState = LeftAltPressed;
                return true;
            case Key.RightAlt:
                virtualKey = 0x12;
                scanCode = 0x38;
                controlState = RightAltPressed | EnhancedKey;
                return true;
            case Key.LWin:
                virtualKey = 0x5B;
                scanCode = 0x5B;
                controlState = EnhancedKey;
                return true;
            case Key.RWin:
                virtualKey = 0x5C;
                scanCode = 0x5C;
                controlState = EnhancedKey;
                return true;
            default:
                return false;
        }
    }

    private static uint GetControlKeyState(Key key, KeyModifiers modifiers)
    {
        uint controlState = 0;
        modifiers = RemoveCurrentModifierKeyState(modifiers, key);

        if (modifiers.HasFlag(KeyModifiers.Shift))
            controlState |= ShiftPressed;
        if (modifiers.HasFlag(KeyModifiers.Control))
            controlState |= LeftCtrlPressed;
        if (modifiers.HasFlag(KeyModifiers.Alt))
            controlState |= LeftAltPressed;

        return controlState;
    }

    private static KeyModifiers NormalizeModifierKeyState(KeyModifiers modifiers, Key key, bool isKeyDown)
    {
        KeyModifiers changedModifier = GetModifierKeyState(key);
        if (changedModifier == KeyModifiers.None)
            return modifiers;

        return isKeyDown
            ? modifiers | changedModifier
            : modifiers & ~changedModifier;
    }

    private static KeyModifiers RemoveCurrentModifierKeyState(KeyModifiers modifiers, Key key)
    {
        KeyModifiers changedModifier = GetModifierKeyState(key);
        return changedModifier == KeyModifiers.None
            ? modifiers
            : modifiers & ~changedModifier;
    }

    private static KeyModifiers GetModifierKeyState(Key key) =>
        key switch
        {
            Key.LeftShift or Key.RightShift => KeyModifiers.Shift,
            Key.LeftCtrl or Key.RightCtrl => KeyModifiers.Control,
            Key.LeftAlt or Key.RightAlt => KeyModifiers.Alt,
            Key.LWin or Key.RWin => KeyModifiers.Meta,
            _ => KeyModifiers.None,
        };

    private void TrackKittyReportedModifierKeyDown(Key key)
    {
        _kittyReportedModifierKeyDown |= GetModifierKeyState(key);
    }

    private void ClearKittyReportedModifierKeyDown(Key key)
    {
        _kittyReportedModifierKeyDown &= ~GetModifierKeyState(key);
    }

    private bool WasKittyReportedModifierKeyDown(Key key)
    {
        KeyModifiers modifier = GetModifierKeyState(key);
        return modifier != KeyModifiers.None && _kittyReportedModifierKeyDown.HasFlag(modifier);
    }

    private void TrackTerminalModifierKeyState(
        Key key,
        bool isKeyDown,
        ITerminalSessionService sessionService,
        IVtProcessor? vtProcessor)
    {
        KeyModifiers modifier = GetModifierKeyState(key);
        if (modifier == KeyModifiers.None)
            return;

        if (!isKeyDown)
        {
            _terminalModifierKeyState &= ~modifier;
            return;
        }

        if (ShouldForwardModifierKeyState(sessionService, vtProcessor))
            _terminalModifierKeyState |= modifier;
    }

    private bool ShouldForwardModifierKeyState(ITerminalSessionService sessionService, IVtProcessor? vtProcessor) =>
        IsWin32InputModeEnabled(sessionService) ||
        ShouldReportKittyIsolatedModifierKeys(sessionService, vtProcessor) ||
        _terminalModifierKeyState != KeyModifiers.None;

    private static bool ShouldReportKittyIsolatedModifierKeys(ITerminalSessionService sessionService)
    {
        if (sessionService.ModeSource is null)
            return false;

        int kittyFlags = GetKittyKeyboardFlags(sessionService);
        return ShouldReportKittyIsolatedModifierKeys(kittyFlags);
    }

    private static bool ShouldReportKittyIsolatedModifierKeys(
        ITerminalSessionService sessionService,
        IVtProcessor? vtProcessor)
    {
        int kittyFlags = GetKittyKeyboardFlags(sessionService, vtProcessor);
        return ShouldReportKittyIsolatedModifierKeys(kittyFlags);
    }

    private static bool ShouldReportKittyIsolatedModifierKeys(int kittyFlags)
    {
        bool reportsKeyStates = (kittyFlags & KittyReportEvents) != 0;
        bool reportsAllKeys = (kittyFlags & KittyReportAllKeysAsEscapeCodes) != 0;
        return reportsKeyStates && reportsAllKeys;
    }
}
