// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>Best-effort initialization of the macOS font registry before the first terminal is created.</summary>
public static class TerminalFontWarmup
{
    /// <summary>
    /// Starts one background font query on macOS, overlapping font discovery with application startup.
    /// Call once from the host's composition root before UI initialization. The returned task owns
    /// the query and its temporary resources; it never accesses controls or a renderer. It completes
    /// with false on unsupported platforms or initialization failure, leaving normal font discovery
    /// available. It need not be awaited before showing a window. This does not warm GPU resources.
    /// </summary>
    public static Task<bool> StartAsync()
        => OperatingSystem.IsMacOS() ? StartAsync(QuerySystemEmojiFont) : Task.FromResult(false);

    internal static Task<bool> StartAsync(Func<bool> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        try
        {
            return Task.Run(() =>
            {
                try { return query(); }
                catch (Exception) { return false; } // Optional optimization cannot prevent application startup.
            });
        }
        catch (Exception) { return Task.FromResult(false); }
    }

    private static bool QuerySystemEmojiFont()
    {
        using SKFontManager manager = SKFontManager.CreateDefault();
        using SKTypeface? typeface = manager.MatchFamily(SkiaTerminalFontMatcher.AppleColorEmojiFamily, SKFontStyle.Normal);
        return typeface is not null;
    }
}
