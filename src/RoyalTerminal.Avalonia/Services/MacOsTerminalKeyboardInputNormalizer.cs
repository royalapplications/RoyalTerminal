// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - macOS keyboard input normalization.

namespace RoyalTerminal.Avalonia.Services;

internal sealed class MacOsTerminalKeyboardInputNormalizer : DefaultTerminalKeyboardInputNormalizer
{
    // macOS Option/dead-key composition is delivered through TextInput; no Ctrl+Alt aliasing is needed here.
}
