// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>Obtains owned fallback candidates independently of cluster selection policy.</summary>
internal interface ITerminalFontMatcher
{
    SKTypeface? MatchCharacter(string? familyName, SKFontStyle style, string[]? languageTags, int codepoint);
}

/// <summary>Looks up a known family without character-fallback discovery.</summary>
internal interface ITerminalFontFamilyMatcher
{
    SKTypeface? MatchFamily(string familyName);
}

internal sealed class SkiaTerminalFontMatcher(SKFontManager manager) : ITerminalFontMatcher, ITerminalFontFamilyMatcher
{
    internal const string AppleColorEmojiFamily = "Apple Color Emoji";

    public SKTypeface? MatchCharacter(string? familyName, SKFontStyle style, string[]? languageTags, int codepoint)
        => manager.MatchCharacter(familyName, style, languageTags, codepoint);

    public SKTypeface? MatchFamily(string familyName) => manager.MatchFamily(familyName, SKFontStyle.Normal);
}
