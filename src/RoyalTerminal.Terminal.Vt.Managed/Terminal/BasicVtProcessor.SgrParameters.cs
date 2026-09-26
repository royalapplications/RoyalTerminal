// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    private void AddCsiParameter(bool colon, bool final = false)
    {
        if (_params.Count >= 24) { _state = ParserState.CsiIgnore; return; }
        if (colon) _csiColonSeparators |= 1u << _params.Count;
        _params.Add(_hasParam ? _currentParam : 0);
        _currentParam = 0;
        _hasParam = false;
        if (!final && _params.Count == 24) _state = ParserState.CsiIgnore;
    }

    private bool HasColon(int index) => (_csiColonSeparators & (1u << index)) != 0;

    private int CountColonAfter(int index)
    {
        int count = 0;
        while (index < _params.Count - 1 && HasColon(index)) { count++; index++; }
        return count;
    }

    private bool ProcessSgrParameterGroup(ref int index)
    {
        int code = _params[index];
        bool colon = HasColon(index);
        if (colon && code is not (4 or 38 or 48 or 58))
        {
            index++;
            while (index < _params.Count && HasColon(index)) index++;
            return true;
        }
        if (code == 4 && colon)
        {
            if (index + 1 >= _params.Count) return true;
            if (HasColon(index + 1)) { index += CountColonAfter(index + 1) + 1; return true; }
            int style = _params[++index];
            _currentUnderlineStyle = style <= 5 ? (TerminalUnderlineStyle)style : TerminalUnderlineStyle.Single;
            if (_currentUnderlineStyle == TerminalUnderlineStyle.None) _currentAttrs &= ~CellAttributes.Underline;
            else _currentAttrs |= CellAttributes.Underline;
            return true;
        }
        if (code is not (38 or 48 or 58)) return false;
        if (index + 1 >= _params.Count) return true;
        int mode = _params[index + 1];
        if (mode == 5 && index + 2 < _params.Count)
        {
            int palette = _params[index + 2] & 0xFF;
            if (code == 38) SetForegroundPalette(palette);
            else if (code == 48) SetBackgroundPalette(palette);
            else
            {
                _currentUnderlineColor = PaletteColor(palette);
                _currentUnderlineIdentity = TerminalColorIdentity.Palette((byte)palette);
                _currentHasUnderlineColor = true;
            }
            index += 2;
        }
        else if (mode == 2 && index + 4 < _params.Count)
        {
            int first = index + 2;
            if (colon)
            {
                int count = CountColonAfter(index + 1);
                if (count is not (3 or 4)) { index += count + 1; return true; }
                if (count == 4) first++;
            }
            // Ghostty truncates out-of-range color components to eight bits.
            uint rgb = 0xFF000000u | ((uint)(_params[first] & 0xFF) << 16) |
                ((uint)(_params[first + 1] & 0xFF) << 8) | (uint)(_params[first + 2] & 0xFF);
            if (code == 38) { _currentFg = rgb; _currentFgKind = SgrColorKind.Rgb; }
            else if (code == 48) { _currentBg = rgb; _currentBgKind = SgrColorKind.Rgb; }
            else
            {
                _currentUnderlineColor = rgb;
                _currentUnderlineIdentity = TerminalColorIdentity.Rgb(rgb);
                _currentHasUnderlineColor = true;
            }
            index = first + 2;
        }
        return true;
    }
}
