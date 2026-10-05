// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>Codepoint-constrained CoreText descriptors with Ghostty's fallback ranking.</summary>
internal static partial class CoreTextFontCandidates
{
    private const string CoreText = "/System/Library/Frameworks/CoreText.framework/CoreText";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    internal static List<NamedFontCandidate>? Find(SKFontStyle style, int codepoint)
    {
        try { return FindCore(style, codepoint); }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
        catch (BadImageFormatException) { return null; }
    }

    private static unsafe List<NamedFontCandidate>? FindCore(SKFontStyle style, int codepoint)
    {
        nint textLibrary = 0, foundationLibrary = 0;
        nint charset = 0, attributes = 0, descriptor = 0, descriptors = 0, collection = 0, matches = 0, unrestricted = 0;
        try
        {
            textLibrary = NativeLibrary.Load(CoreText);
            foundationLibrary = NativeLibrary.Load(CoreFoundation);
            nint charsetKey = Constant(textLibrary, "kCTFontCharacterSetAttribute");
            nint familyKey = Constant(textLibrary, "kCTFontFamilyNameAttribute");
            nint nameKey = Constant(textLibrary, "kCTFontNameAttribute");
            nint urlKey = Constant(textLibrary, "kCTFontURLAttribute");
            nint styleKey = Constant(textLibrary, "kCTFontStyleNameAttribute");
            nint axisIdKey = Constant(textLibrary, "kCTFontVariationAxisIdentifierKey");
            nint axisDefaultKey = Constant(textLibrary, "kCTFontVariationAxisDefaultValueKey");
            nint dictionaryKeys = NativeLibrary.GetExport(foundationLibrary, "kCFTypeDictionaryKeyCallBacks");
            nint dictionaryValues = NativeLibrary.GetExport(foundationLibrary, "kCFTypeDictionaryValueCallBacks");
            nint arrayCallbacks = NativeLibrary.GetExport(foundationLibrary, "kCFTypeArrayCallBacks");
            charset = Native.CFCharacterSetCreateWithCharactersInRange(0, new(codepoint, 1));
            if (charset == 0) return null;
            attributes = Native.CFDictionaryCreate(0, &charsetKey, &charset, 1, dictionaryKeys, dictionaryValues);
            if (attributes == 0) return null;
            descriptor = Native.CTFontDescriptorCreateWithAttributes(attributes);
            if (descriptor == 0) return null;
            // Match Ghostty's CoreText.discover collection query. The descriptor
            // matching API can synchronously request downloadable fonts when
            // no installed face covers a scalar, blocking the rendering thread.
            descriptors = Native.CFArrayCreate(0, &descriptor, 1, arrayCallbacks);
            if (descriptors == 0) return null;
            collection = Native.CTFontCollectionCreateWithFontDescriptors(descriptors, 0);
            if (collection == 0) return null;
            matches = Native.CTFontCollectionCreateMatchingFontDescriptors(collection);
            if (matches == 0) return [];
            // The query's character-set restriction must not leak into the
            // reusable font. Ghostty removes it before materializing a face.
            nint nullValue = Constant(foundationLibrary, "kCFNull");
            unrestricted = Native.CFDictionaryCreate(0, &charsetKey, &nullValue, 1, dictionaryKeys, dictionaryValues);
            if (unrestricted == 0) return null;
            List<NamedFontCandidate> candidates = [];
            HashSet<string> names = new(StringComparer.Ordinal);
            Span<char> characters = stackalloc char[2];
            int length = new Rune(codepoint).EncodeToUtf16(characters);
            Span<ushort> glyphs = stackalloc ushort[2];
            for (nint index = 0, count = Native.CFArrayGetCount(matches); index < count; index++)
            {
                nint match = Native.CFArrayGetValueAtIndex(matches, index);
                nint unrestrictedDescriptor = Native.CTFontDescriptorCreateCopyWithAttributes(match, unrestricted);
                if (unrestrictedDescriptor == 0) continue;
                nint font = 0;
                try
                {
                    font = Native.CTFontCreateWithFontDescriptor(unrestrictedDescriptor, 12, 0);
                    if (font == 0) continue;
                    fixed (char* text = characters)
                    fixed (ushort* output = glyphs)
                        if (Native.CTFontGetGlyphsForCharacters(font, text, output, length) == 0) continue;
                    string? family = ReadAttribute(unrestrictedDescriptor, familyKey);
                    string? name = ReadAttribute(unrestrictedDescriptor, nameKey);
                    if (string.IsNullOrEmpty(family) || string.IsNullOrEmpty(name) || !names.Add(name)) continue;
                    uint traits = Native.CTFontGetSymbolicTraits(font);
                    bool bold = (traits & 2) != 0, italic = (traits & 1) != 0;
                    ReadTableTraits(font, 0x68656164 /* head */, 44, 1, 2, ref bold, ref italic);
                    ReadTableTraits(font, 0x4F532F32 /* OS/2 */, 62, 32, 1, ref bold, ref italic);
                    ReadVariationTraits(font, axisIdKey, axisDefaultKey, ref bold, ref italic);
                    uint score = Score(Native.CTFontGetGlyphCount(font), (traits & (1 << 10)) != 0,
                        bold, italic, style, ReadAttribute(unrestrictedDescriptor, styleKey));
                    candidates.Add(new(family, name, score, ReadPath(unrestrictedDescriptor, urlKey)));
                }
                finally
                {
                    if (font != 0) Native.CFRelease(font);
                    Native.CFRelease(unrestrictedDescriptor);
                }
            }
            candidates.Sort(static (left, right) =>
            {
                int rank = right.Score.CompareTo(left.Score);
                return rank != 0 ? rank : string.CompareOrdinal(left.PostScriptName, right.PostScriptName);
            });
            return candidates;
        }
        finally
        {
            if (unrestricted != 0) Native.CFRelease(unrestricted);
            if (matches != 0) Native.CFRelease(matches);
            if (collection != 0) Native.CFRelease(collection);
            if (descriptors != 0) Native.CFRelease(descriptors);
            if (descriptor != 0) Native.CFRelease(descriptor);
            if (attributes != 0) Native.CFRelease(attributes);
            if (charset != 0) Native.CFRelease(charset);
            if (textLibrary != 0) NativeLibrary.Free(textLibrary);
            if (foundationLibrary != 0) NativeLibrary.Free(foundationLibrary);
        }
    }

    // Codepoint coverage is already checked. Keep Ghostty's packed ordering:
    // mono, exact name, slant, weight, fuzzy name, then total glyph coverage.
    internal static uint Score(nint glyphCount, bool monospace, bool bold, bool italic, SKFontStyle requested, string? styleName = null)
    {
        bool desiredBold = requested.Weight >= 700, desiredItalic = requested.Slant != SKFontStyleSlant.Upright;
        (string first, string second, string third, string fourth) = (desiredBold, desiredItalic) switch
        {
            (true, true) => ("bold italic", "bold", "italic", "oblique"),
            (true, false) => ("bold", "upright", "", ""),
            (false, true) => ("italic", "regular", "oblique", ""),
            _ => ("regular", "upright", "", ""),
        };
        uint nameScore = 0;
        if (styleName is not null)
        {
            if (styleName.Equals(first, StringComparison.OrdinalIgnoreCase)) nameScore |= 1u << 26;
            int unmatched = Math.Min(255, Encoding.UTF8.GetByteCount(styleName));
            Discount(first); Discount(second); Discount(third); Discount(fourth);
            nameScore |= (uint)(255 - unmatched) << 16;

            void Discount(string desired)
            {
                if (desired.Length != 0 && styleName.Contains(desired, StringComparison.OrdinalIgnoreCase))
                    unmatched = Math.Max(0, unmatched - desired.Length);
            }
        }
        return (uint)Math.Clamp((long)glyphCount, 0, ushort.MaxValue) | nameScore |
           (bold == (requested.Weight >= 700) ? 1u << 24 : 0) |
           (italic == (requested.Slant != SKFontStyleSlant.Upright) ? 1u << 25 : 0) |
           (monospace ? 1u << 27 : 0);
    }

    private static unsafe void ReadTableTraits(nint font, uint tag, int offset, int boldMask, int italicMask, ref bool bold, ref bool italic)
    {
        nint data = Native.CTFontCopyTable(font, tag, 0);
        if (data == 0) return;
        try
        {
            nint length = Native.CFDataGetLength(data);
            if (length < offset + 2) return;
            ReadOnlySpan<byte> table = new((void*)Native.CFDataGetBytePtr(data), offset + 2);
            RefineTableTraits(table, offset, boldMask, italicMask, ref bold, ref italic);
        }
        finally { Native.CFRelease(data); }
    }

    internal static void RefineTableTraits(ReadOnlySpan<byte> table, int offset, int boldMask, int italicMask, ref bool bold, ref bool italic)
    {
        if (table.Length < offset + 2) return;
        ushort flags = BinaryPrimitives.ReadUInt16BigEndian(table[offset..]);
        bold |= (flags & boldMask) != 0;
        italic |= (flags & italicMask) != 0;
    }

    private static void ReadVariationTraits(nint font, nint idKey, nint defaultKey, ref bool bold, ref bool italic)
    {
        nint axes = Native.CTFontCopyVariationAxes(font);
        if (axes == 0) return;
        nint values = 0;
        try
        {
            values = Native.CTFontCopyVariation(font);
            if (values == 0) return;
            bool italSeen = false;
            for (nint index = 0, count = Native.CFArrayGetCount(axes); index < count; index++)
            {
                nint axis = Native.CFArrayGetValueAtIndex(axes, index);
                nint id = Native.CFDictionaryGetValue(axis, idKey);
                if (id == 0 || Native.CFNumberGetInt32(id, 3 /* kCFNumberSInt32Type */, out int tag) == 0) continue;
                nint value = Native.CFDictionaryGetValue(values, id);
                if (value == 0) value = Native.CFDictionaryGetValue(axis, defaultKey);
                if (value == 0 || Native.CFNumberGetDouble(value, 13 /* kCFNumberDoubleType */, out double amount) == 0) continue;
                RefineVariationTraits(tag, amount, ref bold, ref italic, ref italSeen);
            }
        }
        finally
        {
            if (values != 0) Native.CFRelease(values);
            Native.CFRelease(axes);
        }
    }

    internal static void RefineVariationTraits(int tag, double amount, ref bool bold, ref bool italic, ref bool italSeen)
    {
        // Use stable OpenType tags, not localized CoreText axis display names.
        if (tag == 0x77676874 /* wght */) bold = amount > 600;
        else if (tag == 0x6974616C /* ital */) { italic = amount > 0.5; italSeen = true; }
        else if (tag == 0x736C6E74 /* slnt */ && !italSeen) italic = amount <= -5;
    }

    private static nint Constant(nint library, string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(library, name));

    private static string? ReadAttribute(nint descriptor, nint key)
    {
        nint value = Native.CTFontDescriptorCopyAttribute(descriptor, key);
        if (value == 0) return null;
        try { return ReadString(value); }
        finally { Native.CFRelease(value); }
    }

    private static string? ReadPath(nint descriptor, nint key)
    {
        nint url = Native.CTFontDescriptorCopyAttribute(descriptor, key);
        if (url == 0) return null;
        nint path = 0;
        try
        {
            path = Native.CFURLCopyFileSystemPath(url, 0 /* kCFURLPOSIXPathStyle */);
            return path == 0 ? null : ReadString(path);
        }
        finally
        {
            if (path != 0) Native.CFRelease(path);
            Native.CFRelease(url);
        }
    }

    private static unsafe string ReadString(nint value)
        => string.Create(checked((int)Native.CFStringGetLength(value)), value, static (characters, text) =>
        {
            fixed (char* output = characters) Native.CFStringGetCharacters(text, new(0, characters.Length), output);
        });

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct CfRange(nint Location, nint Length);

    private static partial class Native
    {
        [LibraryImport(CoreFoundation)] internal static partial void CFRelease(nint value);
        [LibraryImport(CoreFoundation)] internal static partial nint CFCharacterSetCreateWithCharactersInRange(nint allocator, CfRange range);
        [LibraryImport(CoreFoundation)] internal static unsafe partial nint CFDictionaryCreate(nint allocator, nint* keys, nint* values, nint count, nint keyCallbacks, nint valueCallbacks);
        [LibraryImport(CoreFoundation)] internal static unsafe partial nint CFArrayCreate(nint allocator, nint* values, nint count, nint callbacks);
        [LibraryImport(CoreFoundation)] internal static partial nint CFArrayGetCount(nint array);
        [LibraryImport(CoreFoundation)] internal static partial nint CFArrayGetValueAtIndex(nint array, nint index);
        [LibraryImport(CoreFoundation)] internal static partial nint CFStringGetLength(nint value);
        [LibraryImport(CoreFoundation)] internal static partial nint CFDataGetLength(nint data);
        [LibraryImport(CoreFoundation)] internal static partial nint CFDataGetBytePtr(nint data);
        [LibraryImport(CoreFoundation)] internal static partial nint CFDictionaryGetValue(nint dictionary, nint key);
        [LibraryImport(CoreFoundation, EntryPoint = "CFNumberGetValue")] internal static partial byte CFNumberGetInt32(nint value, nint type, out int result);
        [LibraryImport(CoreFoundation, EntryPoint = "CFNumberGetValue")] internal static partial byte CFNumberGetDouble(nint value, nint type, out double result);
        [LibraryImport(CoreFoundation)] internal static unsafe partial void CFStringGetCharacters(nint value, CfRange range, char* output);
        [LibraryImport(CoreFoundation)] internal static partial nint CFURLCopyFileSystemPath(nint url, nint style);
        [LibraryImport(CoreText)] internal static partial nint CTFontDescriptorCreateWithAttributes(nint attributes);
        [LibraryImport(CoreText)] internal static partial nint CTFontCollectionCreateWithFontDescriptors(nint descriptors, nint options);
        [LibraryImport(CoreText)] internal static partial nint CTFontCollectionCreateMatchingFontDescriptors(nint collection);
        [LibraryImport(CoreText)] internal static partial nint CTFontDescriptorCreateCopyWithAttributes(nint descriptor, nint attributes);
        [LibraryImport(CoreText)] internal static partial nint CTFontDescriptorCopyAttribute(nint descriptor, nint key);
        [LibraryImport(CoreText)] internal static partial nint CTFontCreateWithFontDescriptor(nint descriptor, double size, nint matrix);
        [LibraryImport(CoreText)] internal static partial nint CTFontGetGlyphCount(nint font);
        [LibraryImport(CoreText)] internal static partial nint CTFontCopyTable(nint font, uint tag, uint options);
        [LibraryImport(CoreText)] internal static partial nint CTFontCopyVariationAxes(nint font);
        [LibraryImport(CoreText)] internal static partial nint CTFontCopyVariation(nint font);
        [LibraryImport(CoreText)] internal static partial uint CTFontGetSymbolicTraits(nint font);
        [LibraryImport(CoreText)] internal static unsafe partial byte CTFontGetGlyphsForCharacters(nint font, char* characters, ushort* glyphs, nint count);
    }
}
