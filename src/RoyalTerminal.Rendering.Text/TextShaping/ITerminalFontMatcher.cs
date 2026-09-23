// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>Obtains owned fallback candidates independently of cluster selection policy.</summary>
internal interface ITerminalFontMatcher
{
    SKTypeface? MatchCharacter(string? familyName, SKFontStyle style, string[]? languageTags, int codepoint);
}

internal sealed class SkiaTerminalFontMatcher(SKFontManager manager) : ITerminalFontMatcher
{
    public SKTypeface? MatchCharacter(string? familyName, SKFontStyle style, string[]? languageTags, int codepoint)
        => manager.MatchCharacter(familyName, style, languageTags, codepoint);
}
