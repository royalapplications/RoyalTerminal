// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - SSH shell bootstrap command builder.

using System.Text;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Builds POSIX-shell bootstrap commands for SSH sessions.
/// </summary>
public static class SshShellBootstrapCommandBuilder
{
    /// <summary>
    /// Validates SSH environment-variable names and values.
    /// </summary>
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

    /// <summary>
    /// Builds a shell bootstrap command from SSH transport options.
    /// </summary>
    public static string? Build(SshTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Build(options.InitialCommand, options.EnvironmentVariables);
    }

    /// <summary>
    /// Builds a shell bootstrap command from initial command and optional environment variables.
    /// </summary>
    public static string? Build(
        string? initialCommand,
        IReadOnlyDictionary<string, string>? environmentVariables)
    {
        ValidateEnvironmentVariables(environmentVariables);

        StringBuilder? command = null;

        if (environmentVariables is not null)
        {
            foreach ((string key, string value) in environmentVariables)
            {
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

        if (!string.IsNullOrWhiteSpace(initialCommand))
        {
            command ??= new StringBuilder();
            if (command.Length > 0)
            {
                command.Append("; ");
            }

            command.Append(initialCommand.Trim());
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
