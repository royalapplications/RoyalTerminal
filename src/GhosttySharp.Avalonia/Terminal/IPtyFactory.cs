// Licensed under the MIT License.
// GhosttySharp.Avalonia — Factory abstraction for PTY implementations.

namespace GhosttySharp.Avalonia.Terminal;

/// <summary>
/// Factory for creating platform-specific PTY implementations.
/// </summary>
public interface IPtyFactory
{
    /// <summary>
    /// Creates a PTY instance for the current platform.
    /// </summary>
    /// <returns>A PTY implementation instance.</returns>
    IPty Create();
}
