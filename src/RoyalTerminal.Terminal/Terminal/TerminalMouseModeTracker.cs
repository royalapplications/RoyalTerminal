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

    private bool _mode1000;
    private bool _mode1002;
    private bool _mode1003;
    private bool _mode1005;
    private bool _mode1006;
    private bool _mode1015;
    private bool _mode1016;
    private bool _mode9;

    /// <summary>
    /// Gets the current mouse mode snapshot.
    /// </summary>
    public TerminalMouseModeState ModeState => new(
        GetTrackingMode(),
        GetEncodingMode());

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
        else if (_intermediate == '!' && finalByte == 'p') // DECSTR
        {
            ResetMouseModes();
        }

        _state = ParserState.Ground;
        ResetCsiParser();
    }

    private void ApplyPrivateMode(int mode, bool set)
    {
        switch (mode)
        {
            case 1000:
                _mode1000 = set;
                break;
            case 1002:
                _mode1002 = set;
                break;
            case 1003:
                _mode1003 = set;
                break;
            case 1005:
                _mode1005 = set;
                break;
            case 1006:
                _mode1006 = set;
                break;
            case 1015:
                _mode1015 = set;
                break;
            case 1016:
                _mode1016 = set;
                break;
            case 9:
                _mode9 = set;
                break;
        }
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
        _mode1000 = false;
        _mode1002 = false;
        _mode1003 = false;
        _mode1005 = false;
        _mode1006 = false;
        _mode1015 = false;
        _mode1016 = false;
        _mode9 = false;
    }

    private TerminalMouseTrackingMode GetTrackingMode()
    {
        if (_mode1003)
        {
            return TerminalMouseTrackingMode.AnyMotion;
        }

        if (_mode1002)
        {
            return TerminalMouseTrackingMode.ButtonMotion;
        }

        if (_mode1000)
        {
            return TerminalMouseTrackingMode.PressRelease;
        }

        if (_mode9)
        {
            return TerminalMouseTrackingMode.X10Press;
        }

        return TerminalMouseTrackingMode.None;
    }

    private TerminalMouseEncoding GetEncodingMode()
    {
        if (_mode1016)
        {
            return TerminalMouseEncoding.SgrPixels;
        }

        if (_mode1006)
        {
            return TerminalMouseEncoding.Sgr;
        }

        if (_mode1015)
        {
            return TerminalMouseEncoding.Urxvt;
        }

        if (_mode1005)
        {
            return TerminalMouseEncoding.Utf8;
        }

        return TerminalMouseEncoding.Default;
    }
}
