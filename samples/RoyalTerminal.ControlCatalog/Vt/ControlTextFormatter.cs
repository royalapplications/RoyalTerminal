// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.ControlCatalog;

internal static class ControlTextFormatter
{
    public static string FormatControl(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "<none>";
        }

        return value
            .Replace("\x1b", "\\x1b", StringComparison.Ordinal)
            .Replace("\u0090", "\\x90", StringComparison.Ordinal)
            .Replace("\u009B", "\\x9b", StringComparison.Ordinal)
            .Replace("\u009C", "\\x9c", StringComparison.Ordinal)
            .Replace("\u009D", "\\x9d", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
