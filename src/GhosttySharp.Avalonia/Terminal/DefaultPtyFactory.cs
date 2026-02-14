// Licensed under the MIT License.
// GhosttySharp.Avalonia — Default PTY factory.

namespace GhosttySharp.Avalonia.Terminal;

/// <summary>
/// Default factory that creates OS-specific PTY implementations.
/// </summary>
public sealed class DefaultPtyFactory : IPtyFactory
{
    /// <inheritdoc />
    public IPty Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsPty();
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return new UnixPty();
        }

        throw new PlatformNotSupportedException("No PTY implementation available for this platform.");
    }
}
