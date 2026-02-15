// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia — Convenience extensions for IGhosttyLogger.

namespace RoyalTerminal.Avalonia.Diagnostics;

/// <summary>
/// Extension helpers for <see cref="IGhosttyLogger"/>.
/// </summary>
public static class GhosttyLoggerExtensions
{
    /// <summary>
    /// Writes a debug message.
    /// </summary>
    public static void Debug(this IGhosttyLogger logger, string message)
    {
        if (logger.IsEnabled(GhosttyLogLevel.Debug))
        {
            logger.Log(GhosttyLogLevel.Debug, message);
        }
    }

    /// <summary>
    /// Writes an error message with an optional exception.
    /// </summary>
    public static void Error(this IGhosttyLogger logger, string message, Exception? exception = null)
    {
        if (logger.IsEnabled(GhosttyLogLevel.Error))
        {
            logger.Log(GhosttyLogLevel.Error, message, exception);
        }
    }
}
