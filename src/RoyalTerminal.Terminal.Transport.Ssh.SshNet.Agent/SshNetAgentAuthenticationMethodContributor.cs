// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal.Transport.Ssh.SshNet.Agent - SSH agent auth contributor.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Renci.SshNet;
using SshNet.Agent;
using RoyalTerminal.Terminal.Transport.Ssh.SshNet;

namespace RoyalTerminal.Terminal.Transport.Ssh.SshNet.Agent;

/// <summary>
/// Adds authentication methods backed by SSH agent identities.
/// </summary>
public sealed class SshNetAgentAuthenticationMethodContributor : ISshNetAuthenticationMethodContributor
{
    private static readonly TimeSpan AgentRequestTimeout = TimeSpan.FromMilliseconds(750);
    private const string OpenSshAgentPipe = @"\\.\pipe\openssh-ssh-agent";

    /// <inheritdoc />
    public IReadOnlyList<AuthenticationMethod> CreateAuthenticationMethods(
        SshTransportOptions options,
        SshResolvedCredentials credentials)
    {
        if (!options.Authentication.UseAgent && !credentials.UseAgent)
        {
            return Array.Empty<AuthenticationMethod>();
        }

        if (!IsAgentLikelyAvailable())
        {
            return Array.Empty<AuthenticationMethod>();
        }

        if (!TryRequestIdentitiesWithinTimeout(out SshAgentPrivateKey[] identities))
        {
            return Array.Empty<AuthenticationMethod>();
        }

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

    private static bool TryRequestIdentitiesWithinTimeout(out SshAgentPrivateKey[] identities)
    {
        identities = Array.Empty<SshAgentPrivateKey>();

        SshAgentPrivateKey[]? workerResult = null;
        Exception? workerFailure = null;
        Thread worker = new(
            () =>
            {
                try
                {
                    SshAgent agent = new();
                    workerResult = agent.RequestIdentities();
                }
                catch (Exception ex)
                {
                    workerFailure = ex;
                }
            })
        {
            IsBackground = true,
            Name = "SshAgent-IdentityProbe",
        };

        worker.Start();
        if (!worker.Join(AgentRequestTimeout))
        {
            return false;
        }

        if (workerFailure is not null)
        {
            return false;
        }

        identities = workerResult ?? Array.Empty<SshAgentPrivateKey>();
        return true;
    }

    private static bool IsAgentLikelyAvailable()
    {
        string? authSock = Environment.GetEnvironmentVariable("SSH_AUTH_SOCK");

        if (OperatingSystem.IsWindows())
        {
            if (IsNamedPipeEndpointAvailable(authSock))
            {
                return true;
            }

            if (IsNamedPipeEndpointAvailable(OpenSshAgentPipe))
            {
                return true;
            }

            try
            {
                return Process.GetProcessesByName("pageant").Length > 0;
            }
            catch
            {
                return false;
            }
        }

        return !string.IsNullOrWhiteSpace(authSock) && File.Exists(authSock);
    }

    private static bool IsNamedPipeEndpointAvailable(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return false;
        }

        if (!endpoint.StartsWith(@"\\.\pipe\", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return WaitNamedPipe(endpoint, 1);
    }

    [DllImport("kernel32.dll", EntryPoint = "WaitNamedPipeW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WaitNamedPipe(string lpNamedPipeName, uint nTimeOut);
}
