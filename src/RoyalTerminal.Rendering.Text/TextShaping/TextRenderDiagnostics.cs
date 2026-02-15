// Licensed under the MIT License.
// GhosttySharp.Avalonia - Text rendering diagnostics counters.

namespace GhosttySharp.Avalonia.Rendering;

/// <summary>
/// Snapshot of text-render diagnostics collected by <see cref="SkiaTerminalRenderer"/>.
/// </summary>
public readonly record struct TextRenderDiagnostics(
    long ShapedRuns,
    long FallbackRuns,
    long FallbackFontHits,
    long GridClampedRuns);
