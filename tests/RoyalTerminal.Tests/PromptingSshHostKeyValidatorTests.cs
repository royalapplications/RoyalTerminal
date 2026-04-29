// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Demo.Services;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Ssh;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class PromptingSshHostKeyValidatorTests
{
    [Fact]
    public void IsTrusted_WhenUserAccepts_AppendsKnownHostsEntry()
    {
        string filePath = CreateTemporaryKnownHostsPath();
        try
        {
            PromptingSshHostKeyValidator validator = new(
                new KnownHostsSshHostKeyValidator([filePath]),
                _ => true,
                filePath);
            SshEndpointOptions endpoint = new("example.com", 2222, "alice");
            SshHostKeyInfo hostKey = new(
                HostKeyAlgorithm: "ssh-ed25519",
                FingerprintSha256: "SHA256:abc",
                FingerprintMd5: "MD5:00:11",
                KeyLengthBits: 256,
                HostKeyBase64: Convert.ToBase64String([1, 2, 3, 4]));

            bool trusted = validator.IsTrusted(endpoint, hostKey);

            Assert.True(trusted);
            Assert.Contains("[example.com]:2222 ssh-ed25519 AQIDBA==", File.ReadAllText(filePath));
        }
        finally
        {
            DeleteFileAndDirectory(filePath);
        }
    }

    [Fact]
    public void IsTrusted_WhenUserDeclines_DoesNotAppendKnownHostsEntry()
    {
        string filePath = CreateTemporaryKnownHostsPath();
        try
        {
            PromptingSshHostKeyValidator validator = new(
                new KnownHostsSshHostKeyValidator([filePath]),
                _ => false,
                filePath);

            bool trusted = validator.IsTrusted(
                new SshEndpointOptions("example.com", 22, "alice"),
                new SshHostKeyInfo(
                    HostKeyAlgorithm: "ssh-ed25519",
                    FingerprintSha256: "SHA256:abc",
                    FingerprintMd5: "MD5:00:11",
                    KeyLengthBits: 256,
                    HostKeyBase64: Convert.ToBase64String([1, 2, 3, 4])));

            Assert.False(trusted);
            Assert.False(File.Exists(filePath));
        }
        finally
        {
            DeleteFileAndDirectory(filePath);
        }
    }

    [Fact]
    public void IsTrusted_WhenKnownHostsAlreadyTrustsKey_DoesNotPrompt()
    {
        string filePath = CreateTemporaryKnownHostsPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, "example.com ssh-ed25519 AQIDBA==" + Environment.NewLine);
            int promptCount = 0;
            PromptingSshHostKeyValidator validator = new(
                new KnownHostsSshHostKeyValidator([filePath]),
                _ =>
                {
                    promptCount++;
                    return false;
                },
                filePath);

            bool trusted = validator.IsTrusted(
                new SshEndpointOptions("example.com", 22, "alice"),
                new SshHostKeyInfo(
                    HostKeyAlgorithm: "ssh-ed25519",
                    FingerprintSha256: "SHA256:abc",
                    FingerprintMd5: "MD5:00:11",
                    KeyLengthBits: 256,
                    HostKeyBase64: Convert.ToBase64String([1, 2, 3, 4])));

            Assert.True(trusted);
            Assert.Equal(0, promptCount);
        }
        finally
        {
            DeleteFileAndDirectory(filePath);
        }
    }

    [Fact]
    public void IsTrusted_WhenKnownHostsHasChangedKey_DoesNotPromptOrAppendKnownHostsEntry()
    {
        string filePath = CreateTemporaryKnownHostsPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, "example.com ssh-ed25519 AQIDBA==" + Environment.NewLine);
            int promptCount = 0;
            PromptingSshHostKeyValidator validator = new(
                new KnownHostsSshHostKeyValidator([filePath]),
                _ =>
                {
                    promptCount++;
                    return true;
                },
                filePath);

            bool trusted = validator.IsTrusted(
                new SshEndpointOptions("example.com", 22, "alice"),
                new SshHostKeyInfo(
                    HostKeyAlgorithm: "ssh-ed25519",
                    FingerprintSha256: "SHA256:changed",
                    FingerprintMd5: "MD5:00:11",
                    KeyLengthBits: 256,
                    HostKeyBase64: Convert.ToBase64String([5, 6, 7, 8])));

            Assert.False(trusted);
            Assert.Equal(0, promptCount);
            Assert.DoesNotContain("BQYHCA==", File.ReadAllText(filePath), StringComparison.Ordinal);
        }
        finally
        {
            DeleteFileAndDirectory(filePath);
        }
    }

    [Fact]
    public void IsTrusted_WhenKnownHostsHasRevokedKey_DoesNotPromptOrAppendKnownHostsEntry()
    {
        string filePath = CreateTemporaryKnownHostsPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, "@revoked example.com ssh-ed25519 AQIDBA==" + Environment.NewLine);
            int promptCount = 0;
            PromptingSshHostKeyValidator validator = new(
                new KnownHostsSshHostKeyValidator([filePath]),
                _ =>
                {
                    promptCount++;
                    return true;
                },
                filePath);

            bool trusted = validator.IsTrusted(
                new SshEndpointOptions("example.com", 22, "alice"),
                new SshHostKeyInfo(
                    HostKeyAlgorithm: "ssh-ed25519",
                    FingerprintSha256: "SHA256:abc",
                    FingerprintMd5: "MD5:00:11",
                    KeyLengthBits: 256,
                    HostKeyBase64: Convert.ToBase64String([1, 2, 3, 4])));

            Assert.False(trusted);
            Assert.Equal(0, promptCount);
            Assert.Single(File.ReadAllLines(filePath));
        }
        finally
        {
            DeleteFileAndDirectory(filePath);
        }
    }

    [Fact]
    public void IsTrusted_WhenAcceptedKeyCannotBePersisted_TrustsCurrentConnectionOnly()
    {
        string filePath = CreateTemporaryKnownHostsPath();
        try
        {
            SshHostKeyTrustPromptRequest? capturedRequest = null;
            PromptingSshHostKeyValidator validator = new(
                new KnownHostsSshHostKeyValidator([filePath]),
                request =>
                {
                    capturedRequest = request;
                    return true;
                },
                filePath);

            bool trusted = validator.IsTrusted(
                new SshEndpointOptions("example.com", 22, "alice"),
                new SshHostKeyInfo(
                    HostKeyAlgorithm: string.Empty,
                    FingerprintSha256: "SHA256:abc",
                    FingerprintMd5: "MD5:00:11",
                    KeyLengthBits: 256,
                    HostKeyBase64: Convert.ToBase64String([1, 2, 3, 4])));

            Assert.True(trusted);
            Assert.NotNull(capturedRequest);
            Assert.False(capturedRequest.WillPersistTrust);
            Assert.False(File.Exists(filePath));
        }
        finally
        {
            DeleteFileAndDirectory(filePath);
        }
    }

    [Fact]
    public void IsTrusted_WhenPresentedRawHostKeyIsInvalid_DoesNotPrompt()
    {
        string filePath = CreateTemporaryKnownHostsPath();
        try
        {
            int promptCount = 0;
            PromptingSshHostKeyValidator validator = new(
                new KnownHostsSshHostKeyValidator([filePath]),
                _ =>
                {
                    promptCount++;
                    return true;
                },
                filePath);

            bool trusted = validator.IsTrusted(
                new SshEndpointOptions("example.com", 22, "alice"),
                new SshHostKeyInfo(
                    HostKeyAlgorithm: "ssh-ed25519",
                    FingerprintSha256: "SHA256:abc",
                    FingerprintMd5: "MD5:00:11",
                    KeyLengthBits: 256,
                    HostKeyBase64: "not-a-valid-base64-payload"));

            Assert.False(trusted);
            Assert.Equal(0, promptCount);
            Assert.False(File.Exists(filePath));
        }
        finally
        {
            DeleteFileAndDirectory(filePath);
        }
    }

    [Fact]
    public void IsTrusted_PassesFingerprintDetailsToPrompt()
    {
        string filePath = CreateTemporaryKnownHostsPath();
        try
        {
            SshHostKeyTrustPromptRequest? capturedRequest = null;
            PromptingSshHostKeyValidator validator = new(
                new KnownHostsSshHostKeyValidator([filePath]),
                request =>
                {
                    capturedRequest = request;
                    return false;
                },
                filePath);

            _ = validator.IsTrusted(
                new SshEndpointOptions("example.com", 22, "alice"),
                new SshHostKeyInfo(
                    HostKeyAlgorithm: "ssh-rsa",
                    FingerprintSha256: "SHA256:abc",
                    FingerprintMd5: "MD5:00:11",
                    KeyLengthBits: 2048,
                    HostKeyBase64: Convert.ToBase64String([1, 2, 3, 4])));

            Assert.NotNull(capturedRequest);
            Assert.Equal("example.com", capturedRequest.Host);
            Assert.Equal(22, capturedRequest.Port);
            Assert.Equal("alice", capturedRequest.Username);
            Assert.Equal("ssh-rsa", capturedRequest.HostKeyAlgorithm);
            Assert.Equal("SHA256:abc", capturedRequest.FingerprintSha256);
            Assert.Equal("MD5:00:11", capturedRequest.FingerprintMd5);
            Assert.Equal(2048, capturedRequest.KeyLengthBits);
            Assert.True(capturedRequest.WillPersistTrust);
            Assert.Equal(filePath, capturedRequest.KnownHostsFilePath);
        }
        finally
        {
            DeleteFileAndDirectory(filePath);
        }
    }

    private static string CreateTemporaryKnownHostsPath()
    {
        return Path.Combine(
            Path.GetTempPath(),
            "RoyalTerminalTests",
            Guid.NewGuid().ToString("N"),
            "known_hosts");
    }

    private static void DeleteFileAndDirectory(string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
