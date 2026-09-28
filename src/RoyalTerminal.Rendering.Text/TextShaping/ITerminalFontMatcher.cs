// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>Obtains owned fallback candidates independently of cluster selection policy.</summary>
internal interface ITerminalFontMatcher
{
    SKTypeface? MatchCharacter(string? familyName, SKFontStyle style, string[]? languageTags, int codepoint);
}

/// <summary>Looks up an exact family without substituting another family on a miss.</summary>
internal interface ITerminalFontFamilyMatcher
{
    SKTypeface? MatchFamily(string familyName);
}

/// <summary>
/// Supplies additional fallback candidates after the platform's first match was
/// rejected. Each yielded reference transfers to the caller; disposing the
/// iterator releases only untransferred discovery resources.
/// Probes skipped inside the source use releaseRejected so shared references
/// retained by earlier resolver results cannot be disposed accidentally.
/// </summary>
internal interface ITerminalFontCandidateMatcher
{
    IEnumerable<SKTypeface> MatchCandidates(SKFontStyle style, string[]? languageTags, int codepoint, Action<SKTypeface> releaseRejected);
}

internal sealed class SkiaTerminalFontMatcher(SKFontManager manager) : ITerminalFontMatcher, ITerminalFontFamilyMatcher, ITerminalFontCandidateMatcher
{
    internal const string AppleColorEmojiFamily = "Apple Color Emoji";

    public SKTypeface? MatchCharacter(string? familyName, SKFontStyle style, string[]? languageTags, int codepoint)
        => manager.MatchCharacter(familyName, style, languageTags, codepoint);

    public SKTypeface? MatchFamily(string familyName)
    {
        using SKFontStyleSet styles = manager.GetFontStyles(familyName);
        return styles.Count == 0 ? null : styles.CreateTypeface(SKFontStyle.Normal);
    }

    public IEnumerable<SKTypeface> MatchCandidates(SKFontStyle style, string[]? languageTags, int codepoint, Action<SKTypeface> releaseRejected)
        => TerminalFontCandidates.Enumerate(manager, style, languageTags, codepoint, releaseRejected);
}
