// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - SSH secret protection contracts.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Protects and unprotects secret payloads for persistence stores.
/// </summary>
public interface ISshSecretProtector
{
    /// <summary>
    /// Gets the protector identifier written to persisted payload metadata.
    /// </summary>
    string ProtectorId { get; }

    /// <summary>
    /// Protects plaintext UTF-8 bytes for persistence.
    /// </summary>
    byte[] Protect(ReadOnlySpan<byte> plaintext);

    /// <summary>
    /// Restores protected bytes to plaintext UTF-8 bytes.
    /// </summary>
    byte[] Unprotect(ReadOnlySpan<byte> protectedPayload);
}
