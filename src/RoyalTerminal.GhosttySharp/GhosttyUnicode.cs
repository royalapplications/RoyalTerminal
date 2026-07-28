// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

/// <summary>
/// Unicode width helpers that use the same tables and grapheme rules as Ghostty.
/// </summary>
public static class GhosttyUnicode
{
    /// <summary>Gets the terminal-cell width of one Unicode codepoint.</summary>
    public static byte GetCodepointWidth(uint codepoint)
    {
        NativeLibraryLoader.Initialize();
        return GhosttyVtNative.UnicodeCodepointWidth(codepoint);
    }

    /// <summary>
    /// Measures the first complete grapheme cluster and returns its width and consumed codepoint count.
    /// </summary>
    public static unsafe nuint GetGraphemeWidth(
        ReadOnlySpan<uint> codepoints,
        out byte width)
    {
        NativeLibraryLoader.Initialize();
        byte measuredWidth = 0;
        fixed (uint* codepointsPtr = codepoints)
        {
            nuint consumed = GhosttyVtNative.UnicodeGraphemeWidth(
                codepointsPtr,
                (nuint)codepoints.Length,
                &measuredWidth);
            width = measuredWidth;
            return consumed;
        }
    }
}
