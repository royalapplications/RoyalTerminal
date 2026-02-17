// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - SSH credential contracts.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Requested SSH authentication strategies for a session.
/// </summary>
public sealed record SshAuthenticationOptions(
    bool UsePassword,
    string? PasswordSecretId,
    IReadOnlyList<string> PrivateKeySecretIds,
    bool UseAgent);

/// <summary>
/// Input payload for runtime SSH credential resolution.
/// </summary>
public sealed record SshCredentialRequest(
    SshEndpointOptions Endpoint,
    SshAuthenticationOptions Authentication);

/// <summary>
/// Resolved runtime SSH credentials.
/// </summary>
public sealed record SshResolvedCredentials(
    string? Password,
    IReadOnlyList<string> PrivateKeyPemOrPath,
    bool UseAgent);

/// <summary>
/// Resolves runtime SSH credentials.
/// </summary>
public interface ISshCredentialProvider
{
    /// <summary>
    /// Resolves credentials for the requested endpoint and auth settings.
    /// </summary>
    ValueTask<SshResolvedCredentials> ResolveAsync(
        SshCredentialRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Secret store abstraction for SSH credentials.
/// </summary>
public interface ISshSecretStore
{
    /// <summary>
    /// Loads a secret by identifier.
    /// </summary>
    ValueTask<string?> LoadSecretAsync(string secretId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a secret by identifier.
    /// </summary>
    ValueTask SaveSecretAsync(string secretId, string secretValue, CancellationToken cancellationToken = default);
}
