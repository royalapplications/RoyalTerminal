// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>Fontconfig's untrimmed ranking, retaining alternate color/text faces.</summary>
internal static partial class FontconfigFontCandidates
{
    private const string Library = "libfontconfig.so.1";

    internal static List<FontFileCandidate>? Find(SKFontStyle style, string[]? languages, int codepoint)
    {
        try { return FindCore(style, languages, codepoint); }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
        catch (BadImageFormatException) { return null; }
    }

    private static unsafe List<FontFileCandidate>? FindCore(SKFontStyle style, string[]? languages, int codepoint)
    {
        nint config = Native.FcConfigGetCurrent();
        if (config == 0) return null;
        // Retain the active configuration, including application-added fonts,
        // rather than reparsing system configuration for every missing glyph.
        config = Native.FcConfigReference(config);
        nint pattern = 0, charset = 0, set = 0;
        try
        {
            pattern = Native.FcPatternCreate();
            charset = Native.FcCharSetCreate();
            if (pattern == 0 || charset == 0 || Native.FcCharSetAddChar(charset, (uint)codepoint) == 0 ||
                Native.FcPatternAddCharSet(pattern, "charset", charset) == 0 ||
                Native.FcPatternAddInteger(pattern, "spacing", 100 /* FC_MONO */) == 0 ||
                Native.FcPatternAddInteger(pattern, "weight", Native.FcWeightFromOpenType(style.Weight)) == 0 ||
                Native.FcPatternAddInteger(pattern, "slant", style.Slant == SKFontStyleSlant.Upright ? 0 :
                    style.Slant == SKFontStyleSlant.Italic ? 100 : 110) == 0) return null;
            if (languages is not null)
                foreach (string language in languages)
                    if (Native.FcPatternAddString(pattern, "lang", language) == 0) return null;
            if (Native.FcConfigSubstitute(config, pattern, 0 /* FcMatchPattern */) == 0) return null;
            Native.FcDefaultSubstitute(pattern);
            // trim=false is essential: equal character coverage is NOT equal
            // presentation coverage, so another font must remain discoverable.
            set = Native.FcFontSort(config, pattern, 0, 0, out int result);
            if (set == 0 || result != 0) return null;
            FcFontSet* fonts = (FcFontSet*)set;
            List<FontFileCandidate> candidates = [];
            HashSet<FontFileCandidate> seen = [];
            for (int index = 0; index < fonts->Count; index++)
            {
                nint source = ((nint*)fonts->Fonts)[index];
                if (Native.FcPatternGetCharSet(source, "charset", 0, out nint available) != 0 ||
                    Native.FcCharSetHasChar(available, (uint)codepoint) == 0) continue;
                nint prepared = Native.FcFontRenderPrepare(config, pattern, source);
                if (prepared == 0) continue;
                try
                {
                    if (Native.FcPatternGetString(prepared, "file", 0, out nint path) != 0 || path == 0) continue;
                    if (Native.FcPatternGetInteger(prepared, "index", 0, out int faceIndex) != 0) faceIndex = 0;
                    string? file = Marshal.PtrToStringUTF8(path);
                    if (string.IsNullOrEmpty(file)) continue;
                    // Fontconfig paths are relative to the configured sysroot.
                    nint root = Native.FcConfigGetSysRoot(config);
                    if (root != 0 && Marshal.PtrToStringUTF8(root) is { Length: > 0 } prefix)
                        file = Path.Combine(prefix, file.TrimStart(Path.DirectorySeparatorChar));
                    FontFileCandidate candidate = new(file, faceIndex);
                    if (seen.Add(candidate)) candidates.Add(candidate);
                }
                finally { Native.FcPatternDestroy(prepared); }
            }
            return candidates;
        }
        finally
        {
            if (set != 0) Native.FcFontSetDestroy(set);
            if (charset != 0) Native.FcCharSetDestroy(charset);
            if (pattern != 0) Native.FcPatternDestroy(pattern);
            Native.FcConfigDestroy(config);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct FcFontSet(int Count, int Capacity, nint Fonts);

    private static partial class Native
    {
        [LibraryImport(Library)] internal static partial nint FcConfigGetCurrent();
        [LibraryImport(Library)] internal static partial nint FcConfigReference(nint config);
        [LibraryImport(Library)] internal static partial void FcConfigDestroy(nint config);
        [LibraryImport(Library)] internal static partial nint FcConfigGetSysRoot(nint config);
        [LibraryImport(Library)] internal static partial nint FcPatternCreate();
        [LibraryImport(Library)] internal static partial void FcPatternDestroy(nint pattern);
        [LibraryImport(Library)] internal static partial nint FcCharSetCreate();
        [LibraryImport(Library)] internal static partial void FcCharSetDestroy(nint charset);
        [LibraryImport(Library)] internal static partial int FcCharSetAddChar(nint charset, uint codepoint);
        [LibraryImport(Library)] internal static partial int FcCharSetHasChar(nint charset, uint codepoint);
        [LibraryImport(Library)] internal static partial int FcWeightFromOpenType(int weight);
        [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)] internal static partial int FcPatternAddCharSet(nint pattern, string name, nint charset);
        [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)] internal static partial int FcPatternAddInteger(nint pattern, string name, int value);
        [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)] internal static partial int FcPatternAddString(nint pattern, string name, string value);
        [LibraryImport(Library)] internal static partial int FcConfigSubstitute(nint config, nint pattern, int kind);
        [LibraryImport(Library)] internal static partial void FcDefaultSubstitute(nint pattern);
        [LibraryImport(Library)] internal static partial nint FcFontSort(nint config, nint pattern, int trim, nint charset, out int result);
        [LibraryImport(Library)] internal static partial void FcFontSetDestroy(nint set);
        [LibraryImport(Library)] internal static partial nint FcFontRenderPrepare(nint config, nint pattern, nint font);
        [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)] internal static partial int FcPatternGetCharSet(nint pattern, string name, int index, out nint charset);
        [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)] internal static partial int FcPatternGetString(nint pattern, string name, int index, out nint value);
        [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)] internal static partial int FcPatternGetInteger(nint pattern, string name, int index, out int value);
    }
}
