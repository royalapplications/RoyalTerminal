// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>Cold discovery only; scalar hit/miss caching belongs to the resolver.</summary>
internal static class TerminalFontCandidates
{
    internal static IEnumerable<SKTypeface> Enumerate(SKFontManager manager, SKFontStyle style, string[]? languages, int codepoint,
        Action<SKTypeface>? releaseRejected = null)
    {
        releaseRejected ??= static face => face.Dispose();
        if (OperatingSystem.IsLinux() && FontconfigFontCandidates.Find(style, languages, codepoint) is { } files)
        {
            foreach (FontFileCandidate file in files)
                if (LoadFile(file.Path, file.Index) is { } face) yield return face;
        }
        if (OperatingSystem.IsMacOS() && CoreTextFontCandidates.Find(style, codepoint) is { } names)
        {
            foreach (NamedFontCandidate name in names)
            {
                SKTypeface? face = LoadNamed(manager, name, releaseRejected);
                if (face is not null) yield return face;
            }
        }
        if (OperatingSystem.IsWindows())
        {
            // Ghostty's Windows backend includes system and per-user font files,
            // even when they were not registered with DirectWrite. Also inspect
            // the manager below for application-registered fonts elsewhere.
            string? systemRoot = Environment.GetEnvironmentVariable("SYSTEMROOT");
            string? local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            foreach (string? directory in new[]
            {
                string.IsNullOrEmpty(systemRoot) ? null : Path.Combine(systemRoot, "Fonts"),
                string.IsNullOrEmpty(local) ? null : Path.Combine(local, "Microsoft", "Windows", "Fonts"),
            })
            {
                foreach (string path in FontFiles(directory))
                    for (int index = 0, count = CollectionCount(path); index < count; index++)
                        if (LoadFile(path, index) is { } face) yield return face;
            }
        }
        // Also cover manager-private registrations and unavailable native APIs.
        // This is reached only if every preceding candidate was rejected.
        foreach (string family in manager.FontFamilies)
        {
            using SKFontStyleSet styles = manager.GetFontStyles(family);
            // Try the requested weight/slant before the other advertised faces.
            // All variants still participate: cmap/color coverage can differ.
            SKTypeface? closest = styles.CreateTypeface(style);
            string? preferred = closest?.PostScriptName;
            if (string.IsNullOrEmpty(preferred)) preferred = null;
            if (closest is not null) yield return closest;
            for (int index = 0; index < styles.Count; index++)
            {
                SKTypeface? face = styles.CreateTypeface(index);
                if (face is null) continue;
                if (preferred is null || face.PostScriptName != preferred) yield return face;
                else releaseRejected(face);
            }
        }
    }

    private static SKTypeface? LoadNamed(SKFontManager manager, NamedFontCandidate name, Action<SKTypeface> releaseRejected)
    {
        using SKFontStyleSet styles = manager.GetFontStyles(name.Family);
        for (int index = 0; index < styles.Count; index++)
        {
            SKTypeface? face = styles.CreateTypeface(index);
            if (face is null) continue;
            if (string.Equals(face.PostScriptName, name.PostScriptName, StringComparison.Ordinal)) return face;
            releaseRejected(face);
        }
        if (name.Path is not null)
            for (int index = 0, count = CollectionCount(name.Path); index < count; index++)
            {
                SKTypeface? face = LoadFile(name.Path, index);
                if (face is null) continue;
                if (string.Equals(face.PostScriptName, name.PostScriptName, StringComparison.Ordinal)) return face;
                releaseRejected(face);
            }
        return null;
    }

    internal static SKTypeface? LoadFile(string path, int index)
    {
        try { return SKTypeface.FromFile(path, index); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (ArgumentException) { return null; }
    }

    private static IEnumerable<string> FontFiles(string? directory)
    {
        if (directory is null || !Directory.Exists(directory)) yield break;
        IEnumerator<string>? iterator;
        try { iterator = Directory.EnumerateFiles(directory).GetEnumerator(); }
        catch (IOException) { yield break; }
        catch (UnauthorizedAccessException) { yield break; }
        using (iterator)
        {
            while (true)
            {
                bool next;
                try { next = iterator.MoveNext(); }
                catch (IOException) { yield break; }
                catch (UnauthorizedAccessException) { yield break; }
                if (!next) yield break;
                string path = iterator.Current;
                string extension = Path.GetExtension(path);
                if (extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) || extension.Equals(".otf", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".ttc", StringComparison.OrdinalIgnoreCase) || extension.Equals(".otc", StringComparison.OrdinalIgnoreCase)) yield return path;
            }
        }
    }

    internal static int CollectionCount(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[12];
            if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) != header.Length) return 0;
            if (!header[..4].SequenceEqual("ttcf"u8)) return 1;
            uint count = BinaryPrimitives.ReadUInt32BigEndian(header[8..]);
            return count <= int.MaxValue && count <= (stream.Length - 12) / 4 ? (int)count : 0;
        }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }
}

internal readonly record struct FontFileCandidate(string Path, int Index);
internal readonly record struct NamedFontCandidate(string Family, string PostScriptName, uint Score, string? Path = null);
