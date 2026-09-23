// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics.CodeAnalysis;

namespace RoyalTerminal.Terminal;

/// <summary>Host-backed Kitty image transmission media.</summary>
public enum KittyGraphicsMedium
{
    /// <summary>A regular file that remains owned by the sender.</summary>
    File,
    /// <summary>A protocol-owned temporary file, removed after an accepted read attempt.</summary>
    TemporaryFile,
    /// <summary>A POSIX shared-memory object, unlinked after it is opened.</summary>
    SharedMemory,
}

/// <summary>Describes a bounded host-backed image read without coupling the parser to IO.</summary>
/// <param name="Medium">The requested transport medium.</param>
/// <param name="Path">The decoded UTF-8 path or POSIX shared-memory name.</param>
/// <param name="Offset">Offset of the first encoded image byte.</param>
/// <param name="Size">Exact encoded byte count; zero means the remaining payload.</param>
/// <param name="ExpectedBytes">
/// Validated uncompressed raw-image byte count, or null for PNG/compressed data.
/// Used only for shared memory with Size zero, where the object may include page padding.
/// </param>
public readonly record struct KittyGraphicsMediumRequest(
    KittyGraphicsMedium Medium,
    ReadOnlyMemory<byte> Path,
    uint Offset,
    uint Size,
    int? ExpectedBytes);

/// <summary>Optional host capability for bounded Kitty file and shared-memory image loading.</summary>
public interface IKittyGraphicsMediumReader
{
    /// <summary>
    /// Reads an owned image buffer within <paramref name="maxBytes"/>, or returns a Kitty
    /// protocol error. Implementations enforce host policy and validate the opened object
    /// before reading or taking temporary-file/shared-memory cleanup ownership.
    /// </summary>
    bool TryRead(
        KittyGraphicsMediumRequest request,
        int maxBytes,
        [NotNullWhen(true)] out byte[]? data,
        out string? error);
}
