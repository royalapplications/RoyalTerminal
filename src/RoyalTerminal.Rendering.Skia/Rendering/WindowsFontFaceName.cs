// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Runtime.InteropServices;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>Recovers full Windows face names truncated by LOGFONT's 31-character limit.</summary>
internal static partial class WindowsFontFaceName
{
    internal static unsafe string? TryExpand(string name)
    {
        if (!OperatingSystem.IsWindows() || name.Length != 31) return null;
        nint dc = CreateCompatibleDC(0);
        if (dc == 0) return null;
        nint font = 0, previous = 0;
        try
        {
            // Match the stored GDI face, then read its actual full name. Never
            // guess a style from a truncated prefix or accept GDI substitution.
            font = CreateFontW(-16, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, name);
            if (font == 0) return null;
            previous = SelectObject(dc, font);
            if (previous == 0 || previous == -1) return null;
            int count = GetTextFaceW(dc, 0, null);
            if (count <= name.Length + 1 || count > 1024) return null;
            char[] buffer = new char[count];
            fixed (char* pointer = buffer)
                if (GetTextFaceW(dc, count, pointer) == 0) return null;
            int end = buffer.AsSpan().IndexOf('\0');
            if (end <= name.Length) return null;
            string fullName = new(buffer, 0, end);
            return fullName.StartsWith(name, StringComparison.OrdinalIgnoreCase) ? fullName : null;
        }
        finally
        {
            if (previous != 0 && previous != -1) _ = SelectObject(dc, previous);
            if (font != 0) _ = DeleteObject(font);
            _ = DeleteDC(dc);
        }
    }

    [LibraryImport("gdi32.dll")]
    private static partial nint CreateCompatibleDC(nint dc);
    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateFontW(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeout, uint charset, uint outputPrecision, uint clipPrecision,
        uint quality, uint pitchAndFamily, string face);
    [LibraryImport("gdi32.dll")]
    private static partial nint SelectObject(nint dc, nint value);
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint value);
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint dc);
    [LibraryImport("gdi32.dll")]
    private static unsafe partial int GetTextFaceW(nint dc, int count, char* name);
}
