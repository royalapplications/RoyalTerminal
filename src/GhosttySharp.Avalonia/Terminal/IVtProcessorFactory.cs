// Licensed under the MIT License.
// GhosttySharp.Avalonia — Factory abstraction for VT processors.

using GhosttySharp.Avalonia.Rendering;

namespace GhosttySharp.Avalonia.Terminal;

/// <summary>
/// Factory for creating terminal VT processors.
/// </summary>
public interface IVtProcessorFactory
{
    /// <summary>
    /// Creates a VT processor for the provided screen.
    /// </summary>
    /// <param name="screen">Terminal screen model.</param>
    /// <param name="useNativeVtProcessor">
    /// Explicit processor preference:
    /// <c>true</c> = require native, <c>false</c> = force managed fallback, <c>null</c> = auto-detect.
    /// </param>
    /// <returns>A configured VT processor instance.</returns>
    IVtProcessor Create(TerminalScreen screen, bool? useNativeVtProcessor);
}
