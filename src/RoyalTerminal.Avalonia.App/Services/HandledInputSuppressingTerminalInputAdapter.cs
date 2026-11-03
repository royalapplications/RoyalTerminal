// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.App - Terminal input adapter for handled Windows modifier input.

using Avalonia.Input;
using Avalonia.Input.TextInput;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;

namespace RoyalTerminal.Avalonia.App.Services;

internal sealed class HandledInputSuppressingTerminalInputAdapter : ITerminalInputAdapter, IResettableTerminalInputAdapter, ITerminalCompositionInputAdapter
{
    private const int KittyReportEvents = 0x02;
    private const int KittyReportAllKeysAsEscapeCodes = 0x08;

    private readonly ITerminalInputAdapter _inner;
    private bool _isComposing;
    private readonly HashSet<Key> _compositionKeys = [];
    private readonly HashSet<Key> _forwardedModifierKeys = [];
    private KeyModifiers _terminalModifierKeyState;

    public HandledInputSuppressingTerminalInputAdapter(ITerminalInputAdapter inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public bool HandleKeyDown(KeyEventArgs e, ITerminalSessionService sessionService, IVtProcessor? vtProcessor)
    {
        if (_isComposing)
        {
            _compositionKeys.Add(e.Key);
            return _inner is ITerminalCompositionInputAdapter && _inner.HandleKeyDown(e, sessionService, vtProcessor);
        }
        _compositionKeys.Remove(e.Key);
        if (e.Handled && !CanForwardHandledKey(e.Key, sessionService, vtProcessor))
            return false;

        KeyEventArgs terminalKeyEventArgs;
        if (IsIsolatedModifierKey(e.Key))
        {
            TrackTerminalModifierKeyState(e.Key, isKeyDown: true, sessionService, vtProcessor);

            if (IsWin32InputModeEnabled(sessionService) && GetKittyKeyboardFlags(sessionService, vtProcessor) == 0)
            {
                _forwardedModifierKeys.Add(e.Key);
                bool accepted = _inner.HandleKeyDown(e, sessionService, vtProcessor);
                if (accepted) e.Handled = true;
                return accepted;
            }

            if (!ShouldReportKittyIsolatedModifierKeys(sessionService, vtProcessor))
            {
                e.Handled = true;
                return false;
            }

            _forwardedModifierKeys.Add(e.Key);
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
        bool forwardedModifier = _forwardedModifierKeys.Remove(e.Key);
        if (IsIsolatedModifierKey(e.Key))
            TrackTerminalModifierKeyState(e.Key, isKeyDown: false, sessionService, vtProcessor: null);
        bool compositionKey = _compositionKeys.Remove(e.Key);
        if (_isComposing || compositionKey)
            return _inner is ITerminalCompositionInputAdapter && _inner.HandleKeyUp(e, sessionService);
        if (e.Handled && !forwardedModifier && !CanForwardHandledKeyUp(e.Key, sessionService))
            return false;

        KeyEventArgs terminalKeyEventArgs;
        if (IsIsolatedModifierKey(e.Key))
        {
            // A forwarded press updates the inner adapter's physical state even
            // when an encoder emits no bytes. Always deliver its release; the
            // current encoder decides whether the protocol permits output.
            if (forwardedModifier ||
                IsWin32InputModeEnabled(sessionService) && GetKittyKeyboardFlags(sessionService) == 0)
            {
                bool accepted = _inner.HandleKeyUp(CreateNormalizedKeyEventArgs(e, isKeyDown: false), sessionService);
                e.Handled = true;
                return accepted;
            }

            if (!ShouldReportKittyIsolatedModifierKeys(sessionService))
            {
                e.Handled = true;
                return false;
            }

            terminalKeyEventArgs = CreateNormalizedKeyEventArgs(e, isKeyDown: false);
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
        _isComposing = false;
        _compositionKeys.Clear();
        _forwardedModifierKeys.Clear();
        _terminalModifierKeyState = KeyModifiers.None;

        if (_inner is IResettableTerminalInputAdapter resettableInner)
            resettableInner.ResetInputState();
    }

    public void SetComposing(bool composing)
    {
        _isComposing = composing;
        if (_inner is ITerminalCompositionInputAdapter composition) composition.SetComposing(composing);
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
         ShouldReportKittyIsolatedModifierKeys(sessionService));

    private bool CanForwardHandledTrackedModifierKey(ITerminalSessionService sessionService, IVtProcessor? vtProcessor) =>
        _terminalModifierKeyState != KeyModifiers.None &&
        ShouldForwardModifierKeyState(sessionService, vtProcessor);

    private static KeyModifiers NormalizeModifierKeyState(KeyModifiers modifiers, Key key, bool isKeyDown)
    {
        KeyModifiers changedModifier = GetModifierKeyState(key);
        if (changedModifier == KeyModifiers.None)
            return modifiers;

        return isKeyDown
            ? modifiers | changedModifier
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
            foreach (Key heldKey in _forwardedModifierKeys)
                _terminalModifierKeyState |= GetModifierKeyState(heldKey);
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
