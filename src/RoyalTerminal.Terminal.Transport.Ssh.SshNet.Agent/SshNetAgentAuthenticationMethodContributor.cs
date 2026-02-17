// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal.Transport.Ssh.SshNet.Agent - SSH agent auth contributor.

using Renci.SshNet;
using SshNet.Agent;
using RoyalTerminal.Terminal.Transport.Ssh.SshNet;

namespace RoyalTerminal.Terminal.Transport.Ssh.SshNet.Agent;

/// <summary>
/// Adds authentication methods backed by SSH agent identities.
/// </summary>
public sealed class SshNetAgentAuthenticationMethodContributor : ISshNetAuthenticationMethodContributor
{
    /// <inheritdoc />
    public IReadOnlyList<AuthenticationMethod> CreateAuthenticationMethods(
        SshTransportOptions options,
        SshResolvedCredentials credentials)
    {
        if (!options.Authentication.UseAgent && !credentials.UseAgent)
        {
            return Array.Empty<AuthenticationMethod>();
        }

        try
        {
            SshAgent agent = new();
            SshAgentPrivateKey[] identities = agent.RequestIdentities();
            if (identities.Length == 0)
            {
                return Array.Empty<AuthenticationMethod>();
            }

            IPrivateKeySource[] sources = new IPrivateKeySource[identities.Length];
            for (int i = 0; i < identities.Length; i++)
            {
                sources[i] = identities[i];
            }

            return new AuthenticationMethod[]
            {
                new PrivateKeyAuthenticationMethod(options.Endpoint.Username, sources),
            };
        }
        catch
        {
            return Array.Empty<AuthenticationMethod>();
        }
    }
}
