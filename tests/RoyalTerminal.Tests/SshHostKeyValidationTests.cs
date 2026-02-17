// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Ssh;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class SshHostKeyValidationTests
{
    [Fact]
    public void ExpectedFingerprintValidator_MatchesWithNormalizedSha256Formats()
    {
        ExpectedFingerprintSshHostKeyValidator validator = new("SHA256:abc123==");
        SshEndpointOptions endpoint = new("host", 22, "user");
        SshHostKeyInfo hostKey = new(
            HostKeyAlgorithm: "ssh-ed25519",
            FingerprintSha256: "abc123",
            FingerprintMd5: "00:11:22:33",
            KeyLengthBits: 256);

        bool trusted = validator.IsTrusted(endpoint, hostKey);

        Assert.True(trusted);
    }

    [Fact]
    public void ExpectedFingerprintValidator_RejectsMismatchedFingerprint()
    {
        ExpectedFingerprintSshHostKeyValidator validator = new("SHA256:expected");
        SshEndpointOptions endpoint = new("host", 22, "user");
        SshHostKeyInfo hostKey = new(
            HostKeyAlgorithm: "ssh-ed25519",
            FingerprintSha256: "other",
            FingerprintMd5: "00:11:22:33",
            KeyLengthBits: 256);

        bool trusted = validator.IsTrusted(endpoint, hostKey);

        Assert.False(trusted);
    }

    [Fact]
    public void RejectAllValidator_AlwaysReturnsFalse()
    {
        RejectAllSshHostKeyValidator validator = new();
        SshEndpointOptions endpoint = new("host", 22, "user");
        SshHostKeyInfo hostKey = new(
            HostKeyAlgorithm: "ssh-rsa",
            FingerprintSha256: "fp",
            FingerprintMd5: "00:11:22:33",
            KeyLengthBits: 2048);

        Assert.False(validator.IsTrusted(endpoint, hostKey));
    }

    [Fact]
    public void ExpectedFingerprintValidator_ThrowsForEmptyExpectedValue()
    {
        Assert.Throws<ArgumentException>(() => new ExpectedFingerprintSshHostKeyValidator(string.Empty));
    }
}
