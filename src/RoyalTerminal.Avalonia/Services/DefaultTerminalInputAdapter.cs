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
public sealed class DefaultTerminalInputAdapter : ITerminalInputAdapter
{
    /// <inheritdoc />
    public bool HandleKeyDown(KeyEventArgs e, ITerminalSessionService sessionService, IVtProcessor? vtProcessor)
    {
        ITerminalInputSink? inputSink = sessionService.InputSink;
        if (inputSink is not null)
        {
            TerminalKeyEvent keyEvent = new(
                TerminalInputAction.Press,
                KeyCode: (uint)e.Key,
                Text: null,
                Modifiers: ConvertTerminalModifiers(e.KeyModifiers),
                IsComposing: false);
            return inputSink.SendKey(keyEvent);
        }

        if (HasFallbackByteInputPath(sessionService))
        {
            TerminalModeState modeState = ResolveModeState(sessionService, vtProcessor);
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

            int kittyKeyboardFlags = ResolveKittyKeyboardFlags(sessionService, vtProcessor);
            if (!ShouldPreferTextInputForKeyDown(e, modeState, kittyKeyboardFlags) &&
                ResolveKeySequenceEncoderSource(sessionService, vtProcessor) is ITerminalKeySequenceEncoderSource nativeEncoder &&
                nativeEncoder.TryEncodeKey(
                    new TerminalKeyEncodingRequest(
                        e.Key.ToString(),
                        TerminalInputAction.Press,
                        e.KeySymbol,
                        ConvertTerminalModifiers(e.KeyModifiers)),
                    out byte[] nativeSequence))
            {
                sessionService.SendInput(nativeSequence);
                return true;
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
        ITerminalInputSink? inputSink = sessionService.InputSink;
        if (inputSink is null)
        {
            if (HasFallbackByteInputPath(sessionService))
            {
                TerminalModeState modeState = ResolveModeState(sessionService, vtProcessor: null);
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

                if (ResolveKeySequenceEncoderSource(sessionService, vtProcessor: null) is ITerminalKeySequenceEncoderSource nativeEncoder &&
                    nativeEncoder.TryEncodeKey(
                        new TerminalKeyEncodingRequest(
                            e.Key.ToString(),
                            TerminalInputAction.Release,
                            null,
                            ConvertTerminalModifiers(e.KeyModifiers)),
                        out byte[] nativeSequence))
                {
                    sessionService.SendInput(nativeSequence);
                    return true;
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
        if (ShouldUseWin32InputMode(modeState))
        {
            return true;
        }

        sessionService.SendInput(e.Text);
        return true;
    }

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
