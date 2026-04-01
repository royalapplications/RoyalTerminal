// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.GhosttySharp - Helper for applying inline Ghostty config overlays.

using System.Text;

namespace RoyalTerminal.GhosttySharp;

/// <summary>
/// Applies inline Ghostty configuration overlays by materializing them as
/// temporary config files and loading them through the official file-based API.
/// </summary>
public static class GhosttyConfigOverlay
{
    /// <summary>
    /// Applies inline configuration text to an existing <see cref="GhosttyConfig"/>.
    /// </summary>
    /// <param name="config">Config instance to update.</param>
    /// <param name="overlayText">Ghostty configuration text to load.</param>
    /// <param name="filePrefix">Optional temp-file prefix used for diagnostics.</param>
    public static void ApplyText(
        GhosttyConfig config,
        string overlayText,
        string filePrefix = "royalterminal-config")
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentException.ThrowIfNullOrWhiteSpace(overlayText);

        string safePrefix = string.IsNullOrWhiteSpace(filePrefix)
            ? "royalterminal-config"
            : filePrefix.Trim();

        string tempPath = Path.Combine(
            Path.GetTempPath(),
            $"{safePrefix}-{Guid.NewGuid():N}.ghostty");

        try
        {
            File.WriteAllText(
                tempPath,
                overlayText,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            config.LoadFile(tempPath);
        }
        finally
        {
            TryDeleteTempFile(tempPath);
        }
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore temp cleanup failures.
        }
    }
}
