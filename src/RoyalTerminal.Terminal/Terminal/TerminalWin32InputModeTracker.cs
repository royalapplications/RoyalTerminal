// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Incremental tracker for selected DEC private modes.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Tracks DEC private modes 9001 (win32 input mode) and 1004 (focus events)
/// by scanning terminal output.
/// Supports chunked input where escape sequences can span multiple buffers.
/// </summary>
public sealed class TerminalWin32InputModeTracker
{
    private enum ParserState
    {
        Ground,
        Escape,
        CsiEntry,
        CsiParam,
        CsiIntermediate,
    }

    private ParserState _state;
    private readonly int[] _parameters = new int[8];
    private int _parameterCount;
    private int _currentParameter;
    private bool _hasCurrentParameterDigits;
    private bool _privateMode;
    private char _privateMarker;
    private char _intermediate;
    private bool _win32InputMode;
    private bool _focusEventMode;

    /// <summary>
    /// Gets whether DEC private mode 9001 is currently active.
    /// </summary>
    public bool Win32InputMode => _win32InputMode;

    /// <summary>
    /// Gets whether DEC private mode 1004 (focus events) is currently active.
    /// </summary>
    public bool FocusEventMode => _focusEventMode;

    /// <summary>
    /// Resets parser state and tracked mode state.
    /// </summary>
    public void Reset()
    {
        _state = ParserState.Ground;
        ResetCsiParser();
        _win32InputMode = false;
        _focusEventMode = false;
    }

    /// <summary>
    /// Scans terminal output bytes and updates tracked mode state.
    /// </summary>
    /// <returns><see langword="true"/> when tracked mode state changed.</returns>
    public bool Process(ReadOnlySpan<byte> data)
    {
        bool beforeWin32 = _win32InputMode;
        bool beforeFocus = _focusEventMode;

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
                        _win32InputMode = false;
                        _focusEventMode = false;
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

                case ParserState.CsiIntermediate:
                    ProcessCsiIntermediateByte(b);
                    break;
            }
        }

        return beforeWin32 != _win32InputMode || beforeFocus != _focusEventMode;
    }

    private void ProcessCsiEntryByte(byte b)
    {
        if (b == (byte)'?')
        {
            _privateMode = true;
            _privateMarker = '?';
            _state = ParserState.CsiParam;
            return;
        }

        if (b == (byte)'!')
        {
            _privateMarker = '!';
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

        if (b is >= 0x20 and <= 0x2F)
        {
            PushCurrentParameterIfNeeded();
            _intermediate = (char)b;
            _state = ParserState.CsiIntermediate;
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

    private void ProcessCsiIntermediateByte(byte b)
    {
        if (b is >= 0x20 and <= 0x2F)
        {
            _intermediate = (char)b;
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

        if (_privateMode && _privateMarker == '?' && finalByte is 'h' or 'l')
        {
            bool set = finalByte == 'h';
            for (int i = 0; i < _parameterCount; i++)
            {
                if (_parameters[i] == 9001)
                {
                    _win32InputMode = set;
                }
                else if (_parameters[i] == 1004)
                {
                    _focusEventMode = set;
                }
            }
        }
        else if (_privateMarker == '!' && finalByte == 'p') // DECSTR
        {
            _win32InputMode = false;
            _focusEventMode = false;
        }

        _state = ParserState.Ground;
        ResetCsiParser();
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
        _privateMarker = '\0';
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
}
