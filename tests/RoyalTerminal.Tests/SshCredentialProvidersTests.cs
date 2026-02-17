// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;
using System.Text;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class SshCredentialProvidersTests
{
    [Fact]
    public async Task InMemoryStore_SaveThenLoad_ReturnsValue()
    {
        InMemorySshSecretStore store = new();

        await store.SaveSecretAsync("ssh/password", "secret");
        string? value = await store.LoadSecretAsync("ssh/password");

        Assert.Equal("secret", value);
    }

    [Fact]
    public async Task EnvironmentStore_SaveThenLoad_UsesPrefix()
    {
        string prefix = "RT_TEST_";
        EnvironmentVariableSshSecretStore store = new(prefix);

        await store.SaveSecretAsync("MY_SECRET", "value123");
        string? value = await store.LoadSecretAsync("MY_SECRET");

        Assert.Equal("value123", value);

        // Cleanup process-scoped variable created by this test.
        Environment.SetEnvironmentVariable(prefix + "MY_SECRET", null, EnvironmentVariableTarget.Process);
    }

    [Fact]
    public async Task JsonFileStore_SaveThenLoad_ReturnsValue()
    {
        string filePath = Path.Combine(Path.GetTempPath(), "royalterminal-tests", Guid.NewGuid() + ".json");

        try
        {
            JsonFileSshSecretStore store = new(filePath);

            await store.SaveSecretAsync("secret-id", "value");
            string? value = await store.LoadSecretAsync("secret-id");

            Assert.Equal("value", value);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public async Task JsonFileStore_WritesOwnerOnlyPermissionsOnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string filePath = Path.Combine(Path.GetTempPath(), "royalterminal-tests", Guid.NewGuid() + ".json");

        try
        {
            JsonFileSshSecretStore store = new(filePath);
            await store.SaveSecretAsync("secret-id", "value");

            UnixFileMode mode = File.GetUnixFileMode(filePath);
            UnixFileMode disallowed = mode & (
                UnixFileMode.GroupRead |
                UnixFileMode.GroupWrite |
                UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead |
                UnixFileMode.OtherWrite |
                UnixFileMode.OtherExecute);

            Assert.Equal((UnixFileMode)0, disallowed);
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                mode & (UnixFileMode.UserRead | UnixFileMode.UserWrite));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public async Task CompositeStore_Load_FallsBackToSecondStore()
    {
        InMemorySshSecretStore first = new();
        InMemorySshSecretStore second = new();
        CompositeSshSecretStore composite = new(new ISshSecretStore[] { first, second });

        await second.SaveSecretAsync("id", "fallback");

        string? value = await composite.LoadSecretAsync("id");

        Assert.Equal("fallback", value);
    }

    [Fact]
    public async Task CompositeStore_Load_ReturnsEmptySecret_WhenFirstStoreContainsEmptyValue()
    {
        InMemorySshSecretStore first = new();
        InMemorySshSecretStore second = new();
        CompositeSshSecretStore composite = new(new ISshSecretStore[] { first, second });

        await first.SaveSecretAsync("id", string.Empty);
        await second.SaveSecretAsync("id", "fallback");

        string? value = await composite.LoadSecretAsync("id");

        Assert.NotNull(value);
        Assert.Equal(string.Empty, value);
    }

    [Fact]
    public async Task SecretStoreCredentialProvider_ResolvesPasswordAndKeys()
    {
        InMemorySshSecretStore store = new(
            new Dictionary<string, string>
            {
                ["pwd"] = "pw",
                ["key1"] = "KEYDATA",
            });
        SecretStoreSshCredentialProvider provider = new(store);

        SshResolvedCredentials credentials = await provider.ResolveAsync(
            new SshCredentialRequest(
                new SshEndpointOptions("host", 22, "user"),
                new SshAuthenticationOptions(
                    UsePassword: true,
                    PasswordSecretId: "pwd",
                    PrivateKeySecretIds: new[] { "key1" },
                    UseAgent: true)));

        Assert.Equal("pw", credentials.Password);
        Assert.Single(credentials.PrivateKeyPemOrPath);
        Assert.Equal("KEYDATA", credentials.PrivateKeyPemOrPath[0]);
        Assert.True(credentials.UseAgent);
    }

    [Fact]
    public async Task SecretStoreCredentialProvider_Throws_WhenPasswordIdMissing()
    {
        SecretStoreSshCredentialProvider provider = new(new InMemorySshSecretStore());

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await provider.ResolveAsync(
                new SshCredentialRequest(
                    new SshEndpointOptions("host", 22, "user"),
                    new SshAuthenticationOptions(
                        UsePassword: true,
                        PasswordSecretId: null,
                        PrivateKeySecretIds: Array.Empty<string>(),
                        UseAgent: false))));

        Assert.Contains("PasswordSecretId", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SecretStoreCredentialProvider_Throws_WhenSecretMissing()
    {
        SecretStoreSshCredentialProvider provider = new(new InMemorySshSecretStore());

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await provider.ResolveAsync(
                new SshCredentialRequest(
                    new SshEndpointOptions("host", 22, "user"),
                    new SshAuthenticationOptions(
                        UsePassword: true,
                        PasswordSecretId: "missing",
                        PrivateKeySecretIds: Array.Empty<string>(),
                        UseAgent: false))));

        Assert.Contains("missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProtectedJsonFileStore_SaveThenLoad_RoundTripsWithNoOpProtector()
    {
        string filePath = Path.Combine(Path.GetTempPath(), "royalterminal-tests", Guid.NewGuid() + ".protected.json");

        try
        {
            ProtectedJsonFileSshSecretStore store = new(filePath, new NoOpSshSecretProtector());

            await store.SaveSecretAsync("secret-id", "value");
            string? value = await store.LoadSecretAsync("secret-id");

            Assert.Equal("value", value);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public async Task ProtectedJsonFileStore_SaveThenLoad_RoundTripsEmptySecret()
    {
        string filePath = Path.Combine(Path.GetTempPath(), "royalterminal-tests", Guid.NewGuid() + ".protected.json");

        try
        {
            ProtectedJsonFileSshSecretStore store = new(filePath, new NoOpSshSecretProtector());

            await store.SaveSecretAsync("secret-id", string.Empty);
            string? value = await store.LoadSecretAsync("secret-id");

            Assert.NotNull(value);
            Assert.Equal(string.Empty, value);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public async Task ProtectedJsonFileStore_WritesOwnerOnlyPermissionsOnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string filePath = Path.Combine(Path.GetTempPath(), "royalterminal-tests", Guid.NewGuid() + ".protected.json");

        try
        {
            ProtectedJsonFileSshSecretStore store = new(filePath, new NoOpSshSecretProtector());
            await store.SaveSecretAsync("secret-id", "value");

            UnixFileMode mode = File.GetUnixFileMode(filePath);
            UnixFileMode disallowed = mode & (
                UnixFileMode.GroupRead |
                UnixFileMode.GroupWrite |
                UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead |
                UnixFileMode.OtherWrite |
                UnixFileMode.OtherExecute);

            Assert.Equal((UnixFileMode)0, disallowed);
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                mode & (UnixFileMode.UserRead | UnixFileMode.UserWrite));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public async Task ProtectedJsonFileStore_PersistsEncodedPayload_NotPlainText()
    {
        string filePath = Path.Combine(Path.GetTempPath(), "royalterminal-tests", Guid.NewGuid() + ".protected.json");

        try
        {
            ProtectedJsonFileSshSecretStore store = new(filePath, new NoOpSshSecretProtector());
            const string secretValue = "ssh-super-secret-value";

            await store.SaveSecretAsync("secret-id", secretValue);
            string json = File.ReadAllText(filePath);

            Assert.DoesNotContain(secretValue, json, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public async Task ProtectedJsonFileStore_Load_ThrowsWhenProtectorDoesNotMatch()
    {
        string filePath = Path.Combine(Path.GetTempPath(), "royalterminal-tests", Guid.NewGuid() + ".protected.json");

        try
        {
            ProtectedJsonFileSshSecretStore saveStore = new(filePath, new PrefixProtector("alpha"));
            await saveStore.SaveSecretAsync("secret-id", "value");

            ProtectedJsonFileSshSecretStore loadStore = new(filePath, new PrefixProtector("beta"));
            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await loadStore.LoadSecretAsync("secret-id"));

            Assert.Contains("persisted with protector", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public async Task ProtectedJsonFileStore_Load_ThrowsWhenProtectorMetadataMissing()
    {
        string filePath = Path.Combine(Path.GetTempPath(), "royalterminal-tests", Guid.NewGuid() + ".protected.json");

        try
        {
            const string json = """
            {
              "secret-id": {
                "ValueBase64": "dmFsdWU="
              }
            }
            """;
            File.WriteAllText(filePath, json);

            ProtectedJsonFileSshSecretStore store = new(filePath, new NoOpSshSecretProtector());
            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await store.LoadSecretAsync("secret-id"));

            Assert.Contains("missing protector metadata", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public void DpapiProtector_ThrowsOnNonWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Throws<PlatformNotSupportedException>(() => new DpapiSshSecretProtector());
    }

    [Fact]
    public void DpapiProtector_RoundTripsOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        DpapiSshSecretProtector protector = new();
        byte[] protectedPayload = protector.Protect("top-secret"u8);
        byte[] plaintext = protector.Unprotect(protectedPayload);

        Assert.Equal("top-secret", Encoding.UTF8.GetString(plaintext));
    }

    private sealed class PrefixProtector : ISshSecretProtector
    {
        private readonly byte[] _prefix;

        public PrefixProtector(string id)
        {
            ProtectorId = id;
            _prefix = Encoding.UTF8.GetBytes(id + ":");
        }

        public string ProtectorId { get; }

        public byte[] Protect(ReadOnlySpan<byte> plaintext)
        {
            byte[] result = new byte[_prefix.Length + plaintext.Length];
            _prefix.CopyTo(result.AsSpan(0, _prefix.Length));
            plaintext.CopyTo(result.AsSpan(_prefix.Length));
            return result;
        }

        public byte[] Unprotect(ReadOnlySpan<byte> protectedPayload)
        {
            return protectedPayload.Slice(_prefix.Length).ToArray();
        }
    }
}
