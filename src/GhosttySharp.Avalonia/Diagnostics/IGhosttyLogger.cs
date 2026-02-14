// Licensed under the MIT License.
// GhosttySharp.Avalonia — Logging abstraction for library diagnostics.

namespace GhosttySharp.Avalonia.Diagnostics;

/// <summary>
/// Abstraction used by GhosttySharp.Avalonia controls for diagnostics.
/// </summary>
public interface IGhosttyLogger
{
    /// <summary>
    /// Returns <c>true</c> when the requested level should be emitted.
    /// </summary>
    bool IsEnabled(GhosttyLogLevel level);

    /// <summary>
    /// Logs a diagnostic message.
    /// </summary>
    void Log(GhosttyLogLevel level, string message, Exception? exception = null);
}
