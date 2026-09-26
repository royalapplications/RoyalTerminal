// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;

namespace RoyalTerminal.Terminal;

internal static partial class UnixPtyInputMode
{
    internal static unsafe bool TryGetPasswordInput(int descriptor, out bool passwordInput)
    {
        passwordInput = false;
        // Supported desktop ABIs: Darwin uses unsigned-long flags; Linux uses
        // unsigned-int flags. Reserve/alignment cover both complete termios structs.
        if (descriptor < 0 || (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux()) ||
            RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64)) return false;
        ulong* attributes = stackalloc ulong[16];
        if (GetAttributes(descriptor, attributes) != 0) return false;
        bool macOS = OperatingSystem.IsMacOS();
        ulong localFlags = macOS ? attributes[3] : ((uint*)attributes)[3];
        passwordInput = IsPasswordInput(localFlags, macOS);
        return true;
    }

    internal static bool IsPasswordInput(ulong localFlags, bool macOS)
        => (localFlags & (macOS ? 0x100UL : 0x2UL)) != 0 && (localFlags & 0x8UL) == 0;

    [LibraryImport("libc", EntryPoint = "tcgetattr", SetLastError = true)]
    private static unsafe partial int GetAttributes(int descriptor, ulong* attributes);
}
