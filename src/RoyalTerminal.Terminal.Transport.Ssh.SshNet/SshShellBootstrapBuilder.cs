// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal.Transport.Ssh.SshNet - Bootstrap command builder for shell env setup.

using System.Text;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Terminal.Transport.Ssh.SshNet;

internal static class SshShellBootstrapBuilder
{
    public static void ValidateEnvironmentVariables(IReadOnlyDictionary<string, string>? environmentVariables)
    {
        if (environmentVariables is null)
        {
            return;
        }

        foreach (KeyValuePair<string, string> pair in environmentVariables)
        {
            ValidateEnvironmentVariableName(pair.Key);
            ValidateEnvironmentVariableValue(pair.Key, pair.Value);
        }
    }

    public static string? BuildBootstrapCommand(SshTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateEnvironmentVariables(options.EnvironmentVariables);

        StringBuilder? command = null;

        if (options.EnvironmentVariables is not null)
        {
            foreach ((string key, string value) in options.EnvironmentVariables)
            {
                ValidateEnvironmentVariableValue(key, value);
                command ??= new StringBuilder();
                if (command.Length > 0)
                {
                    command.Append("; ");
                }

                command.Append("export ");
                command.Append(key);
                command.Append("='");
                command.Append(EscapeSingleQuoted(value));
                command.Append('\'');
            }
        }

        if (!string.IsNullOrWhiteSpace(options.InitialCommand))
        {
            command ??= new StringBuilder();
            if (command.Length > 0)
            {
                command.Append("; ");
            }

            command.Append(options.InitialCommand.Trim());
        }

        return command?.ToString();
    }

    private static void ValidateEnvironmentVariableName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("SSH environment variable name must not be empty.");
        }

        if (!IsAsciiLetter(name[0]) && name[0] != '_')
        {
            throw new InvalidOperationException(
                $"SSH environment variable '{name}' has an invalid identifier. " +
                "Names must match [A-Za-z_][A-Za-z0-9_]*.");
        }

        for (int i = 1; i < name.Length; i++)
        {
            char c = name[i];
            if (!IsAsciiLetter(c) && !char.IsAsciiDigit(c) && c != '_')
            {
                throw new InvalidOperationException(
                    $"SSH environment variable '{name}' has an invalid identifier. " +
                    "Names must match [A-Za-z_][A-Za-z0-9_]*.");
            }
        }
    }

    private static void ValidateEnvironmentVariableValue(string name, string? value)
    {
        if (value is null)
        {
            throw new InvalidOperationException(
                $"SSH environment variable '{name}' must have a non-null value.");
        }

        if (value.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0)
        {
            throw new InvalidOperationException(
                $"SSH environment variable '{name}' contains forbidden control characters (CR/LF/NUL).");
        }
    }

    private static string EscapeSingleQuoted(string value)
    {
        return value.Replace("'", "'\"'\"'", StringComparison.Ordinal);
    }

    private static bool IsAsciiLetter(char c)
    {
        return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
    }
}
