// Licensed under the MIT License.
// RoyalTerminal.Avalonia - Text rendering diagnostics counters.

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>
/// Snapshot of text-render diagnostics collected by <see cref="SkiaTerminalRenderer"/>.
/// </summary>
public readonly record struct TextRenderDiagnostics(
    long ShapedRuns,
    long FallbackRuns,
    long FallbackFontHits,
    long GridClampedRuns);
