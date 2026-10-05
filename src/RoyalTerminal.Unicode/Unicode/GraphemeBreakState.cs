// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// Boundary algorithm adapted from uucode src/grapheme.zig (MIT):
// Copyright (c) 2026 Jacob Sandlund.
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

namespace RoyalTerminal.Unicode;

/// <summary>
/// Unicode 18 boundary state. Linker and emoji-prefix state are independent:
/// GB9c may overlap GB11, and no longer requires a leading consonant.
/// </summary>
internal struct GraphemeBreakState
{
    private bool _afterLinker;
    private Prefix _prefix;

    private enum Prefix : byte { None, ExtendedPictographic, RegionalIndicator }

    internal bool IsBreak(uint previous, uint current, bool terminal = false)
    {
        ushort previousProperties = Unicode18Data.Get(previous);
        ushort currentProperties = Unicode18Data.Get(current);
        GraphemeBreakClass left = (GraphemeBreakClass)(previousProperties & 31);
        GraphemeBreakClass right = (GraphemeBreakClass)(currentProperties & 31);
        IndicConjunctBreakClass leftIndic = (IndicConjunctBreakClass)((previousProperties >> 8) & 3);
        IndicConjunctBreakClass rightIndic = (IndicConjunctBreakClass)((currentProperties >> 8) & 3);
        bool leftModifier = terminal && (previousProperties & (1 << 14)) != 0;
        bool rightModifier = terminal && (currentProperties & (1 << 14)) != 0;
        bool leftModifierBase = terminal && (previousProperties & (1 << 15)) != 0;
        bool rightModifierBase = terminal && (currentProperties & (1 << 15)) != 0;
        bool leftExtend = left == GraphemeBreakClass.Extend && !leftModifier;
        bool rightExtend = right == GraphemeBreakClass.Extend && !rightModifier;
        bool leftPictographic = left == GraphemeBreakClass.ExtendedPictographic || leftModifierBase;
        bool rightPictographic = right == GraphemeBreakClass.ExtendedPictographic || rightModifierBase;

        // Ghostty's terminal printer has already handled controls. Its public
        // grapheme-width helper uses the same no-control property table.
        if (terminal)
        {
            if (IsControl(left)) left = GraphemeBreakClass.Other;
            if (IsControl(right)) right = GraphemeBreakClass.Other;
        }

        bool afterLinker = leftIndic == IndicConjunctBreakClass.Linker ||
            (_afterLinker && leftIndic == IndicConjunctBreakClass.Extend);
        _afterLinker = afterLinker && rightIndic == IndicConjunctBreakClass.Extend;

        if (_prefix == Prefix.RegionalIndicator &&
            (left != GraphemeBreakClass.RegionalIndicator || right != GraphemeBreakClass.RegionalIndicator))
        {
            _prefix = Prefix.None;
        }
        else if (_prefix == Prefix.ExtendedPictographic &&
            (!(leftExtend || leftModifier || leftPictographic || left == GraphemeBreakClass.ZWJ) ||
             !(rightExtend || rightModifier || rightPictographic || right == GraphemeBreakClass.ZWJ)))
        {
            _prefix = Prefix.None;
        }

        if (left == GraphemeBreakClass.CR && right == GraphemeBreakClass.LF) return false; // GB3
        if (IsControl(left) || IsControl(right)) return true; // GB4, GB5
        if (left == GraphemeBreakClass.L && right is GraphemeBreakClass.L or GraphemeBreakClass.V or GraphemeBreakClass.LV or GraphemeBreakClass.LVT) return false; // GB6
        if (left is GraphemeBreakClass.LV or GraphemeBreakClass.V && right is GraphemeBreakClass.V or GraphemeBreakClass.T) return false; // GB7
        if (left is GraphemeBreakClass.LVT or GraphemeBreakClass.T && right == GraphemeBreakClass.T) return false; // GB8
        if (right == GraphemeBreakClass.SpacingMark) return false; // GB9a
        if (left == GraphemeBreakClass.Prepend) return false; // GB9b
        if (afterLinker && rightIndic == IndicConjunctBreakClass.Consonant)
        {
            _prefix = Prefix.None;
            return false; // GB9c
        }

        if (leftPictographic)
        {
            if (rightExtend || right == GraphemeBreakClass.ZWJ || (leftModifierBase && rightModifier))
            {
                _prefix = Prefix.ExtendedPictographic;
                return false;
            }
        }
        else if (_prefix == Prefix.ExtendedPictographic)
        {
            if ((leftExtend || leftModifier) && (rightExtend || right == GraphemeBreakClass.ZWJ)) return false;
            if (left == GraphemeBreakClass.ZWJ && rightPictographic)
            {
                _prefix = Prefix.None;
                return false; // GB11
            }
            _prefix = Prefix.None;
        }

        if (left == GraphemeBreakClass.RegionalIndicator && right == GraphemeBreakClass.RegionalIndicator)
        {
            if (_prefix == Prefix.None)
            {
                _prefix = Prefix.RegionalIndicator;
                return false;
            }
            _prefix = Prefix.None;
            return true; // GB12, GB13
        }
        return !(rightExtend || right == GraphemeBreakClass.ZWJ); // GB9, GB999
    }

    private static bool IsControl(GraphemeBreakClass value)
        => value is GraphemeBreakClass.Control or GraphemeBreakClass.CR or GraphemeBreakClass.LF;
}
