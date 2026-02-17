// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal.Transport.Ssh.SshNet - SSH.NET auth method extension point.

using Renci.SshNet;

namespace RoyalTerminal.Terminal.Transport.Ssh.SshNet;

/// <summary>
/// Adds SSH.NET authentication methods for transport startup.
/// </summary>
public interface ISshNetAuthenticationMethodContributor
{
    /// <summary>
    /// Creates additional authentication methods.
    /// </summary>
    IReadOnlyList<AuthenticationMethod> CreateAuthenticationMethods(
        SshTransportOptions options,
        SshResolvedCredentials credentials);
}
