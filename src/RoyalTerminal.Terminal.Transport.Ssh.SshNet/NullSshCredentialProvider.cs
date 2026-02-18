// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal.Transport.Ssh.SshNet - Default SSH credential provider.

namespace RoyalTerminal.Terminal.Transport.Ssh.SshNet;

/// <summary>
/// Credential provider used when no runtime SSH credential provider is configured.
/// </summary>
public sealed class NullSshCredentialProvider : ISshCredentialProvider
{
    /// <inheritdoc />
    public ValueTask<SshResolvedCredentials> ResolveAsync(
        SshCredentialRequest request,
        CancellationToken cancellationToken = default)
    {
        _ = request;
        _ = cancellationToken;

        throw new InvalidOperationException(
            "No SSH credential provider was configured. " +
            "Provide an ISshCredentialProvider to resolve runtime credentials " +
            "(for example SecretStoreSshCredentialProvider with SshSecretProtectionFactory.CreateDefaultSecretStore()).");
    }
}
