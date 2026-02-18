// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - SSH credential provider and secret store implementations.

using System.Text.Json;
using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text;

namespace RoyalTerminal.Terminal;

/// <summary>
/// In-memory SSH secret store for tests and short-lived runtime sessions.
/// </summary>
public sealed class InMemorySshSecretStore : ISshSecretStore
{
    private readonly Dictionary<string, string> _secrets;

    /// <summary>
    /// Initializes a new in-memory secret store.
    /// </summary>
    public InMemorySshSecretStore(IReadOnlyDictionary<string, string>? initialSecrets = null)
    {
        _secrets = initialSecrets is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(initialSecrets, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public ValueTask<string?> LoadSecretAsync(string secretId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(secretId);

        return ValueTask.FromResult(_secrets.TryGetValue(secretId, out string? value) ? value : null);
    }

    /// <inheritdoc />
    public ValueTask SaveSecretAsync(string secretId, string secretValue, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(secretId);
        ArgumentNullException.ThrowIfNull(secretValue);

        _secrets[secretId] = secretValue;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Environment-variable-backed secret store.
/// </summary>
public sealed class EnvironmentVariableSshSecretStore : ISshSecretStore
{
    /// <summary>
    /// Initializes a new environment-variable store.
    /// </summary>
    /// <param name="variablePrefix">
    /// Optional prefix prepended to every secret id when reading/writing variables.
    /// </param>
    public EnvironmentVariableSshSecretStore(string? variablePrefix = null)
    {
        VariablePrefix = variablePrefix ?? string.Empty;
    }

    /// <summary>
    /// Gets the variable prefix applied to secret ids.
    /// </summary>
    public string VariablePrefix { get; }

    /// <inheritdoc />
    public ValueTask<string?> LoadSecretAsync(string secretId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(secretId);

        return ValueTask.FromResult(Environment.GetEnvironmentVariable(GetVariableName(secretId)));
    }

    /// <inheritdoc />
    public ValueTask SaveSecretAsync(string secretId, string secretValue, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(secretId);
        ArgumentNullException.ThrowIfNull(secretValue);

        // Process scope only: callers can manage persistence policy externally.
        Environment.SetEnvironmentVariable(GetVariableName(secretId), secretValue, EnvironmentVariableTarget.Process);
        return ValueTask.CompletedTask;
    }

    private string GetVariableName(string secretId)
    {
        return string.Concat(VariablePrefix, secretId);
    }
}

/// <summary>
/// JSON-file-backed secret store for local configuration scenarios.
/// </summary>
public sealed class JsonFileSshSecretStore : ISshSecretStore
{
    private readonly string _filePath;
    private readonly object _sync = new();

    /// <summary>
    /// Initializes a file-backed secret store.
    /// </summary>
    public JsonFileSshSecretStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    /// <inheritdoc />
    public ValueTask<string?> LoadSecretAsync(string secretId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(secretId);

        lock (_sync)
        {
            Dictionary<string, string> payload = ReadAllSecrets();
            return ValueTask.FromResult(payload.TryGetValue(secretId, out string? value) ? value : null);
        }
    }

    /// <inheritdoc />
    public ValueTask SaveSecretAsync(string secretId, string secretValue, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(secretId);
        ArgumentNullException.ThrowIfNull(secretValue);

        lock (_sync)
        {
            Dictionary<string, string> payload = ReadAllSecrets();
            payload[secretId] = secretValue;
            WriteAllSecrets(payload);
        }

        return ValueTask.CompletedTask;
    }

    private Dictionary<string, string> ReadAllSecrets()
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        string json = File.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        Dictionary<string, string>? data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        return data is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(data, StringComparer.Ordinal);
    }

    private void WriteAllSecrets(Dictionary<string, string> payload)
    {
        string json = JsonSerializer.Serialize(payload);
        SshSecretFileIo.WriteJsonAtomically(_filePath, json);
    }
}

/// <summary>
/// Secret protector that leaves payloads unchanged.
/// Useful for tests and explicit plaintext-compatible persistence.
/// </summary>
public sealed class NoOpSshSecretProtector : ISshSecretProtector
{
    /// <inheritdoc />
    public string ProtectorId => "none";

    /// <inheritdoc />
    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        return plaintext.ToArray();
    }

    /// <inheritdoc />
    public byte[] Unprotect(ReadOnlySpan<byte> protectedPayload)
    {
        return protectedPayload.ToArray();
    }
}

/// <summary>
/// Scope selection for DPAPI-based secret protection.
/// </summary>
public enum DpapiSshSecretScope
{
    /// <summary>Protect secrets per current OS user account.</summary>
    CurrentUser,

    /// <summary>Protect secrets for all local machine users.</summary>
    LocalMachine,
}

/// <summary>
/// Windows DPAPI-based secret protector.
/// </summary>
public sealed class DpapiSshSecretProtector : ISshSecretProtector
{
    private readonly byte[]? _optionalEntropy;

    /// <summary>
    /// Initializes a DPAPI secret protector.
    /// </summary>
    public DpapiSshSecretProtector(
        DpapiSshSecretScope scope = DpapiSshSecretScope.CurrentUser,
        byte[]? optionalEntropy = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI secret protection is supported only on Windows.");
        }

        Scope = scope;
        _optionalEntropy = optionalEntropy?.ToArray();
    }

    /// <summary>
    /// Gets the DPAPI scope used for protection.
    /// </summary>
    public DpapiSshSecretScope Scope { get; }

    /// <inheritdoc />
    public string ProtectorId => Scope == DpapiSshSecretScope.LocalMachine
        ? "dpapi-local-machine"
        : "dpapi-current-user";

    /// <inheritdoc />
    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI secret protection is supported only on Windows.");
        }

        return ProtectWindows(plaintext);
    }

    /// <inheritdoc />
    public byte[] Unprotect(ReadOnlySpan<byte> protectedPayload)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI secret protection is supported only on Windows.");
        }

        return UnprotectWindows(protectedPayload);
    }

    [SupportedOSPlatform("windows")]
    private byte[] ProtectWindows(ReadOnlySpan<byte> plaintext)
    {
        return ProtectedData.Protect(plaintext.ToArray(), _optionalEntropy, GetPlatformScope());
    }

    [SupportedOSPlatform("windows")]
    private byte[] UnprotectWindows(ReadOnlySpan<byte> protectedPayload)
    {
        return ProtectedData.Unprotect(protectedPayload.ToArray(), _optionalEntropy, GetPlatformScope());
    }

    [SupportedOSPlatform("windows")]
    private DataProtectionScope GetPlatformScope()
    {
        return Scope == DpapiSshSecretScope.LocalMachine
            ? DataProtectionScope.LocalMachine
            : DataProtectionScope.CurrentUser;
    }
}

/// <summary>
/// JSON-file-backed secret store with pluggable payload protection.
/// </summary>
public sealed class ProtectedJsonFileSshSecretStore : ISshSecretStore
{
    private readonly string _filePath;
    private readonly ISshSecretProtector _secretProtector;
    private readonly object _sync = new();

    /// <summary>
    /// Initializes a protected file-backed secret store.
    /// </summary>
    public ProtectedJsonFileSshSecretStore(
        string filePath,
        ISshSecretProtector secretProtector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        _secretProtector = secretProtector ?? throw new ArgumentNullException(nameof(secretProtector));
    }

    /// <inheritdoc />
    public ValueTask<string?> LoadSecretAsync(string secretId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(secretId);

        lock (_sync)
        {
            Dictionary<string, ProtectedSecretRecord> payload = ReadAllSecrets();
            if (!payload.TryGetValue(secretId, out ProtectedSecretRecord? record) ||
                record.ValueBase64 is null)
            {
                return ValueTask.FromResult<string?>(null);
            }

            if (string.IsNullOrWhiteSpace(record.ProtectorId))
            {
                throw new InvalidOperationException($"Secret '{secretId}' is missing protector metadata.");
            }

            if (!string.Equals(record.ProtectorId, _secretProtector.ProtectorId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Secret '{secretId}' was persisted with protector '{record.ProtectorId}' but current store uses '{_secretProtector.ProtectorId}'.");
            }

            byte[] protectedPayload;
            try
            {
                protectedPayload = Convert.FromBase64String(record.ValueBase64);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    $"Secret '{secretId}' has invalid base64 payload.", ex);
            }

            byte[] plaintext = _secretProtector.Unprotect(protectedPayload);
            return ValueTask.FromResult<string?>(Encoding.UTF8.GetString(plaintext));
        }
    }

    /// <inheritdoc />
    public ValueTask SaveSecretAsync(string secretId, string secretValue, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(secretId);
        ArgumentNullException.ThrowIfNull(secretValue);

        lock (_sync)
        {
            Dictionary<string, ProtectedSecretRecord> payload = ReadAllSecrets();
            byte[] plaintext = Encoding.UTF8.GetBytes(secretValue);
            byte[] protectedPayload = _secretProtector.Protect(plaintext);

            payload[secretId] = new ProtectedSecretRecord
            {
                ProtectorId = _secretProtector.ProtectorId,
                ValueBase64 = Convert.ToBase64String(protectedPayload),
            };

            WriteAllSecrets(payload);
        }

        return ValueTask.CompletedTask;
    }

    private Dictionary<string, ProtectedSecretRecord> ReadAllSecrets()
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, ProtectedSecretRecord>(StringComparer.Ordinal);
        }

        string json = File.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, ProtectedSecretRecord>(StringComparer.Ordinal);
        }

        Dictionary<string, ProtectedSecretRecord>? data =
            JsonSerializer.Deserialize<Dictionary<string, ProtectedSecretRecord>>(json);
        return data is null
            ? new Dictionary<string, ProtectedSecretRecord>(StringComparer.Ordinal)
            : new Dictionary<string, ProtectedSecretRecord>(data, StringComparer.Ordinal);
    }

    private void WriteAllSecrets(Dictionary<string, ProtectedSecretRecord> payload)
    {
        string json = JsonSerializer.Serialize(payload);
        SshSecretFileIo.WriteJsonAtomically(_filePath, json);
    }

    private sealed class ProtectedSecretRecord
    {
        public string ProtectorId { get; set; } = string.Empty;

        public string ValueBase64 { get; set; } = string.Empty;
    }
}

internal static class SshSecretFileIo
{
    private static readonly UTF8Encoding s_utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private const UnixFileMode OwnerReadWriteMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const UnixFileMode OwnerDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    public static void WriteJsonAtomically(string filePath, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(json);

        string? directory = Path.GetDirectoryName(filePath);
        string baseDirectory = string.IsNullOrWhiteSpace(directory)
            ? Directory.GetCurrentDirectory()
            : directory;
        CreateSecureDirectory(baseDirectory);

        string tempPath = Path.Combine(baseDirectory, $"{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (FileStream stream = OpenSecureCreateStream(tempPath))
            {
                using StreamWriter writer = new(stream, s_utf8NoBom);
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            RestrictFilePermissionsIfSupported(tempPath);
            File.Move(tempPath, filePath, overwrite: true);
            RestrictFilePermissionsIfSupported(filePath);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }

    public static void WriteBytesAtomically(string filePath, ReadOnlySpan<byte> payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string? directory = Path.GetDirectoryName(filePath);
        string baseDirectory = string.IsNullOrWhiteSpace(directory)
            ? Directory.GetCurrentDirectory()
            : directory;
        CreateSecureDirectory(baseDirectory);

        string tempPath = Path.Combine(baseDirectory, $"{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (FileStream stream = OpenSecureCreateStream(tempPath))
            {
                stream.Write(payload);
                stream.Flush(flushToDisk: true);
            }

            RestrictFilePermissionsIfSupported(tempPath);
            File.Move(tempPath, filePath, overwrite: true);
            RestrictFilePermissionsIfSupported(filePath);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }

    private static FileStream OpenSecureCreateStream(string path)
    {
        FileStreamOptions options = new()
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.WriteThrough,
        };

        if (!OperatingSystem.IsWindows())
        {
            // Ensure least-privilege mode at file creation time on Unix-like systems.
            options.UnixCreateMode = OwnerReadWriteMode;
        }

        return new FileStream(path, options);
    }

    private static void RestrictFilePermissionsIfSupported(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, OwnerReadWriteMode);
        }
        catch
        {
            // Best effort only (unsupported FS/platform combinations).
        }
    }

    private static void CreateSecureDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            return;
        }

        bool alreadyExists = Directory.Exists(path);
        DirectoryInfo directory = Directory.CreateDirectory(path, OwnerDirectoryMode);
        if (alreadyExists)
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(directory.FullName, OwnerDirectoryMode);
        }
        catch
        {
            // Best effort only (unsupported FS/platform combinations).
        }
    }
}

/// <summary>
/// Secret store chain that resolves by first available store and saves to the first store.
/// </summary>
public sealed class CompositeSshSecretStore : ISshSecretStore
{
    private readonly IReadOnlyList<ISshSecretStore> _stores;

    /// <summary>
    /// Initializes a composite secret store.
    /// </summary>
    public CompositeSshSecretStore(IReadOnlyList<ISshSecretStore> stores)
    {
        ArgumentNullException.ThrowIfNull(stores);
        if (stores.Count == 0)
        {
            throw new ArgumentException("At least one SSH secret store must be configured.", nameof(stores));
        }

        _stores = stores;
    }

    /// <inheritdoc />
    public async ValueTask<string?> LoadSecretAsync(string secretId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(secretId);

        for (int i = 0; i < _stores.Count; i++)
        {
            string? value = await _stores[i].LoadSecretAsync(secretId, cancellationToken).ConfigureAwait(false);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public ValueTask SaveSecretAsync(string secretId, string secretValue, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _stores[0].SaveSecretAsync(secretId, secretValue, cancellationToken);
    }
}

/// <summary>
/// Resolves SSH credentials from a secret store and validates required auth payloads.
/// </summary>
public sealed class SecretStoreSshCredentialProvider : ISshCredentialProvider
{
    private readonly ISshSecretStore _secretStore;

    /// <summary>
    /// Initializes a secret-store-backed SSH credential provider.
    /// </summary>
    public SecretStoreSshCredentialProvider(ISshSecretStore secretStore)
    {
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
    }

    /// <inheritdoc />
    public async ValueTask<SshResolvedCredentials> ResolveAsync(
        SshCredentialRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        string? password = null;
        if (request.Authentication.UsePassword)
        {
            if (string.IsNullOrWhiteSpace(request.Authentication.PasswordSecretId))
            {
                throw new InvalidOperationException(
                    "Password authentication was requested but no PasswordSecretId was provided.");
            }

            password = await LoadRequiredSecretAsync(
                request.Authentication.PasswordSecretId,
                "password",
                cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<string> privateKeys = await ResolvePrivateKeysAsync(
                request.Authentication.PrivateKeySecretIds,
                cancellationToken)
            .ConfigureAwait(false);

        return new SshResolvedCredentials(password, privateKeys, request.Authentication.UseAgent);
    }

    private async ValueTask<IReadOnlyList<string>> ResolvePrivateKeysAsync(
        IReadOnlyList<string> secretIds,
        CancellationToken cancellationToken)
    {
        if (secretIds.Count == 0)
        {
            return Array.Empty<string>();
        }

        List<string> values = new(secretIds.Count);
        for (int i = 0; i < secretIds.Count; i++)
        {
            string? secretId = secretIds[i];
            if (string.IsNullOrWhiteSpace(secretId))
            {
                throw new InvalidOperationException("Private key secret id must not be empty.");
            }

            string value = await LoadRequiredSecretAsync(secretId, "private key", cancellationToken).ConfigureAwait(false);
            values.Add(value);
        }

        return values;
    }

    private async ValueTask<string> LoadRequiredSecretAsync(
        string secretId,
        string secretKind,
        CancellationToken cancellationToken)
    {
        string? value = await _secretStore.LoadSecretAsync(secretId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Unable to resolve SSH {secretKind} secret for id '{secretId}'.");
        }

        return value.Trim();
    }
}
