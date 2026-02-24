// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal.Theming;

/// <summary>
/// Controls how the 256-color palette is generated from base ANSI entries.
/// </summary>
public enum TerminalPaletteGenerationMode
{
    /// <summary>
    /// Uses canonical xterm-style generation for 16-255.
    /// </summary>
    Canonical = 0,

    /// <summary>
    /// Derives the 216-color cube and grayscale ramp from ANSI base colors
    /// using CIELAB interpolation.
    /// </summary>
    DerivedFromBase16Lab = 1,
}
