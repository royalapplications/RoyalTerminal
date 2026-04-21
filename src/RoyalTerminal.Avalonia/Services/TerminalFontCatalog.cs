// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using SkiaSharp;

namespace RoyalTerminal.Avalonia.Services;

/// <summary>
/// Discovers terminal fonts through the same Skia font manager used by the renderer.
/// </summary>
public static class TerminalFontCatalog
{
    /// <summary>
    /// Gets installed system font family names.
    /// </summary>
    public static IReadOnlyList<string> GetSystemFontFamilies()
    {
        try
        {
            using SKFontManager fontManager = SKFontManager.CreateDefault();
            string[] families = fontManager.GetFontFamilies();
            return NormalizeFamilies(families);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Attempts to read the family name from a font file.
    /// </summary>
    public static string? TryGetFontFamilyNameFromFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            using SKTypeface? typeface = SKTypeface.FromFile(filePath, 0);
            return string.IsNullOrWhiteSpace(typeface?.FamilyName)
                ? null
                : typeface.FamilyName.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> NormalizeFamilies(IReadOnlyList<string> families)
    {
        SortedSet<string> sortedFamilies = new(StringComparer.CurrentCultureIgnoreCase);
        for (int i = 0; i < families.Count; i++)
        {
            string family = families[i];
            if (!string.IsNullOrWhiteSpace(family))
            {
                sortedFamilies.Add(family.Trim());
            }
        }

        return sortedFamilies.ToArray();
    }
}
