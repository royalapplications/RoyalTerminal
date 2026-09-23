// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia — Default terminal input adapter.

using Avalonia.Input;
using Avalonia.Input.TextInput;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Services;

namespace RoyalTerminal.Avalonia.Services;

/// <summary>
/// Default implementation for terminal keyboard and mouse input mapping.
/// </summary>
public sealed class DefaultTerminalInputAdapter : ITerminalInputAdapter, IResettableTerminalInputAdapter
{
    private readonly ITerminalKeyboardInputNormalizer _keyboardInputNormalizer;
    private readonly HashSet<int> _pressedKeys = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultTerminalInputAdapter"/> class.
    /// </summary>
    public DefaultTerminalInputAdapter()
        : this(TerminalKeyboardInputNormalizerFactory.Create())
    {
    }

    internal DefaultTerminalInputAdapter(ITerminalKeyboardInputNormalizer keyboardInputNormalizer)
    {
        _keyboardInputNormalizer = keyboardInputNormalizer ?? throw new ArgumentNullException(nameof(keyboardInputNormalizer));
    }

    /// <inheritdoc />
    public bool HandleKeyDown(KeyEventArgs e, ITerminalSessionService sessionService, IVtProcessor? vtProcessor)
    {
        int identity = KeyIdentity(e);
        TerminalInputAction action = identity != 0 && !_pressedKeys.Add(identity)
            ? TerminalInputAction.Repeat : TerminalInputAction.Press;
        TerminalModeState modeState = ResolveModeState(sessionService, vtProcessor);
        if (_keyboardInputNormalizer.HandleKeyDown(e, modeState) == TerminalKeyboardInputAction.SuppressForTextInput)
        {
            return false;
        }

        ITerminalInputSink? inputSink = sessionService.InputSink;
        if (inputSink is not null)
        {
            TerminalKeyEvent keyEvent = new(
                action,
                KeyCode: (uint)e.Key,
                Text: null,
                Modifiers: ConvertTerminalModifiers(e.KeyModifiers),
                IsComposing: false);
            return inputSink.SendKey(keyEvent);
        }

        if (HasFallbackByteInputPath(sessionService))
        {
            int kittyKeyboardFlags = ResolveKittyKeyboardFlags(sessionService, vtProcessor);
            if (ShouldPreferNativeKittyEncoder(kittyKeyboardFlags) &&
                TrySendNativeKeySequence(
                    e,
                    sessionService,
                    vtProcessor,
                    action,
                    e.KeySymbol) is bool kittyHandled)
            {
                return kittyHandled;
            }

            if (ShouldUseWin32InputMode(modeState) &&
                TerminalWin32InputSequenceEncoder.TryEncode(
                    e.Key,
                    e.KeyModifiers,
                    e.KeySymbol,
                    keyDown: true,
                    out string win32Sequence))
            {
                sessionService.SendInput(win32Sequence);
                return true;
            }

            bool modifyOtherKeys2 = (vtProcessor as ITerminalModifyOtherKeysStateSource ??
                sessionService.ModeSource as ITerminalModifyOtherKeysStateSource)?.ModifyOtherKeys2 == true;
            if ((!ShouldPreferTextInputForKeyDown(e, modeState, kittyKeyboardFlags) ||
                 (modifyOtherKeys2 && e.KeyModifiers != KeyModifiers.None)) &&
                TrySendNativeKeySequence(
                    e,
                    sessionService,
                    vtProcessor,
                    action,
                    e.KeySymbol) is bool backendHandled)
            {
                return backendHandled;
            }

            if (TerminalKeySequenceEncoder.TryEncode(e.Key, e.KeyModifiers, modeState, kittyKeyboardFlags, out string sequence))
            {
                sessionService.SendInput(sequence);
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public bool HandleKeyUp(KeyEventArgs e, ITerminalSessionService sessionService)
    {
        _pressedKeys.Remove(KeyIdentity(e));
        TerminalModeState modeState = ResolveModeState(sessionService, vtProcessor: null);
        if (_keyboardInputNormalizer.HandleKeyUp(e, modeState) == TerminalKeyboardInputAction.SuppressForTextInput)
        {
            return false;
        }

        ITerminalInputSink? inputSink = sessionService.InputSink;
        if (inputSink is null)
        {
            if (HasFallbackByteInputPath(sessionService))
            {
                int kittyKeyboardFlags = ResolveKittyKeyboardFlags(sessionService, vtProcessor: null);
                if (ShouldPreferNativeKittyEncoder(kittyKeyboardFlags) &&
                    TrySendNativeKeySequence(
                        e,
                        sessionService,
                        vtProcessor: null,
                        TerminalInputAction.Release,
                        text: null) is bool kittyHandled)
                {
                    return kittyHandled;
                }

                if (ShouldUseWin32InputMode(modeState) &&
                    TerminalWin32InputSequenceEncoder.TryEncode(
                        e.Key,
                        e.KeyModifiers,
                        keySymbol: null,
                        keyDown: false,
                        out string win32Sequence))
                {
                    sessionService.SendInput(win32Sequence);
                    return true;
                }

                if (TrySendNativeKeySequence(
                        e,
                        sessionService,
                        vtProcessor: null,
                        TerminalInputAction.Release,
                        text: null) is bool backendHandled)
                {
                    return backendHandled;
                }
            }

            return false;
        }

        TerminalKeyEvent keyEvent = new(
            TerminalInputAction.Release,
            KeyCode: (uint)e.Key,
            Text: null,
            Modifiers: ConvertTerminalModifiers(e.KeyModifiers),
            IsComposing: false);

        return inputSink.SendKey(keyEvent);
    }

    /// <inheritdoc />
    public bool HandleTextInput(TextInputEventArgs e, ITerminalSessionService sessionService)
    {
        if (string.IsNullOrEmpty(e.Text))
        {
            return false;
        }

        ITerminalInputSink? inputSink = sessionService.InputSink;
        if (inputSink is not null)
        {
            return inputSink.SendText(e.Text);
        }

        bool hasFallbackPath = HasFallbackByteInputPath(sessionService);
        if (!hasFallbackPath)
        {
            return false;
        }

        TerminalModeState modeState = ResolveModeState(sessionService, vtProcessor: null);
        if (ShouldUseWin32InputMode(modeState) && ResolveKittyKeyboardFlags(sessionService, vtProcessor: null) == 0)
        {
            return true;
        }

        sessionService.SendInput(e.Text);
        return true;
    }

    /// <inheritdoc />
    public void ResetInputState()
    {
        _pressedKeys.Clear();
        _keyboardInputNormalizer.ResetInputState();
    }

    // Physical identity survives a layout/modifier change between down and up.
    // Synthetic/headless events may only provide the logical key.
    private static int KeyIdentity(KeyEventArgs e) => e.PhysicalKey != PhysicalKey.None
        ? 0x10000 | (int)e.PhysicalKey : (int)e.Key;

    private static TerminalModeState ResolveModeState(
        ITerminalSessionService sessionService,
        IVtProcessor? vtProcessor)
    {
        ITerminalModeSource? modeSource = sessionService.ModeSource;
        if (modeSource is not null)
        {
            return modeSource.ModeState;
        }

        if (vtProcessor is not null)
        {
            return vtProcessor.ModeState;
        }

        return new TerminalModeState(
            CursorVisible: true,
            ApplicationCursorKeys: false,
            ApplicationKeypad: false,
            AlternateScreen: false,
            BracketedPaste: false);
    }

    private static TerminalModifiers ConvertTerminalModifiers(KeyModifiers keyModifiers)
    {
        TerminalModifiers mods = TerminalModifiers.None;
        if (keyModifiers.HasFlag(KeyModifiers.Shift)) mods |= TerminalModifiers.Shift;
        if (keyModifiers.HasFlag(KeyModifiers.Control)) mods |= TerminalModifiers.Control;
        if (keyModifiers.HasFlag(KeyModifiers.Alt)) mods |= TerminalModifiers.Alt;
        if (keyModifiers.HasFlag(KeyModifiers.Meta)) mods |= TerminalModifiers.Meta;
        return mods;
    }

    private static int ResolveKittyKeyboardFlags(
        ITerminalSessionService sessionService,
        IVtProcessor? vtProcessor)
    {
        if (vtProcessor is IKittyKeyboardStateSource kittyFromProcessor)
        {
            return kittyFromProcessor.KittyKeyboardFlags;
        }

        if (sessionService.ModeSource is IKittyKeyboardStateSource kittyFromModeSource)
        {
            return kittyFromModeSource.KittyKeyboardFlags;
        }

        return 0;
    }

    private static ITerminalKeySequenceEncoderSource? ResolveKeySequenceEncoderSource(
        ITerminalSessionService sessionService,
        IVtProcessor? vtProcessor)
    {
        if (vtProcessor is ITerminalKeySequenceEncoderSource encoderFromProcessor)
        {
            return encoderFromProcessor;
        }

        return sessionService.ModeSource as ITerminalKeySequenceEncoderSource;
    }

    // null permits fallback; false is authoritative suppression; true sent bytes.
    private static bool? TrySendNativeKeySequence(
        KeyEventArgs e,
        ITerminalSessionService sessionService,
        IVtProcessor? vtProcessor,
        TerminalInputAction action,
        string? text)
    {
        if (ResolveKeySequenceEncoderSource(sessionService, vtProcessor) is not ITerminalKeySequenceEncoderSource nativeEncoder)
            return null;
        if (!nativeEncoder.TryEncodeKey(
                new TerminalKeyEncodingRequest(
                    TerminalKeyEncodingIdentity.Get(e),
                    action,
                    text,
                    ConvertTerminalModifiers(e.KeyModifiers)),
                out byte[] nativeSequence))
        {
            return (nativeEncoder as ITerminalKeyEncodingPolicy)?.IsKeyEncodingAuthoritative == true ? false : null;
        }

        sessionService.SendInput(nativeSequence);
        return true;
    }

    private static bool ShouldPreferNativeKittyEncoder(int kittyKeyboardFlags) => kittyKeyboardFlags != 0;

    private static bool ShouldUseWin32InputMode(in TerminalModeState modeState)
    {
        return OperatingSystem.IsWindows() && modeState.Win32InputMode;
    }

    private static bool HasFallbackByteInputPath(ITerminalSessionService sessionService)
    {
        return sessionService.Endpoint is not null ||
               sessionService.HasActiveTransport ||
               sessionService.HasPty;
    }

    private static bool ShouldPreferTextInputForKeyDown(
        KeyEventArgs e,
        in TerminalModeState modeState,
        int kittyKeyboardFlags)
    {
        if (string.IsNullOrEmpty(e.KeySymbol))
        {
            return false;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
            e.KeyModifiers.HasFlag(KeyModifiers.Alt) ||
            e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            return false;
        }

        return !TerminalKeySequenceEncoder.TryEncode(
            e.Key,
            e.KeyModifiers,
            modeState,
            kittyKeyboardFlags,
            out _);
    }
}
