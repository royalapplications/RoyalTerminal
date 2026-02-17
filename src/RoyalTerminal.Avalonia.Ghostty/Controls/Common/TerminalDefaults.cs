// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.Ghostty.Controls - Shared terminal defaults.

using System.Runtime.InteropServices;

namespace RoyalTerminal.Avalonia.Controls;

internal static class TerminalDefaults
{
    public static readonly string DefaultMonoFont =
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Menlo" :
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "DejaVu Sans Mono" :
        "Consolas";
}
