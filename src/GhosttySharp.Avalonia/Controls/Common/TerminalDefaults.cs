// Licensed under the MIT License.
// GhosttySharp.Avalonia.Controls - Shared terminal defaults.

using System.Runtime.InteropServices;

namespace GhosttySharp.Avalonia.Controls;

internal static class TerminalDefaults
{
    public static readonly string DefaultMonoFont =
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Menlo" :
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "DejaVu Sans Mono" :
        "Consolas";
}
