// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Optional VT capability for predicting terminal layout with the processor's Unicode rules.
/// </summary>
public interface ITerminalUnicodeWidthProvider
{
    /// <summary>Gets the width of one Unicode codepoint in terminal cells.</summary>
    byte GetCodepointWidth(uint codepoint);

    /// <summary>
    /// Measures the first grapheme cluster and returns its width and consumed codepoint count.
    /// </summary>
    nuint GetGraphemeWidth(ReadOnlySpan<uint> codepoints, out byte width);
}
