// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

public sealed partial class GhosttyTerminal
{
    /// <summary>Sets the OSC 7501 callback. Keep its delegate alive until unregistered.
    /// A nonzero callback also enables support-query replies.</summary>
    public void SetProgramStatusCallback(nint callback)
        => SetPointerOption(GhosttyVtNative.GhosttyTerminalOption.ProgramStatus, callback);

    /// <summary>Sets the OSC 133 callback. Keep its delegate alive until unregistered.</summary>
    public void SetSemanticPromptCallback(nint callback)
        => SetPointerOption(GhosttyVtNative.GhosttyTerminalOption.SemanticPrompt, callback);

    /// <summary>Sets the full-reset callback. Keep its delegate alive until unregistered.</summary>
    public void SetResetCallback(nint callback)
        => SetPointerOption(GhosttyVtNative.GhosttyTerminalOption.Reset, callback);

    /// <summary>Enables rectangular checksum replies; disabled by default.</summary>
    public void SetXtChecksumReport(bool enabled)
        => SetStructOption(GhosttyVtNative.GhosttyTerminalOption.XtChecksumReport, enabled,
            "ghostty_terminal_set(xt_checksum_report)");

    /// <summary>Sets default XTCHECKSUM flags, restored by RIS and DECSTR (0–31).</summary>
    public void SetXtChecksumExtension(byte flags)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(flags, (byte)31);
        SetStructOption(GhosttyVtNative.GhosttyTerminalOption.XtChecksumExtension, flags,
            "ghostty_terminal_set(xt_checksum_extension)");
    }

    /// <summary>Gets the application-requested pointer shape, excluding host hover overrides.</summary>
    public GhosttyVtNative.GhosttyMouseShape GetMouseShape()
        => GetValue<GhosttyVtNative.GhosttyMouseShape>(GhosttyVtNative.GhosttyTerminalData.MouseShape);

    /// <summary>Queries page and image memory without decompressing history.
    /// Visits every page; avoid calling for each input write.</summary>
    public unsafe GhosttyVtNative.GhosttyTerminalMemoryUsage GetMemoryUsage()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.GhosttyTerminalMemoryUsage usage = new()
        {
            Size = (nuint)sizeof(GhosttyVtNative.GhosttyTerminalMemoryUsage),
        };
        ThrowIfFailed(GhosttyVtNative.TerminalGet(_handle,
            GhosttyVtNative.GhosttyTerminalData.MemoryUsage, &usage), "ghostty_terminal_get(memory_usage)");
        return usage;
    }
}
