// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Security.Cryptography;
using System.Text;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Ssh;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class KnownHostsSshHostKeyValidatorTests
{
    [Fact]
    public void KnownHostsValidator_TrustsMatchingHostAndFingerprint()
    {
        (KnownHostsSshHostKeyValidator validator, SshHostKeyInfo hostKey, string filePath) =
            CreateFixture("example.com", "ssh-ed25519");
        try
        {
            bool trusted = validator.IsTrusted(
                new SshEndpointOptions("example.com", 22, "alice"),
                hostKey);

            Assert.True(trusted);
        }
        finally
        {
            DeleteFileAndDirectory(filePath);
        }
    }

    [Fact]
    public void KnownHostsValidator_UsesRawHostKeyWhenAvailable()
    {
        (KnownHostsSshHostKeyValidator validator, SshHostKeyInfo hostKey, string filePath, byte[] keyBytes) =
            CreateFixtureWithKeyBytes("example.com", "ssh-ed25519");
        try
        {
            SshHostKeyInfo hostKeyWithRawBytes = hostKey with
            {
                HostKeyBase64 = Convert.ToBase64String(keyBytes),
                FingerprintSha256 = "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            };

            bool trusted = validator.IsTrusted(
                new SshEndpointOptions("example.com", 22, "alice"),
                hostKeyWithRawBytes);

            Assert.True(trusted);
        }
        finally
        {
            DeleteFileAndDirectory(filePath);
        }
    }

    [Fact]
    public void KnownHostsValidator_RejectsRawHostKeyMismatchEvenWhenFingerprintMatches()
    {
        (KnownHostsSshHostKeyValidator validator, SshHostKeyInfo hostKey, string filePath) =
            CreateFixture("example.com", "ssh-ed25519");
        try
        {
            byte[] mismatchedKeyBytes = RandomNumberGenerator.GetBytes(64);
            SshHostKeyInfo mismatchedHostKey = hostKey with
            {
                HostKeyBase64 = Convert.ToBase64String(mismatchedKeyBytes),
            };

            bool trusted = validator.IsTrusted(
                new SshEndpointOptions("example.com", 22, "alice"),
                mismatchedHostKey);

            Assert.False(trusted);
        }
        finally
        {
            DeleteFileAndDirectory(filePath);
        }
    }

    [Fact]
    public void KnownHostsValidator_RejectsInvalidPresentedRawHostKeyEncoding()
    {
        (KnownHostsSshHostKeyValidator validator, SshHostKeyInfo hostKey, string filePath) =
            CreateFixture("example.com", "ssh-ed25519");
        try
        {
            SshHostKeyInfo malformedHostKey = hostKey with
            {
                HostKeyBase64 = "not-a-valid-base64-payload",
            };

            bool trusted = validator.IsTrusted(
                new SshEndpointOptions("example.com", 22, "alice"),
                malformedHostKey);

            Assert.False(trusted);
        }
        finally
        {
            DeleteFileAndDirectory(filePath);
        }
    }

    [Fact]
    public void KnownHostsValidator_TrustsBracketedHostForCustomPort()
    {
        (KnownHostsSshHostKeyValidator validator, SshHostKeyInfo hostKey, string filePath) =
            CreateFixture("[example.com]:2222", "ssh-ed25519");
        try
        {
            bool trusted = validator.IsTrusted(
                new SshEndpointOptions("example.com", 2222, "alice"),
                hostKey);

            Assert.True(trusted);
        }
        finally
        {
            DeleteFileAndDirectory(filePath);
        }
    }

    [Fact]
    public void KnownHostsValidator_TrustsHashedHostEntries()
    {
        byte[] keyBytes = RandomNumberGenerator.GetBytes(64);
        string keyBase64 = Convert.ToBase64String(keyBytes);
        string fingerprint = "SHA256:" + Convert.ToBase64String(SHA256.HashData(keyBytes));
        string hashedToken = CreateHashedHostToken("example.com");
        string line = $"{hashedToken} ssh-ed25519 {keyBase64}";
        string filePath = WriteKnownHostsFile(line);

        try
        {
            KnownHostsSshHostKeyValidator validator = new([filePath]);
            SshHostKeyInfo hostKey = new(
                HostKeyAlgorithm: "ssh-ed25519",
                FingerprintSha256: fingerprint,
                FingerprintMd5: string.Empty,
                KeyLengthBits: 256);

            bool trusted = validator.IsTrusted(
                new SshEndpointOptions("example.com", 22, "alice"),
                hostKey);

            Assert.True(trusted);
        }
        finally
        {
            DeleteFileAndDirectory(filePath);
        }
    }

    [Fact]
    public void KnownHostsValidator_RejectsRevokedMatchingEntry()
    {
        byte[] keyBytes = RandomNumberGenerator.GetBytes(64);
        string keyBase64 = Convert.ToBase64String(keyBytes);
        string fingerprint = "SHA256:" + Convert.ToBase64String(SHA256.HashData(keyBytes));
        string filePath = WriteKnownHostsFile($"@revoked example.com ssh-ed25519 {keyBase64}");

        try
        {
            KnownHostsSshHostKeyValidator validator = new([filePath]);
            SshHostKeyInfo hostKey = new(
                HostKeyAlgorithm: "ssh-ed25519",
                FingerprintSha256: fingerprint,
                FingerprintMd5: string.Empty,
                KeyLengthBits: 256,
                HostKeyBase64: keyBase64);

            bool trusted = validator.IsTrusted(
                new SshEndpointOptions("example.com", 22, "alice"),
                hostKey);

            Assert.False(trusted);
        }
        finally
        {
            DeleteFileAndDirectory(filePath);
        }
    }

    [Fact]
    public void KnownHostsValidator_RevokedEntryOverridesEarlierTrustedEntry()
    {
        byte[] keyBytes = RandomNumberGenerator.GetBytes(64);
        string keyBase64 = Convert.ToBase64String(keyBytes);
        string fingerprint = "SHA256:" + Convert.ToBase64String(SHA256.HashData(keyBytes));
        string filePath = WriteKnownHostsFile(
            $"example.com ssh-ed25519 {keyBase64}" + Environment.NewLine +
            $"@revoked example.com ssh-ed25519 {keyBase64}");

        try
        {
            KnownHostsSshHostKeyValidator validator = new([filePath]);
            SshHostKeyInfo hostKey = new(
                HostKeyAlgorithm: "ssh-ed25519",
                FingerprintSha256: fingerprint,
                FingerprintMd5: string.Empty,
                KeyLengthBits: 256,
                HostKeyBase64: keyBase64);

            bool trusted = validator.IsTrusted(
                new SshEndpointOptions("example.com", 22, "alice"),
                hostKey);

            Assert.False(trusted);
        }
        finally
        {
            DeleteFileAndDirectory(filePath);
        }
    }

    [Fact]
    public void KnownHostsValidator_IgnoresCertAuthorityMarkerEntries()
    {
        byte[] keyBytes = RandomNumberGenerator.GetBytes(64);
        string keyBase64 = Convert.ToBase64String(keyBytes);
        string fingerprint = "SHA256:" + Convert.ToBase64String(SHA256.HashData(keyBytes));
        string filePath = WriteKnownHostsFile($"@cert-authority example.com ssh-ed25519 {keyBase64}");

        try
        {
            KnownHostsSshHostKeyValidator validator = new([filePath]);
            SshHostKeyInfo hostKey = new(
                HostKeyAlgorithm: "ssh-ed25519",
                FingerprintSha256: fingerprint,
                FingerprintMd5: string.Empty,
                KeyLengthBits: 256,
                HostKeyBase64: keyBase64);

            bool trusted = validator.IsTrusted(
                new SshEndpointOptions("example.com", 22, "alice"),
                hostKey);

            Assert.False(trusted);
        }
        finally
        {
            DeleteFileAndDirectory(filePath);
        }
    }

    [Fact]
    public void KnownHostsValidator_RejectsUnknownHost()
    {
        (KnownHostsSshHostKeyValidator validator, SshHostKeyInfo hostKey, string filePath) =
            CreateFixture("example.com", "ssh-ed25519");
        try
        {
            bool trusted = validator.IsTrusted(
                new SshEndpointOptions("other-host", 22, "alice"),
                hostKey);

            Assert.False(trusted);
        }
        finally
        {
            DeleteFileAndDirectory(filePath);
        }
    }

    [Fact]
    public void KnownHostsValidator_RejectsFingerprintMismatch()
    {
        (KnownHostsSshHostKeyValidator validator, SshHostKeyInfo hostKey, string filePath) =
            CreateFixture("example.com", "ssh-ed25519");
        try
        {
            SshHostKeyInfo mismatchedHostKey = hostKey with
            {
                FingerprintSha256 = "SHA256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            };

            bool trusted = validator.IsTrusted(
                new SshEndpointOptions("example.com", 22, "alice"),
                mismatchedHostKey);

            Assert.False(trusted);
        }
        finally
        {
            DeleteFileAndDirectory(filePath);
        }
    }

    [Fact]
    public void KnownHostsValidator_RejectsWhenKnownHostsPathListIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => new KnownHostsSshHostKeyValidator(Array.Empty<string>()));
    }

    private static string CreateHashedHostToken(string hostCandidate)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(20);
        byte[] hostBytes = Encoding.UTF8.GetBytes(hostCandidate);
        using HMACSHA1 hmac = new(salt);
        byte[] hash = hmac.ComputeHash(hostBytes);
        return $"|1|{Convert.ToBase64String(salt)}|{Convert.ToBase64String(hash)}";
    }

    private static string WriteKnownHostsFile(string line)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "royalterminal-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string filePath = Path.Combine(directory, "known_hosts");
        File.WriteAllText(filePath, line + Environment.NewLine);
        return filePath;
    }

    private static void DeleteFileAndDirectory(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // Best effort cleanup.
        }

        try
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    private static (KnownHostsSshHostKeyValidator Validator, SshHostKeyInfo HostKey, string FilePath) CreateFixture(
        string hostPattern,
        string algorithm)
    {
        (KnownHostsSshHostKeyValidator validator, SshHostKeyInfo hostKey, string filePath, _) =
            CreateFixtureWithKeyBytes(hostPattern, algorithm);
        return (validator, hostKey, filePath);
    }

    private static (KnownHostsSshHostKeyValidator Validator, SshHostKeyInfo HostKey, string FilePath, byte[] KeyBytes)
        CreateFixtureWithKeyBytes(
        string hostPattern,
        string algorithm)
    {
        byte[] keyBytes = RandomNumberGenerator.GetBytes(64);
        string keyBase64 = Convert.ToBase64String(keyBytes);
        string fingerprint = "SHA256:" + Convert.ToBase64String(SHA256.HashData(keyBytes));
        string line = $"{hostPattern} {algorithm} {keyBase64}";
        string filePath = WriteKnownHostsFile(line);
        KnownHostsSshHostKeyValidator validator = new([filePath]);
        SshHostKeyInfo hostKey = new(
            HostKeyAlgorithm: algorithm,
            FingerprintSha256: fingerprint,
            FingerprintMd5: string.Empty,
            KeyLengthBits: 256);

        return (validator, hostKey, filePath, keyBytes);
    }
}
