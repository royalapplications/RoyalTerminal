// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Text rendering diagnostics counters.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Snapshot of text-render diagnostics collected by <see cref="SkiaTerminalRenderer"/>.
/// </summary>
public readonly record struct TextRenderDiagnostics(
    long ShapedRuns,
    long FallbackRuns,
    long FallbackFontHits,
    long GridClampedRuns,
    long SpriteCells = 0,
    long BoxDrawingSpriteCells = 0,
    long BrailleSpriteCells = 0,
    long BlockSpriteCells = 0,
    long ScanLineSpriteCells = 0);
