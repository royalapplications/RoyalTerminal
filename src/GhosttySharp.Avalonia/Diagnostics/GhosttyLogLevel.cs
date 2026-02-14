// Licensed under the MIT License.
// GhosttySharp.Avalonia — Diagnostic log levels for library logging.

namespace GhosttySharp.Avalonia.Diagnostics;

/// <summary>
/// Log severity for GhosttySharp diagnostics.
/// </summary>
public enum GhosttyLogLevel
{
    /// <summary>Detailed trace diagnostics.</summary>
    Trace = 0,

    /// <summary>Debug diagnostics.</summary>
    Debug = 1,

    /// <summary>General informational messages.</summary>
    Information = 2,

    /// <summary>Recoverable warnings.</summary>
    Warning = 3,

    /// <summary>Error diagnostics.</summary>
    Error = 4,
}
