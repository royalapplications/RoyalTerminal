// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.App.Services;

/// <summary>Recognizes inert Windows toast launches; only an existing live session may handle a notification action.</summary>
public static class TerminalNotificationLaunch
{
    /// <summary>
    /// Returns true when the host should exit before starting its desktop lifetime.
    /// Windows may launch another instance as well as delivering the live toast
    /// callback. Call this from the application's entry point to avoid creating an
    /// unrelated terminal session. No terminal-provided command line is accepted.
    /// </summary>
    /// <param name="arguments">Arguments passed to the application's entry point.</param>
    public static bool IsInertActivation(ReadOnlySpan<string> arguments)
    {
        const string prefix = "--royalterminal-notification";
        if (arguments.Length != 1 || arguments[0] is not { } argument) return false;
        return argument == prefix || argument.Length == prefix.Length + 2 &&
            argument.StartsWith(prefix, StringComparison.Ordinal) && argument[prefix.Length] == ':' && argument[^1] is >= '1' and <= '5';
    }
}
