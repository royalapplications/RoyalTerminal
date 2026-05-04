// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Terminal text render pipeline selection.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Selects the text preparation and drawing pipeline used by <see cref="SkiaTerminalRenderer"/>.
/// </summary>
public enum TerminalTextRenderPipeline
{
    /// <summary>
    /// Uses RoyalTerminal's HarfBuzz shaping pipeline.
    /// </summary>
    HarfBuzz = 0,

    /// <summary>
    /// Uses the optional Pretext text-preparation pipeline when available.
    /// </summary>
    Pretext = 1,
}
