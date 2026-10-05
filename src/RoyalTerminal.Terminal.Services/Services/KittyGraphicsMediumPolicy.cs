// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal.Services;

/// <summary>Host-granted capabilities for Kitty image payloads outside the VT byte stream.</summary>
public sealed record KittyGraphicsMediumPolicy
{
    /// <summary>Gets whether regular file transmission is enabled.</summary>
    public bool FileEnabled { get; init; }

    /// <summary>
    /// Gets the host temporary directory, enabling temporary-file transmission when non-null.
    /// Files must be inside this directory, /tmp, or /dev/shm and contain the protocol marker.
    /// </summary>
    public string? TemporaryDirectory { get; init; }

    /// <summary>Gets whether POSIX shared-memory transmission is enabled (macOS/Linux only).</summary>
    public bool SharedMemoryEnabled { get; init; }
}
