// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.App - SSH host-key prompt validator.

using System.Text;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Ssh;

namespace RoyalTerminal.Avalonia.App.Services;

internal sealed class PromptingSshHostKeyValidator : ISshHostKeyValidator
{
    private readonly KnownHostsSshHostKeyValidator _knownHostsValidator;
    private readonly Func<SshHostKeyTrustPromptRequest, bool> _prompt;
    private readonly string _userKnownHostsFile;

    public PromptingSshHostKeyValidator(
        KnownHostsSshHostKeyValidator knownHostsValidator,
        Func<SshHostKeyTrustPromptRequest, bool> prompt,
        string? userKnownHostsFile = null)
    {
        _knownHostsValidator = knownHostsValidator ?? throw new ArgumentNullException(nameof(knownHostsValidator));
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        _userKnownHostsFile = string.IsNullOrWhiteSpace(userKnownHostsFile)
            ? GetDefaultUserKnownHostsFile()
            : userKnownHostsFile.Trim();
    }

    public bool IsTrusted(SshEndpointOptions endpoint, SshHostKeyInfo hostKey)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        SshKnownHostTrustStatus knownHostStatus = _knownHostsValidator.GetTrustStatus(endpoint, hostKey);
        if (knownHostStatus == SshKnownHostTrustStatus.Trusted)
        {
            return true;
        }

        if (knownHostStatus != SshKnownHostTrustStatus.Unknown)
        {
            return false;
        }

        bool canPersistTrust = CanPersistKnownHostsEntry(endpoint, hostKey);
        SshHostKeyTrustPromptRequest request = new(
            Host: endpoint.Host,
            Port: endpoint.Port,
            Username: endpoint.Username,
            HostKeyAlgorithm: string.IsNullOrWhiteSpace(hostKey.HostKeyAlgorithm)
                ? "unknown"
                : hostKey.HostKeyAlgorithm,
            FingerprintSha256: string.IsNullOrWhiteSpace(hostKey.FingerprintSha256)
                ? "<missing>"
                : hostKey.FingerprintSha256,
            FingerprintMd5: hostKey.FingerprintMd5,
            KeyLengthBits: hostKey.KeyLengthBits,
            HostKeyBase64: hostKey.HostKeyBase64,
            WillPersistTrust: canPersistTrust,
            KnownHostsFilePath: _userKnownHostsFile);

        if (!_prompt(request))
        {
            return false;
        }

        if (canPersistTrust)
        {
            AppendKnownHostsEntry(endpoint, hostKey);
        }

        return true;
    }

    private void AppendKnownHostsEntry(SshEndpointOptions endpoint, SshHostKeyInfo hostKey)
    {
        string? directory = Path.GetDirectoryName(_userKnownHostsFile);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string hostPattern = endpoint.Port == 22
            ? endpoint.Host
            : $"[{endpoint.Host}]:{endpoint.Port}";
        string line = $"{hostPattern} {hostKey.HostKeyAlgorithm} {hostKey.HostKeyBase64}";

        using FileStream stream = new(
            _userKnownHostsFile,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read);
        using StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.WriteLine(line);
    }

    private static string GetDefaultUserKnownHostsFile()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            userProfile = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        return Path.Combine(userProfile, ".ssh", "known_hosts");
    }

    private static bool CanPersistKnownHostsEntry(SshEndpointOptions endpoint, SshHostKeyInfo hostKey)
    {
        return IsKnownHostsHostSafe(endpoint.Host) &&
            IsKnownHostsTokenSafe(hostKey.HostKeyAlgorithm) &&
            IsKnownHostsTokenSafe(hostKey.HostKeyBase64) &&
            IsValidBase64(hostKey.HostKeyBase64);
    }

    private static bool IsKnownHostsHostSafe(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        for (int i = 0; i < host.Length; i++)
        {
            char c = host[i];
            if (char.IsWhiteSpace(c) || c is ',' or '*' or '?' or '!' or '[' or ']')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsKnownHostsTokenSafe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsWhiteSpace(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidBase64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            _ = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
