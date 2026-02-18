// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal.Transport.Ssh.Abstractions - OpenSSH known_hosts validator.

using System.Security.Cryptography;
using System.Text;

namespace RoyalTerminal.Terminal.Transport.Ssh;

/// <summary>
/// Host-key validator that trusts keys already present in OpenSSH known-hosts files.
/// </summary>
public sealed class KnownHostsSshHostKeyValidator : ISshHostKeyValidator
{
    private readonly IReadOnlyList<string> _knownHostsFiles;

    /// <summary>
    /// Initializes a known-hosts validator.
    /// </summary>
    public KnownHostsSshHostKeyValidator(IReadOnlyList<string>? knownHostsFiles = null)
    {
        _knownHostsFiles = knownHostsFiles is null
            ? GetDefaultKnownHostsFiles()
            : NormalizeKnownHostsFiles(knownHostsFiles);
    }

    /// <summary>
    /// Gets the known-hosts files searched for trust entries.
    /// </summary>
    public IReadOnlyList<string> KnownHostsFiles => _knownHostsFiles;

    /// <inheritdoc />
    public bool IsTrusted(SshEndpointOptions endpoint, SshHostKeyInfo hostKey)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        byte[]? presentedHostKeyBytes = null;
        if (!string.IsNullOrWhiteSpace(hostKey.HostKeyBase64))
        {
            presentedHostKeyBytes = TryDecodeBase64(hostKey.HostKeyBase64);
            if (presentedHostKeyBytes is null)
            {
                return false;
            }
        }

        string normalizedFingerprint = NormalizeFingerprint(hostKey.FingerprintSha256);
        if (presentedHostKeyBytes is null && string.IsNullOrWhiteSpace(normalizedFingerprint))
        {
            return false;
        }

        bool matchedTrustedEntry = false;
        for (int i = 0; i < _knownHostsFiles.Count; i++)
        {
            string filePath = _knownHostsFiles[i];
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                continue;
            }

            foreach (string line in File.ReadLines(filePath))
            {
                if (!TryParseKnownHostsLine(
                        line,
                        endpoint,
                        hostKey.HostKeyAlgorithm,
                        out bool isRevoked,
                        out byte[]? entryKeyBytes,
                        out string? entryFingerprint))
                {
                    continue;
                }

                if (presentedHostKeyBytes is not null)
                {
                    if (entryKeyBytes is null ||
                        entryKeyBytes.Length != presentedHostKeyBytes.Length ||
                        !CryptographicOperations.FixedTimeEquals(entryKeyBytes, presentedHostKeyBytes))
                    {
                        continue;
                    }
                }
                else if (!string.Equals(entryFingerprint, normalizedFingerprint, StringComparison.Ordinal))
                {
                    continue;
                }

                if (isRevoked)
                {
                    return false;
                }

                matchedTrustedEntry = true;
            }
        }

        return matchedTrustedEntry;
    }

    /// <summary>
    /// Returns default known-hosts file paths for the current platform.
    /// </summary>
    public static IReadOnlyList<string> GetDefaultKnownHostsFiles()
    {
        List<string> paths = new(capacity: 3);

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            paths.Add(Path.Combine(userProfile, ".ssh", "known_hosts"));
        }

        if (OperatingSystem.IsWindows())
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (!string.IsNullOrWhiteSpace(programData))
            {
                paths.Add(Path.Combine(programData, "ssh", "ssh_known_hosts"));
            }
        }
        else
        {
            paths.Add("/etc/ssh/ssh_known_hosts");
            paths.Add("/etc/ssh/ssh_known_hosts2");
        }

        return NormalizeKnownHostsFiles(paths);
    }

    private static IReadOnlyList<string> NormalizeKnownHostsFiles(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        List<string> normalized = new(paths.Count);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < paths.Count; i++)
        {
            string path = paths[i];
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            string trimmed = path.Trim();
            if (seen.Add(trimmed))
            {
                normalized.Add(trimmed);
            }
        }

        if (normalized.Count == 0)
        {
            throw new ArgumentException("At least one known-hosts file path must be configured.", nameof(paths));
        }

        return normalized;
    }

    private static bool TryParseKnownHostsLine(
        string line,
        SshEndpointOptions endpoint,
        string hostKeyAlgorithm,
        out bool isRevoked,
        out byte[]? entryKeyBytes,
        out string? entryFingerprint)
    {
        isRevoked = false;
        entryKeyBytes = null;
        entryFingerprint = null;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        string trimmed = line.Trim();
        if (trimmed.StartsWith('#'))
        {
            return false;
        }

        string[] parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            return false;
        }

        int hostFieldIndex;
        int algorithmIndex;
        int keyIndex;

        if (parts[0].StartsWith('@'))
        {
            if (parts.Length < 4)
            {
                return false;
            }

            hostFieldIndex = 1;
            algorithmIndex = 2;
            keyIndex = 3;
            if (string.Equals(parts[0], "@revoked", StringComparison.OrdinalIgnoreCase))
            {
                isRevoked = true;
            }
            else
            {
                // Unsupported known_hosts marker (for example @cert-authority).
                return false;
            }
        }
        else
        {
            hostFieldIndex = 0;
            algorithmIndex = 1;
            keyIndex = 2;
        }

        string hostPatterns = parts[hostFieldIndex];
        if (!MatchesHostPatterns(hostPatterns, endpoint))
        {
            return false;
        }

        string algorithm = parts[algorithmIndex];
        if (!string.IsNullOrWhiteSpace(hostKeyAlgorithm) &&
            !string.Equals(algorithm, hostKeyAlgorithm, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string keyBase64 = parts[keyIndex];
        entryKeyBytes = TryDecodeBase64(keyBase64);
        if (entryKeyBytes is null)
        {
            return false;
        }

        entryFingerprint = NormalizeFingerprint(Convert.ToBase64String(SHA256.HashData(entryKeyBytes)));
        return true;
    }

    private static byte[]? TryDecodeBase64(string base64)
    {
        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool MatchesHostPatterns(string hostPatterns, SshEndpointOptions endpoint)
    {
        string[] patterns = hostPatterns.Split(',', StringSplitOptions.RemoveEmptyEntries);
        bool matched = false;

        for (int i = 0; i < patterns.Length; i++)
        {
            string pattern = patterns[i].Trim();
            if (pattern.Length == 0)
            {
                continue;
            }

            bool negated = pattern.StartsWith('!');
            string effectivePattern = negated ? pattern[1..] : pattern;
            if (!MatchesHostPattern(effectivePattern, endpoint))
            {
                continue;
            }

            if (negated)
            {
                return false;
            }

            matched = true;
        }

        return matched;
    }

    private static bool MatchesHostPattern(string pattern, SshEndpointOptions endpoint)
    {
        if (pattern.StartsWith("|1|", StringComparison.Ordinal))
        {
            return MatchesHashedHost(pattern, endpoint);
        }

        if (TryParseBracketedHostAndPort(pattern, out string? bracketedHost, out int bracketedPort))
        {
            return bracketedPort == endpoint.Port && MatchesOpenSshHostPattern(bracketedHost, endpoint.Host);
        }

        if (endpoint.Port != 22)
        {
            return false;
        }

        return MatchesOpenSshHostPattern(pattern, endpoint.Host);
    }

    private static bool MatchesHashedHost(string pattern, SshEndpointOptions endpoint)
    {
        string[] parts = pattern.Split('|');
        if (parts.Length != 4 || !string.Equals(parts[1], "1", StringComparison.Ordinal))
        {
            return false;
        }

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expectedHash = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        foreach (string candidate in BuildHashedHostCandidates(endpoint))
        {
            byte[] candidateBytes = Encoding.UTF8.GetBytes(candidate);
            using HMACSHA1 hmac = new(salt);
            byte[] actualHash = hmac.ComputeHash(candidateBytes);
            if (CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> BuildHashedHostCandidates(SshEndpointOptions endpoint)
    {
        string host = endpoint.Host;
        string lowerHost = endpoint.Host.ToLowerInvariant();
        string bracketed = $"[{endpoint.Host}]:{endpoint.Port}";
        string lowerBracketed = $"[{lowerHost}]:{endpoint.Port}";

        if (endpoint.Port == 22)
        {
            yield return host;
            if (!string.Equals(host, lowerHost, StringComparison.Ordinal))
            {
                yield return lowerHost;
            }
        }

        yield return bracketed;
        if (!string.Equals(bracketed, lowerBracketed, StringComparison.Ordinal))
        {
            yield return lowerBracketed;
        }
    }

    private static bool TryParseBracketedHostAndPort(string pattern, out string host, out int port)
    {
        host = string.Empty;
        port = 0;

        if (!pattern.StartsWith('['))
        {
            return false;
        }

        int closeIndex = pattern.IndexOf(']');
        if (closeIndex <= 1 || closeIndex >= pattern.Length - 2)
        {
            return false;
        }

        if (pattern[closeIndex + 1] != ':')
        {
            return false;
        }

        host = pattern[1..closeIndex];
        return int.TryParse(pattern.AsSpan(closeIndex + 2), out port);
    }

    private static bool MatchesOpenSshHostPattern(string pattern, string host)
    {
        if (string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!pattern.Contains('*') && !pattern.Contains('?'))
        {
            return false;
        }

        return MatchesWildcardPattern(pattern, host);
    }

    private static bool MatchesWildcardPattern(string pattern, string value)
    {
        int patternIndex = 0;
        int valueIndex = 0;
        int starPatternIndex = -1;
        int starValueIndex = -1;

        while (valueIndex < value.Length)
        {
            if (patternIndex < pattern.Length &&
                (pattern[patternIndex] == '?' ||
                 char.ToUpperInvariant(pattern[patternIndex]) == char.ToUpperInvariant(value[valueIndex])))
            {
                patternIndex++;
                valueIndex++;
                continue;
            }

            if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                starPatternIndex = patternIndex++;
                starValueIndex = valueIndex;
                continue;
            }

            if (starPatternIndex >= 0)
            {
                patternIndex = starPatternIndex + 1;
                valueIndex = ++starValueIndex;
                continue;
            }

            return false;
        }

        while (patternIndex < pattern.Length && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }

    private static string NormalizeFingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = value.Trim();
        if (normalized.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring("SHA256:".Length);
        }

        return normalized.TrimEnd('=').Trim();
    }
}
