// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Linux keyboard input normalization.

namespace RoyalTerminal.Avalonia.Services;

internal sealed class LinuxTerminalKeyboardInputNormalizer : DefaultTerminalKeyboardInputNormalizer
{
    // X11/Wayland Level3/IME composition is expected to arrive as TextInput without Windows AltGr aliasing.
}
