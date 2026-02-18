// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal.Transport.Ssh.Abstractions - SSH host-key validation contracts.

namespace RoyalTerminal.Terminal.Transport.Ssh;

/// <summary>
/// SSH host key metadata.
/// </summary>
public readonly record struct SshHostKeyInfo(
    string HostKeyAlgorithm,
    string FingerprintSha256,
    string FingerprintMd5,
    int KeyLengthBits,
    string? HostKeyBase64 = null);

/// <summary>
/// Validates SSH host keys for endpoint connections.
/// </summary>
public interface ISshHostKeyValidator
{
    /// <summary>
    /// Returns <see langword="true"/> when the supplied host key is trusted.
    /// </summary>
    bool IsTrusted(SshEndpointOptions endpoint, SshHostKeyInfo hostKey);
}

/// <summary>
/// Strict host-key validator that rejects all host keys.
/// </summary>
public sealed class RejectAllSshHostKeyValidator : ISshHostKeyValidator
{
    /// <inheritdoc />
    public bool IsTrusted(SshEndpointOptions endpoint, SshHostKeyInfo hostKey)
    {
        _ = endpoint;
        _ = hostKey;
        return false;
    }
}

/// <summary>
/// Host-key validator that trusts one expected SHA-256 fingerprint.
/// </summary>
public sealed class ExpectedFingerprintSshHostKeyValidator : ISshHostKeyValidator
{
    private readonly string _expectedSha256;

    /// <summary>
    /// Initializes a validator for one expected SHA-256 fingerprint.
    /// </summary>
    public ExpectedFingerprintSshHostKeyValidator(string expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            throw new ArgumentException("Expected fingerprint must not be empty.", nameof(expectedSha256));
        }

        _expectedSha256 = Normalize(expectedSha256);
    }

    /// <inheritdoc />
    public bool IsTrusted(SshEndpointOptions endpoint, SshHostKeyInfo hostKey)
    {
        _ = endpoint;
        return string.Equals(_expectedSha256, Normalize(hostKey.FingerprintSha256), StringComparison.Ordinal);
    }

    private static string Normalize(string value)
    {
        string normalized = value.Trim();
        if (normalized.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring("SHA256:".Length);
        }

        return normalized.TrimEnd('=').Trim();
    }
}
