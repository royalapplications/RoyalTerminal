// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Demo - SSH host-key prompt validator.

using System.Text;
using RoyalTerminal.Demo.ViewModels;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Ssh;

namespace RoyalTerminal.Demo.Services;

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

        if (_knownHostsValidator.IsTrusted(endpoint, hostKey))
        {
            return true;
        }

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
            WillPersistTrust: !string.IsNullOrWhiteSpace(hostKey.HostKeyBase64),
            KnownHostsFilePath: _userKnownHostsFile);

        if (!_prompt(request))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(hostKey.HostKeyBase64))
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
        string algorithm = string.IsNullOrWhiteSpace(hostKey.HostKeyAlgorithm)
            ? "ssh-ed25519"
            : hostKey.HostKeyAlgorithm;
        string line = $"{hostPattern} {algorithm} {hostKey.HostKeyBase64}";

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
}
