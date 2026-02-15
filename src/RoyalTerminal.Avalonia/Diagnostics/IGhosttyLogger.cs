// Licensed under the MIT License.
// RoyalTerminal.Avalonia — Logging abstraction for library diagnostics.

namespace RoyalTerminal.Avalonia.Diagnostics;

/// <summary>
/// Abstraction used by RoyalTerminal.Avalonia controls for diagnostics.
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
