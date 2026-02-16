// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia — Default PTY factory.

namespace RoyalTerminal.Avalonia.Terminal;

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
