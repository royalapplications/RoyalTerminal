// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Cross-platform SSH secret protection defaults.

using System.Security.Cryptography;

namespace RoyalTerminal.Terminal;

/// <summary>
/// AES-GCM secret protector backed by a persisted per-user key file.
/// </summary>
public sealed class AesGcmSshSecretProtector : ISshSecretProtector
{
    private const int KeySizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const byte PayloadFormatVersion = 1;
    private const UnixFileMode OwnerReadWriteMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private readonly string _keyFilePath;
    private readonly object _sync = new();
    private byte[]? _cachedKey;

    /// <summary>
    /// Initializes a new AES-GCM secret protector.
    /// </summary>
    public AesGcmSshSecretProtector(string keyFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyFilePath);
        _keyFilePath = keyFilePath;
    }

    /// <inheritdoc />
    public string ProtectorId => "aes-gcm-keyfile-v1";

    /// <inheritdoc />
    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        byte[] key = GetOrCreateKey();
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSizeBytes];

        using (AesGcm aes = new(key, TagSizeBytes))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        byte[] payload = new byte[1 + NonceSizeBytes + TagSizeBytes + ciphertext.Length];
        payload[0] = PayloadFormatVersion;
        nonce.CopyTo(payload.AsSpan(1, NonceSizeBytes));
        tag.CopyTo(payload.AsSpan(1 + NonceSizeBytes, TagSizeBytes));
        ciphertext.CopyTo(payload.AsSpan(1 + NonceSizeBytes + TagSizeBytes));
        return payload;
    }

    /// <inheritdoc />
    public byte[] Unprotect(ReadOnlySpan<byte> protectedPayload)
    {
        int minimumLength = 1 + NonceSizeBytes + TagSizeBytes;
        if (protectedPayload.Length < minimumLength)
        {
            throw new CryptographicException("Protected payload is too short.");
        }

        byte version = protectedPayload[0];
        if (version != PayloadFormatVersion)
        {
            throw new CryptographicException($"Unsupported payload format version '{version}'.");
        }

        ReadOnlySpan<byte> nonce = protectedPayload.Slice(1, NonceSizeBytes);
        ReadOnlySpan<byte> tag = protectedPayload.Slice(1 + NonceSizeBytes, TagSizeBytes);
        ReadOnlySpan<byte> ciphertext = protectedPayload.Slice(1 + NonceSizeBytes + TagSizeBytes);
        byte[] plaintext = new byte[ciphertext.Length];

        byte[] key = GetOrCreateKey();
        using (AesGcm aes = new(key, TagSizeBytes))
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }

        return plaintext;
    }

    private byte[] GetOrCreateKey()
    {
        lock (_sync)
        {
            if (_cachedKey is not null)
            {
                return _cachedKey;
            }

            if (File.Exists(_keyFilePath))
            {
                byte[] existing = File.ReadAllBytes(_keyFilePath);
                if (existing.Length != KeySizeBytes)
                {
                    throw new InvalidOperationException(
                        $"SSH secret protector key file '{_keyFilePath}' has invalid length {existing.Length}.");
                }

                RestrictKeyFilePermissionsIfSupported(_keyFilePath);
                _cachedKey = existing;
                return existing;
            }

            byte[] generated = RandomNumberGenerator.GetBytes(KeySizeBytes);
            SshSecretFileIo.WriteBytesAtomically(_keyFilePath, generated);
            RestrictKeyFilePermissionsIfSupported(_keyFilePath);
            _cachedKey = generated;
            return generated;
        }
    }

    private static void RestrictKeyFilePermissionsIfSupported(string keyFilePath)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(keyFilePath, OwnerReadWriteMode);
        }
        catch
        {
            // Best effort only (unsupported FS/platform combinations).
        }
    }
}

/// <summary>
/// Factory helpers for secure-by-default SSH secret persistence.
/// </summary>
public static class SshSecretProtectionFactory
{
    /// <summary>
    /// Creates the default secret protector for the current platform.
    /// </summary>
    public static ISshSecretProtector CreateDefaultProtector(
        DpapiSshSecretScope windowsScope = DpapiSshSecretScope.CurrentUser,
        string? nonWindowsKeyFilePath = null)
    {
        if (OperatingSystem.IsWindows())
        {
            return new DpapiSshSecretProtector(windowsScope);
        }

        string keyFilePath = string.IsNullOrWhiteSpace(nonWindowsKeyFilePath)
            ? GetDefaultNonWindowsKeyFilePath()
            : nonWindowsKeyFilePath;
        return new AesGcmSshSecretProtector(keyFilePath);
    }

    /// <summary>
    /// Creates the default protected JSON secret store for the current platform.
    /// </summary>
    public static ISshSecretStore CreateDefaultSecretStore(
        string? secretsFilePath = null,
        DpapiSshSecretScope windowsScope = DpapiSshSecretScope.CurrentUser,
        string? nonWindowsKeyFilePath = null)
    {
        string filePath = string.IsNullOrWhiteSpace(secretsFilePath)
            ? GetDefaultSecretStoreFilePath()
            : secretsFilePath;
        ISshSecretProtector protector = CreateDefaultProtector(windowsScope, nonWindowsKeyFilePath);
        return new ProtectedJsonFileSshSecretStore(filePath, protector);
    }

    /// <summary>
    /// Gets the default protected JSON secret-store file path.
    /// </summary>
    public static string GetDefaultSecretStoreFilePath()
    {
        return Path.Combine(GetDefaultStorageDirectory(), "ssh-secrets.json");
    }

    /// <summary>
    /// Gets the default non-Windows key-file path used by <see cref="AesGcmSshSecretProtector"/>.
    /// </summary>
    public static string GetDefaultNonWindowsKeyFilePath()
    {
        return Path.Combine(GetDefaultStorageDirectory(), "ssh-secrets.key");
    }

    private static string GetDefaultStorageDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.Combine(localAppData, "RoyalTerminal");
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return Path.Combine(userProfile, ".royalterminal");
        }

        return Path.Combine(Directory.GetCurrentDirectory(), ".royalterminal");
    }
}
