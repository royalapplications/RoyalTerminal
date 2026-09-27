// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Text;
using RoyalTerminal.Unicode;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

public sealed partial class TerminalFontResolver
{
    private readonly Dictionary<TerminalTypefaceCollection, CollectionState> _collections = new();
    private readonly HashSet<SKTypeface> _borrowedTypefaces = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Resolves a scalar using ordered configured faces, regular-style fallback and
    /// loaded discovery faces. The caller must keep configured faces alive until
    /// this resolver is disposed. Invalid scalars return the configured primary.
    /// </summary>
    /// <exception cref="InvalidOperationException">No face covers the scalar, U+FFFD or space.</exception>
    public TerminalFontResolution ResolveTypeface(TerminalTypefaceCollection collection,
        TerminalTypefaceStyle style, int codepoint, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(collection);
        TerminalTypefaceCollection.ValidateStyle(style);
        lock (_sync)
        {
            ThrowIfDisposed();
            CollectionState state = GetCollectionState(collection);
            SKTypeface primary = collection.GetPrimaryTypeface(style);
            if (!Rune.IsValid(codepoint)) return new(primary, false);
            return TryResolveTypefaceCore(primary, codepoint, culture, null, out TerminalFontResolution result, state, style)
                ? result : ResolveReplacement(primary, culture, null, state, style);
        }
    }

    /// <summary>
    /// Resolves one UTF-16 cluster against ordered configured and discovered faces.
    /// Configured faces are borrowed, not disposed by the resolver. Whole-cluster
    /// replacement and explicit presentation follow the single-face overload.
    /// </summary>
    /// <exception cref="InvalidOperationException">No face covers the cluster, U+FFFD or space.</exception>
    public TerminalFontResolution ResolveTypeface(TerminalTypefaceCollection collection,
        TerminalTypefaceStyle style, ReadOnlySpan<char> text, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(collection);
        TerminalTypefaceCollection.ValidateStyle(style);
        lock (_sync)
        {
            ThrowIfDisposed();
            return ResolveText(collection.GetPrimaryTypeface(style), text, culture, GetCollectionState(collection), style);
        }
    }

    /// <summary>Resolves one string cluster; configured faces remain caller-owned.</summary>
    /// <exception cref="InvalidOperationException">No face covers the cluster, U+FFFD or space.</exception>
    public TerminalFontResolution ResolveTypeface(TerminalTypefaceCollection collection,
        TerminalTypefaceStyle style, string text, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        return ResolveTypeface(collection, style, text.AsSpan(), culture);
    }

    private CollectionState GetCollectionState(TerminalTypefaceCollection collection)
    {
        if (_collections.TryGetValue(collection, out CollectionState? state)) return state;
        state = new(collection);
        // Register every style before calling discovery: a matcher may return a
        // configured object from another style, including a rejected candidate.
        for (TerminalTypefaceStyle style = TerminalTypefaceStyle.Regular; style <= TerminalTypefaceStyle.BoldItalic; style++)
            foreach (SKTypeface face in collection.GetFaces(style)) _borrowedTypefaces.Add(face);
        _collections.Add(collection, state);
        return state;
    }

    private bool TryResolveCollectionFace(CollectionState state, TerminalTypefaceStyle style,
        SKTypeface primary, int codepoint, CultureInfo? culture, bool? presentation,
        out TerminalFontResolution resolution)
    {
        CultureInfo usedCulture = culture ?? CultureInfo.CurrentUICulture;
        CollectionCodepointKey key = new(style, codepoint, presentation, usedCulture.Name);
        if (!state.Results.TryGetValue(key, out SKTypeface? face))
        {
            face = FindCodepointOverride(state, codepoint);
            face ??= FindConfiguredFace(state, style, codepoint, presentation);
            if (face is null && style != TerminalTypefaceStyle.Regular)
                face = FindConfiguredFace(state, TerminalTypefaceStyle.Regular, codepoint, presentation);

            bool requiredPresentation = presentation ?? new Codepoint((uint)codepoint).IsEmojiPresentation;
            face ??= FindLoadedFace(state, codepoint, presentation, requiredPresentation);
            if (face is null)
            {
                // Ghostty discovers only regular faces, irrespective of the
                // requested style or the regular face's intrinsic font style.
                TerminalFontResolution discovered = ResolveCachedFallback(
                    state.Configured.GetPrimaryTypeface(), codepoint, usedCulture, requiredPresentation, SKFontStyle.Normal);
                if (discovered.UsedFallback)
                {
                    face = discovered.Typeface;
                    AddLoadedFace(state, face, isFallback: true);
                }
            }
            // ANY is restricted to already loaded regular faces. A rejected
            // wrong-presentation discovery candidate never enters this list.
            face ??= FindConfiguredFace(state, TerminalTypefaceStyle.Regular, codepoint, null);
            face ??= FindLoadedFace(state, codepoint, null, null);
            // SharedGrid caches misses as well as hits. Loading another face
            // must not silently change a previously resolved scalar's result.
            state.Results.Add(key, face);
        }
        resolution = new(face ?? primary, face is not null && face.Handle != primary.Handle);
        return face is not null;
    }

    private SKTypeface? FindConfiguredFace(CollectionState state, TerminalTypefaceStyle style,
        int codepoint, bool? presentation)
    {
        foreach (SKTypeface face in state.Configured.GetFaces(style))
            // Avoid querying/marshalling the family name for every scalar the
            // configured face cannot cover (the common discovery path).
            if (ContainsGlyph(face, codepoint, presentation) && !TerminalFontCoverage.IsLastResort(face)) return face;
        return null;
    }

    private SKTypeface? FindLoadedFace(CollectionState state, int codepoint, bool? presentation, bool? fallbackPresentation)
    {
        // Mapped and discovered faces retain insertion order, but only fallback
        // discovery imposes UCD default presentation; mappings are configured faces.
        foreach (LoadedCollectionFace face in state.Loaded)
            if (ContainsGlyph(face.Typeface, codepoint, presentation ?? (face.IsFallback ? fallbackPresentation : null))) return face.Typeface;
        return null;
    }

    private sealed class CollectionState(TerminalTypefaceCollection configured)
    {
        public TerminalTypefaceCollection Configured { get; } = configured;
        public List<LoadedCollectionFace> Loaded { get; } = new();
        public Dictionary<string, SKTypeface?> Descriptors { get; } = new(StringComparer.Ordinal);
        public Dictionary<CollectionCodepointKey, SKTypeface?> Results { get; } = new();
    }

    private readonly record struct CollectionCodepointKey(TerminalTypefaceStyle Style,
        int Codepoint, bool? Presentation, string CultureName);
}
