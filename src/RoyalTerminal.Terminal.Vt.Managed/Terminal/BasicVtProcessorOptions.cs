// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Managed VT processor options.

using RoyalTerminal.Sixel;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Options for the managed VT processor.
/// </summary>
public sealed record BasicVtProcessorOptions
{
    /// <summary>Default managed VT options.</summary>
    public static BasicVtProcessorOptions Default { get; } = new();

    /// <summary>Gets whether managed sixel image decoding is enabled.</summary>
    public bool SixelGraphicsEnabled { get; init; }

    /// <summary>Enables Ghostty-compatible session glyph registration and queries.</summary>
    public bool GlyphProtocolEnabled { get; init; } = true;

    /// <summary>
    /// Gets whether ED 2 (<c>CSI 2 J</c>) scrolls the active viewport into
    /// history before clearing it.
    /// </summary>
    public bool ScrollOnEraseInDisplay { get; init; }

    /// <summary>
    /// Allows resize to pull history back into the live viewport. The default
    /// matches the native host policy: enabled on Windows, disabled elsewhere.
    /// Set true for Ghostty lib-VT's standalone default behavior.
    /// </summary>
    public bool ResizePullScrollback { get; init; } = OperatingSystem.IsWindows();

    /// <summary>
    /// Gets whether CSI 21 t may report the current window title. This is disabled by
    /// default because a title report can expose host-controlled text to the child.
    /// </summary>
    public bool TitleReportEnabled { get; init; }

    /// <summary>
    /// Gets the terminal name returned for XTGETTCAP <c>TN</c> queries. Null or empty
    /// suppresses only that capability; the static Ghostty capability map remains available.
    /// </summary>
    public string? TerminfoName { get; init; } = "xterm-ghostty";

    /// <summary>
    /// Gets the maximum decoded bytes accumulated by one Kitty clipboard write.
    /// The 64 MiB default matches Ghostty. Zero permits only empty writes.
    /// </summary>
    public int ClipboardWriteLimitBytes { get; init; } = 64 * 1024 * 1024;

    /// <summary>Maximum encoded Kitty graphics bytes in one APC command.</summary>
    public int KittyGraphicsMaxApcBytes { get; init; } = 64 * 1024 * 1024;

    /// <summary>
    /// Maximum bytes in one loaded/decompressed Kitty image, also bounding its
    /// largest RGBA view before allocation. This per-image safety bound is
    /// independent of the retained image/frame admission budget.
    /// </summary>
    public int KittyGraphicsMaxImageBytes { get; init; } = 400 * 1024 * 1024;

    /// <summary>
    /// Kitty image/frame admission budget per screen. RGB is charged at three
    /// bytes per pixel until animation composition promotes it to RGBA. Like
    /// Ghostty, promotion and existing-frame edits do not evict or reject an
    /// existing image; a later new-image/frame admission enforces the budget.
    /// Renderer caches and caller-retained immutable publications are separate.
    /// </summary>
    public int KittyGraphicsStorageLimitBytes { get; init; } = 32 * 1024 * 1024;

    /// <summary>
    /// Maximum retained unfinished VT/UTF-8 fragment for continuation export.
    /// Zero disables retention; processing continues even when this bound is exceeded.
    /// </summary>
    public int ContinuationMaxBytes { get; init; } = 64 * 1024;

    /// <summary>Gets the monotonic clock used to expire synchronized-output holds.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>
    /// Gets the optional PNG decoder for Kitty graphics. Framework-independent callers
    /// inject an imaging provider; the Avalonia defaults provide the Skia implementation.
    /// Null supports raw RGB/RGBA images but rejects PNG images.
    /// </summary>
    public IKittyGraphicsPngDecoder? KittyGraphicsPngDecoder { get; init; }

    /// <summary>
    /// Gets optional host capabilities for Kitty file, temporary-file, and shared-memory
    /// payloads. Null permits direct transmission only; hosts inject their explicit policy.
    /// </summary>
    public IKittyGraphicsMediumReader? KittyGraphicsMediumReader { get; init; }

    /// <summary>Gets resource limits and compatibility settings for sixel decoding.</summary>
    public SixelDecoderOptions SixelDecoderOptions { get; init; } = SixelDecoderOptions.Default;
}
