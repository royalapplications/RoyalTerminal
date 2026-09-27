// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class TerminalFontResolver
{
    // Host sprite drawing must yield only to a successfully resolved mapping.
    // An unavailable override must not disable the built-in replacement glyph.
    internal bool TryGetCodepointOverride(TerminalTypefaceCollection collection, int codepoint, out SKTypeface? face)
    {
        face = null;
        if (!collection.HasCodepointMappings || !Rune.IsValid(codepoint)) return false;
        lock (_sync)
        {
            ThrowIfDisposed();
            CollectionState state = GetCollectionState(collection);
            Dictionary<int, SKTypeface?> cache = state.SpriteOverrides ??= new();
            if (!cache.TryGetValue(codepoint, out face))
            {
                face = FindCodepointOverride(state, codepoint);
                cache.Add(codepoint, face);
            }
            return face is not null;
        }
    }

    private SKTypeface? FindCodepointOverride(CollectionState state, int codepoint)
    {
        string? family = state.Configured.GetCodepointFamily(codepoint);
        if (family is null || _fontMatcher is not ITerminalFontFamilyMatcher matcher) return null;
        if (!state.Descriptors.TryGetValue(family, out SKTypeface? face))
        {
            face = matcher.MatchFamily(family);
            if (face is not null && TerminalFontCoverage.IsLastResort(face))
            {
                ReleaseRejectedTypeface(face, state.Configured.GetPrimaryTypeface());
                face = null;
            }
            // Cache successful descriptors even when this particular codepoint
            // is missing. Other ranges using that descriptor reuse its face.
            state.Descriptors.Add(family, face);
            if (face is not null) AddLoadedFace(state, face, isFallback: false);
        }
        return face is not null && ContainsGlyph(face, codepoint) ? face : null;
    }

    private static void AddLoadedFace(CollectionState state, SKTypeface face, bool isFallback)
    {
        foreach (LoadedCollectionFace entry in state.Loaded)
            if (entry.Typeface.Handle == face.Handle && entry.IsFallback == isFallback) return;
        state.Loaded.Add(new(face, isFallback));
    }

    private readonly record struct LoadedCollectionFace(SKTypeface Typeface, bool IsFallback);
}
