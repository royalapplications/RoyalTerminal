// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Text;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    private int _dcsBufferLimit;

    private void ProcessDcsHeader(byte value)
    {
        if (value == 0x1B)
        {
            EnterCsiState();
            _state = ParserState.Escape;
            return;
        }
        if (_state == ParserState.DcsIgnore) return;
        if (value is >= 0x80 and <= 0x9F) { ProcessC1(value); return; }
        if (value < 0x20 || value >= 0x7F) return;
        if (value is >= 0x40 and <= 0x7E) { FinishDcsHeader(value); return; }
        if (value is >= 0x20 and <= 0x2F)
        {
            CollectDcsIntermediate(value);
            _state = ParserState.DcsIntermediate;
            return;
        }
        if (_state == ParserState.DcsIntermediate) { _state = ParserState.DcsIgnore; return; }
        if (value is >= (byte)'0' and <= (byte)'9')
        {
            if (_params.Count < 24)
            {
                _currentParam = Math.Min(ushort.MaxValue, _currentParam * 10 + value - '0');
                _hasParam = true;
            }
            _state = ParserState.DcsParam;
        }
        else if (value == ';')
        {
            if (_params.Count < 24)
            {
                _params.Add(_hasParam ? _currentParam : 0);
                _currentParam = 0;
                _hasParam = false;
            }
            _state = ParserState.DcsParam;
        }
        else if (_state == ParserState.DcsEntry && value is >= 0x3C and <= 0x3F)
        {
            CollectDcsIntermediate(value);
            _state = ParserState.DcsParam;
        }
        else _state = ParserState.DcsIgnore;
    }

    private void CollectDcsIntermediate(byte value)
    {
        if (_intermediateCount == 0) _intermediateChar = (char)value;
        _intermediateCount = Math.Min(4, _intermediateCount + 1);
    }

    private void FinishDcsHeader(byte final)
    {
        _state = ParserState.DcsString;
        _isDiscardingDcsPayload = true;
        if (_params.Count >= 24) return; // Upstream skips the hook on overflow.
        if (_hasParam) _params.Add(_currentParam);
        if (final != 'q') return;

        if (_intermediateCount == 1 && _intermediateChar is '$' or '+')
        {
            // These handlers ignore header parameters. DECRQSS accepts at most
            // two request bytes; XTGETTCAP has Ghostty's 1 MiB payload budget.
            _dcsBufferLimit = _intermediateChar == '$' ? 4 : MaxDcsQueryBytes + 2;
            _dcsBuffer.Add((byte)_intermediateChar);
            _dcsBuffer.Add(final);
            _isDiscardingDcsPayload = false;
        }
        else if (_intermediateCount == 0 && _sixelGraphicsEnabled)
        {
            _dcsBufferLimit = _options.SixelDecoderOptions.MaxInputBytes;
            _isDiscardingDcsPayload = false;
            Span<byte> digits = stackalloc byte[5];
            for (int i = 0; i < _params.Count; i++)
            {
                if (i > 0) AppendDcsByteOrDiscard((byte)';');
                Utf8Formatter.TryFormat(_params[i], digits, out int written);
                foreach (byte digit in digits[..written]) AppendDcsByteOrDiscard(digit);
            }
            AppendDcsByteOrDiscard(final);
        }
    }
}
