// Licensed under the MIT License.
// RoyalTerminal.Avalonia — Default no-op logger for library consumers.

namespace RoyalTerminal.Avalonia.Diagnostics;

/// <summary>
/// No-op logger used as the default for controls.
/// </summary>
public sealed class NullGhosttyLogger : IGhosttyLogger
{
    /// <summary>Singleton no-op logger instance.</summary>
    public static NullGhosttyLogger Instance { get; } = new();

    private NullGhosttyLogger()
    {
    }

    /// <inheritdoc />
    public bool IsEnabled(GhosttyLogLevel level)
    {
        _ = level;
        return false;
    }

    /// <inheritdoc />
    public void Log(GhosttyLogLevel level, string message, Exception? exception = null)
    {
        _ = level;
        _ = message;
        _ = exception;
    }
}
