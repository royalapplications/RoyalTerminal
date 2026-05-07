// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Windows Terminal-compatible ConPTY environment block.

using System.Collections;
using System.Runtime.InteropServices;
using System.Text;

namespace RoyalTerminal.Terminal;

internal static class WindowsPtyEnvironment
{
    private static readonly string[] BuiltInWslEnvironmentVariables =
    [
        "WT_SESSION",
        "WT_PROFILE_ID",
    ];

    public static nint BuildEnvironmentBlock(
        IReadOnlyDictionary<string, string>? userEnvironment,
        Guid sessionId,
        Guid profileId)
    {
        SortedDictionary<string, string> variables = CreateEnvironmentVariables(
            ReadCurrentEnvironment(),
            userEnvironment,
            sessionId,
            profileId);
        return Marshal.StringToHGlobalUni(CreateEnvironmentBlockString(variables));
    }

    internal static SortedDictionary<string, string> CreateEnvironmentVariables(
        IEnumerable<KeyValuePair<string, string?>> baseEnvironment,
        IReadOnlyDictionary<string, string>? userEnvironment,
        Guid sessionId,
        Guid profileId)
    {
        ArgumentNullException.ThrowIfNull(baseEnvironment);

        SortedDictionary<string, string> variables = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string?> entry in baseEnvironment)
        {
            if (string.IsNullOrEmpty(entry.Key))
            {
                continue;
            }

            variables[entry.Key] = entry.Value ?? string.Empty;
        }

        variables["WT_SESSION"] = sessionId.ToString("D");
        variables["WT_PROFILE_ID"] = "{" + profileId.ToString("D") + "}";

        string originalWslEnv = variables.TryGetValue("WSLENV", out string? wslEnv)
            ? wslEnv
            : string.Empty;
        HashSet<string> existingWslEnvNames = CreateWslEnvironmentNameSet(originalWslEnv);
        List<string> additionalWslEnvNames = [];
        AddWslEnvironmentNames(BuiltInWslEnvironmentVariables, existingWslEnvNames, additionalWslEnvNames);

        if (userEnvironment is not null)
        {
            foreach (KeyValuePair<string, string> entry in userEnvironment)
            {
                if (string.IsNullOrEmpty(entry.Key))
                {
                    continue;
                }

                variables[entry.Key] = Environment.ExpandEnvironmentVariables(entry.Value ?? string.Empty);

                if (!string.Equals(entry.Key, "PATH", StringComparison.OrdinalIgnoreCase) &&
                    existingWslEnvNames.Add(entry.Key))
                {
                    additionalWslEnvNames.Add(entry.Key);
                }
            }
        }

        string effectiveWslEnv = variables.TryGetValue("WSLENV", out string? updatedWslEnv)
            ? updatedWslEnv
            : string.Empty;
        variables["WSLENV"] = PrependWslEnvironmentNames(additionalWslEnvNames, effectiveWslEnv);

        return variables;
    }

    private static IEnumerable<KeyValuePair<string, string?>> ReadCurrentEnvironment()
    {
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key)
            {
                yield return new KeyValuePair<string, string?>(key, entry.Value?.ToString());
            }
        }
    }

    private static HashSet<string> CreateWslEnvironmentNameSet(string wslEnv)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase)
        {
            "PATH",
        };

        ReadOnlySpan<char> remaining = wslEnv.AsSpan();
        while (!remaining.IsEmpty)
        {
            int separatorIndex = remaining.IndexOf(':');
            ReadOnlySpan<char> part = separatorIndex >= 0
                ? remaining[..separatorIndex]
                : remaining;

            if (!part.IsEmpty)
            {
                int flagsIndex = part.IndexOf('/');
                ReadOnlySpan<char> name = flagsIndex >= 0 ? part[..flagsIndex] : part;
                if (!name.IsEmpty)
                {
                    names.Add(name.ToString());
                }
            }

            if (separatorIndex < 0)
            {
                break;
            }

            remaining = remaining[(separatorIndex + 1)..];
        }

        return names;
    }

    private static void AddWslEnvironmentNames(
        IEnumerable<string> names,
        HashSet<string> existingNames,
        List<string> additionalNames)
    {
        foreach (string name in names)
        {
            if (existingNames.Add(name))
            {
                additionalNames.Add(name);
            }
        }
    }

    private static string PrependWslEnvironmentNames(List<string> additionalNames, string existingWslEnv)
    {
        if (additionalNames.Count == 0)
        {
            return existingWslEnv;
        }

        string additionalWslEnv = string.Join(':', additionalNames);
        if (existingWslEnv.Length == 0)
        {
            return additionalWslEnv;
        }

        return existingWslEnv[0] == ':'
            ? additionalWslEnv + existingWslEnv
            : additionalWslEnv + ":" + existingWslEnv;
    }

    private static string CreateEnvironmentBlockString(SortedDictionary<string, string> variables)
    {
        int capacity = 1;
        foreach (KeyValuePair<string, string> entry in variables)
        {
            capacity = checked(capacity + entry.Key.Length + entry.Value.Length + 2);
        }

        StringBuilder builder = new(capacity);
        foreach (KeyValuePair<string, string> entry in variables)
        {
            builder.Append(entry.Key)
                .Append('=')
                .Append(entry.Value)
                .Append('\0');
        }

        builder.Append('\0');
        return builder.ToString();
    }
}
