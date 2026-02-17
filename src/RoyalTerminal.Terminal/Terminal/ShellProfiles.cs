// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Shell profile abstractions.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Local shell profile definition.
/// </summary>
public sealed record ShellProfile(
    string Id,
    string DisplayName,
    TerminalCommandSpec Command);

/// <summary>
/// Lists available shell profiles and the preferred default profile.
/// </summary>
public interface IShellProfileCatalog
{
    /// <summary>
    /// Gets available profiles for the current environment.
    /// </summary>
    IReadOnlyList<ShellProfile> GetProfiles();

    /// <summary>
    /// Gets the default shell profile.
    /// </summary>
    ShellProfile GetDefaultProfile();
}

/// <summary>
/// Default cross-platform shell profile catalog.
/// </summary>
public sealed class DefaultShellProfileCatalog : IShellProfileCatalog
{
    /// <inheritdoc />
    public IReadOnlyList<ShellProfile> GetProfiles()
    {
        List<ShellProfile> profiles = new();

        if (OperatingSystem.IsWindows())
        {
            AddWindowsProfile(profiles, "pwsh", "PowerShell (pwsh)", FindInPath("pwsh.exe"));
            AddWindowsProfile(
                profiles,
                "powershell",
                "Windows PowerShell",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"));
            AddWindowsProfile(
                profiles,
                "cmd",
                "Command Prompt",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"));
        }
        else
        {
            string? envShell = Environment.GetEnvironmentVariable("SHELL");
            if (!string.IsNullOrWhiteSpace(envShell) && File.Exists(envShell))
            {
                profiles.Add(new ShellProfile(
                    "env-shell",
                    Path.GetFileNameWithoutExtension(envShell),
                    new TerminalCommandSpec(envShell, Array.Empty<string>())));
            }

            AddUnixProfile(profiles, "zsh", "Zsh", "/bin/zsh");
            AddUnixProfile(profiles, "bash", "Bash", "/bin/bash");
            AddUnixProfile(profiles, "sh", "POSIX sh", "/bin/sh");
        }

        if (profiles.Count == 0)
        {
            throw new PlatformNotSupportedException("No shell profile is available for the current platform.");
        }

        return profiles;
    }

    /// <inheritdoc />
    public ShellProfile GetDefaultProfile()
    {
        IReadOnlyList<ShellProfile> profiles = GetProfiles();
        return profiles[0];
    }

    private static void AddUnixProfile(List<ShellProfile> profiles, string id, string displayName, string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        if (profiles.Any(p => string.Equals(p.Command.FileName, path, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        profiles.Add(new ShellProfile(
            id,
            displayName,
            new TerminalCommandSpec(path, Array.Empty<string>())));
    }

    private static void AddWindowsProfile(List<ShellProfile> profiles, string id, string displayName, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        profiles.Add(new ShellProfile(
            id,
            displayName,
            new TerminalCommandSpec(path, Array.Empty<string>())));
    }

    private static string? FindInPath(string executableName)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string[] directories = path.Split(Path.PathSeparator);
        for (int i = 0; i < directories.Length; i++)
        {
            string directory = directories[i];
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            string candidate = Path.Combine(directory, executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
