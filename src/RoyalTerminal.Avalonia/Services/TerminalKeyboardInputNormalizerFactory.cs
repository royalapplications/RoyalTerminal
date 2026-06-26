// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Platform keyboard input normalizer factory.

namespace RoyalTerminal.Avalonia.Services;

internal static class TerminalKeyboardInputNormalizerFactory
{
    public static ITerminalKeyboardInputNormalizer Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsTerminalKeyboardInputNormalizer();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOsTerminalKeyboardInputNormalizer();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxTerminalKeyboardInputNormalizer();
        }

        return new DefaultTerminalKeyboardInputNormalizer();
    }
}
