// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Incremental tracker for DEC mouse mode sequences.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Tracks DEC private mouse modes by scanning terminal output for mode set/reset sequences.
/// Supports chunked input where control sequences can span multiple buffers.
/// </summary>
public sealed class TerminalMouseModeTracker
{
    private enum ParserState
    {
        Ground,
        Escape,
        CsiEntry,
        CsiParam,
    }

    private ParserState _state;
    private readonly int[] _parameters = new int[16];
    private int _parameterCount;
    private int _currentParameter;
    private bool _hasCurrentParameterDigits;
    private bool _privateMode;
    private char _intermediate;

    private byte _modeBits;
    private byte _savedModeBits;
    private TerminalMouseModeState _mouseModeState;

    /// <summary>
    /// Gets the current mouse mode snapshot.
    /// </summary>
    public TerminalMouseModeState ModeState => _mouseModeState;

    /// <summary>
    /// Resets parser state and all tracked mouse modes.
    /// </summary>
    public void Reset()
    {
        _state = ParserState.Ground;
        ResetCsiParser();
        ResetMouseModes();
    }

    /// <summary>
    /// Scans terminal output bytes and updates the tracked mode state.
    /// </summary>
    /// <returns><see langword="true"/> when the tracked mouse mode changed.</returns>
    public bool Process(ReadOnlySpan<byte> data)
    {
        TerminalMouseModeState before = ModeState;

        foreach (byte b in data)
        {
            switch (_state)
            {
                case ParserState.Ground:
                    if (b == 0x1B)
                    {
                        _state = ParserState.Escape;
                    }
                    else if (b == 0x9B) // 8-bit CSI
                    {
                        BeginCsi();
                    }
                    break;

                case ParserState.Escape:
                    if (b == (byte)'[')
                    {
                        BeginCsi();
                    }
                    else if (b == (byte)'c') // RIS
                    {
                        ResetMouseModes();
                        _state = ParserState.Ground;
                    }
                    else
                    {
                        _state = b == 0x1B ? ParserState.Escape : ParserState.Ground;
                    }
                    break;

                case ParserState.CsiEntry:
                    ProcessCsiEntryByte(b);
                    break;

                case ParserState.CsiParam:
                    ProcessCsiParamByte(b);
                    break;
            }
        }

        return before != ModeState;
    }

    private void ProcessCsiEntryByte(byte b)
    {
        if (b == (byte)'?')
        {
            _privateMode = true;
            _state = ParserState.CsiParam;
            return;
        }

        if (b == (byte)'!')
        {
            _intermediate = '!';
            _state = ParserState.CsiParam;
            return;
        }

        if (b is >= (byte)'0' and <= (byte)'9')
        {
            _currentParameter = b - (byte)'0';
            _hasCurrentParameterDigits = true;
            _state = ParserState.CsiParam;
            return;
        }

        if (b == (byte)';')
        {
            PushParameter(value: 0);
            _state = ParserState.CsiParam;
            return;
        }

        if (b is >= 0x40 and <= 0x7E)
        {
            ExecuteCsi((char)b);
            return;
        }

        if (b == 0x1B)
        {
            _state = ParserState.Escape;
            return;
        }

        _state = ParserState.Ground;
        ResetCsiParser();
    }

    private void ProcessCsiParamByte(byte b)
    {
        if (b is >= (byte)'0' and <= (byte)'9')
        {
            _currentParameter = (_currentParameter * 10) + (b - (byte)'0');
            _hasCurrentParameterDigits = true;
            return;
        }

        if (b == (byte)';')
        {
            bool hadCurrentParameter = _hasCurrentParameterDigits;
            PushCurrentParameterIfNeeded();
            if (!hadCurrentParameter)
            {
                PushParameter(value: 0);
            }
            _currentParameter = 0;
            _hasCurrentParameterDigits = false;
            return;
        }

        if (b is >= 0x40 and <= 0x7E)
        {
            ExecuteCsi((char)b);
            return;
        }

        if (b == 0x1B)
        {
            _state = ParserState.Escape;
            ResetCsiParser();
            return;
        }

        _state = ParserState.Ground;
        ResetCsiParser();
    }

    private void ExecuteCsi(char finalByte)
    {
        PushCurrentParameterIfNeeded();

        if (_privateMode && (finalByte is 'h' or 'l'))
        {
            bool set = finalByte == 'h';
            for (int i = 0; i < _parameterCount; i++)
            {
                ApplyPrivateMode(_parameters[i], set);
            }
        }
        else if (_privateMode && finalByte is 's' or 'r')
        {
            for (int i = 0; i < _parameterCount; i++)
            {
                int index = TerminalMouseModeTransitions.Modes.IndexOf(_parameters[i]);
                if (index < 0) continue;
                int bit = 1 << index;
                if (finalByte == 'r') ApplyPrivateMode(_parameters[i], (_savedModeBits & bit) != 0);
                else _savedModeBits = (byte)((_savedModeBits & ~bit) | (_modeBits & bit));
            }
        }
        else if (_intermediate == '!' && finalByte == 'p') // DECSTR
        {
            ResetMouseModes();
        }

        _state = ParserState.Ground;
        ResetCsiParser();
    }

    private void ApplyPrivateMode(int mode, bool set)
    {
        int index = TerminalMouseModeTransitions.Modes.IndexOf(mode);
        if (index < 0) return;
        int bit = 1 << index;
        _modeBits = (byte)(set ? _modeBits | bit : _modeBits & ~bit);
        _mouseModeState = TerminalMouseModeTransitions.Apply(_mouseModeState, mode, set);
    }

    private void BeginCsi()
    {
        _state = ParserState.CsiEntry;
        ResetCsiParser();
    }

    private void ResetCsiParser()
    {
        _parameterCount = 0;
        _currentParameter = 0;
        _hasCurrentParameterDigits = false;
        _privateMode = false;
        _intermediate = '\0';
    }

    private void PushCurrentParameterIfNeeded()
    {
        if (_hasCurrentParameterDigits)
        {
            PushParameter(_currentParameter);
            _currentParameter = 0;
            _hasCurrentParameterDigits = false;
        }
    }

    private void PushParameter(int value)
    {
        if (_parameterCount >= _parameters.Length)
        {
            return;
        }

        _parameters[_parameterCount++] = value;
    }

    private void ResetMouseModes()
    {
        _modeBits = _savedModeBits = 0;
        _mouseModeState = default;
    }
}
