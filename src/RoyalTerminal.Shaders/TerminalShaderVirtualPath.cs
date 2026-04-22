// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader package model.

namespace RoyalTerminal.Shaders;

internal static class TerminalShaderVirtualPath
{
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Virtual shader path must be non-empty.", nameof(path));
        }

        string normalized = path.Trim().Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        normalized = normalized.TrimStart('/');
        return normalized.Length == 0
            ? throw new ArgumentException("Virtual shader path must be non-empty.", nameof(path))
            : normalized;
    }

    public static bool HasTraversal(string path)
    {
        string normalized = path.Replace('\\', '/');
        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length; i++)
        {
            if (string.Equals(segments[i], "..", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static string ResolveInclude(string includingFile, string includePath)
    {
        string normalizedInclude = includePath.Trim().Replace('\\', '/');
        if (normalizedInclude.StartsWith("/", StringComparison.Ordinal))
        {
            return Normalize(normalizedInclude);
        }

        string normalizedIncludingFile = Normalize(includingFile);
        int lastSlash = normalizedIncludingFile.LastIndexOf('/');
        if (lastSlash < 0)
        {
            return Normalize(normalizedInclude);
        }

        return Normalize(normalizedIncludingFile[..(lastSlash + 1)] + normalizedInclude);
    }
}
