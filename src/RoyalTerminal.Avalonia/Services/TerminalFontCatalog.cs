// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using SkiaSharp;

namespace RoyalTerminal.Avalonia.Services;

/// <summary>
/// Discovers terminal fonts through the same Skia font manager used by the renderer.
/// </summary>
public static class TerminalFontCatalog
{
    private static readonly Lazy<IReadOnlyList<string>> SystemFontFamilies = new(LoadSystemFontFamilies);

    /// <summary>
    /// Gets installed system font family names.
    /// </summary>
    public static IReadOnlyList<string> GetSystemFontFamilies() => SystemFontFamilies.Value;

    private static IReadOnlyList<string> LoadSystemFontFamilies()
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

    private static IReadOnlyList<string> NormalizeFamilies(string[] families)
    {
        if (families.Length == 0)
        {
            return Array.Empty<string>();
        }

        Array.Sort(families, StringComparer.OrdinalIgnoreCase);
        List<string> normalizedFamilies = new(families.Length);
        string? previous = null;

        for (int i = 0; i < families.Length; i++)
        {
            string? family = families[i];
            if (string.IsNullOrWhiteSpace(family))
            {
                continue;
            }

            string normalized = family.Trim();
            if (previous is null || !string.Equals(previous, normalized, StringComparison.OrdinalIgnoreCase))
            {
                normalizedFamilies.Add(normalized);
                previous = normalized;
            }
        }

        return normalizedFamilies.Count == 0
            ? Array.Empty<string>()
            : normalizedFamilies.ToArray();
    }
}
