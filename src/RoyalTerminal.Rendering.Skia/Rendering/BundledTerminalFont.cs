// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering;

/// <summary>Provides an owned primary face when the system has no usable monospace fonts.</summary>
internal static class BundledTerminalFont
{
    internal static SKTypeface CreateTypeface()
    {
        // Embed the font so default construction also works before a host can
        // assign FontSource.File or its own caller-owned typeface collection.
        using Stream stream = typeof(BundledTerminalFont).Assembly.GetManifestResourceStream(
            "RoyalTerminal.Fonts.JetBrainsMono-Regular.ttf")
            ?? throw new InvalidOperationException("The bundled terminal font resource is missing.");
        return SKTypeface.FromStream(stream)
            ?? throw new InvalidOperationException("The bundled terminal font could not be loaded.");
    }
}
