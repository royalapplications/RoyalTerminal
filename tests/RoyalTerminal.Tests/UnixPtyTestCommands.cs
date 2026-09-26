// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Tests;

internal static class UnixPtyTestCommands
{
    // Split the marker so the tty's command echo cannot satisfy readiness or
    // recovery assertions before the shell has actually executed printf.
    internal static string PrintMarker(string marker)
    {
        int split = marker.Length / 2;
        string left = marker[..split].Replace("'", "'\\''", StringComparison.Ordinal);
        string right = marker[split..].Replace("'", "'\\''", StringComparison.Ordinal);
        return $"printf '%s%s\\n' '{left}' '{right}'\n";
    }
}
