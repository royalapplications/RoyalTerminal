// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Avalonia.App.Services.Notifications;

// XDG resource names always use POSIX paths, including when the resolver is
// exercised with an in-memory filesystem on a Windows build agent.
internal static class LinuxDesktopPath
{
    internal static bool IsAbsolute(string path) => path.StartsWith('/');

    internal static string Combine(string left, string right)
        => IsAbsolute(right) || left.Length == 0 ? right
            : right.Length == 0 ? left : left + (left.EndsWith('/') ? "" : "/") + right;

    internal static string Combine(string first, string second, string third)
        => Combine(Combine(first, second), third);
}
