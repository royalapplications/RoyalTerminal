// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;

namespace RoyalTerminal.Terminal;

// Private live-state layout, deliberately independent of snapshot-v1 bit order.
// Known hot-path flags bypass mode-number dispatch altogether.
[Flags]
internal enum ManagedDecModeFlag : uint
{
    None = 0,
    Columns132 = 1U << 0,
    SmoothScroll = 1U << 1,
    ReverseVideo = 1U << 2,
    AutoRepeat = 1U << 3,
    MouseX10 = 1U << 4,
    CursorBlink = 1U << 5,
    AllowColumnMode = 1U << 6,
    ReverseWrap = 1U << 7,
    LeftRightMargins = 1U << 8,
    MouseNormal = 1U << 9,
    MouseButton = 1U << 10,
    MouseAny = 1U << 11,
    FocusEvents = 1U << 12,
    MouseUtf8 = 1U << 13,
    MouseSgr = 1U << 14,
    AlternateScroll = 1U << 15,
    MouseUrxvt = 1U << 16,
    MouseSgrPixels = 1U << 17,
    NumLockKeypad = 1U << 18,
    AltEscapePrefix = 1U << 19,
    AltSendsEscape = 1U << 20,
    ReverseWrapExtended = 1U << 21,
    SynchronizedOutput = 1U << 22,
    GraphemeClusters = 1U << 23,
    ColorSchemeReports = 1U << 24,
    VisibilityReports = 1U << 25,
    SizeReports = 1U << 26,
    KittyClipboard = 1U << 27,
}

/// <summary>
/// Allocation-free storage for the managed engine's extended DEC mode subset.
/// Number lookup is a fixed switch, not a scan followed by a hash-table probe.
/// Semantic setters, reset policy and snapshot mode ordering remain outside it.
/// </summary>
internal struct ManagedDecModeState
{
    private ManagedDecModeFlag _values;

    internal static ReadOnlySpan<int> SupportedModes =>
    [
        3, 4, 5, 8, 9, 12, 40, 45, 69, 1000, 1002, 1003, 1004, 1005,
        1006, 1007, 1015, 1016, 1035, 1036, 1039, 1045, 2026, 2027,
        2031, 2033, 2048, 5522,
    ];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal readonly bool Contains(ManagedDecModeFlag flag) => (_values & flag) != 0;

    internal readonly bool Contains(int mode) => Contains(FlagFor(mode));

    internal readonly bool TryGet(int mode, out bool enabled)
    {
        ManagedDecModeFlag flag = FlagFor(mode);
        enabled = Contains(flag);
        return flag != ManagedDecModeFlag.None;
    }

    internal void Set(int mode, bool enabled)
    {
        ManagedDecModeFlag flag = FlagFor(mode);
        _values = enabled ? _values | flag : _values & ~flag;
    }

    internal void Reset() => _values = ManagedDecModeFlag.AlternateScroll |
        ManagedDecModeFlag.NumLockKeypad | ManagedDecModeFlag.AltEscapePrefix;

    internal static ManagedDecModeFlag FlagFor(int mode) => mode switch
    {
        3 => ManagedDecModeFlag.Columns132,
        4 => ManagedDecModeFlag.SmoothScroll,
        5 => ManagedDecModeFlag.ReverseVideo,
        8 => ManagedDecModeFlag.AutoRepeat,
        9 => ManagedDecModeFlag.MouseX10,
        12 => ManagedDecModeFlag.CursorBlink,
        40 => ManagedDecModeFlag.AllowColumnMode,
        45 => ManagedDecModeFlag.ReverseWrap,
        69 => ManagedDecModeFlag.LeftRightMargins,
        1000 => ManagedDecModeFlag.MouseNormal,
        1002 => ManagedDecModeFlag.MouseButton,
        1003 => ManagedDecModeFlag.MouseAny,
        1004 => ManagedDecModeFlag.FocusEvents,
        1005 => ManagedDecModeFlag.MouseUtf8,
        1006 => ManagedDecModeFlag.MouseSgr,
        1007 => ManagedDecModeFlag.AlternateScroll,
        1015 => ManagedDecModeFlag.MouseUrxvt,
        1016 => ManagedDecModeFlag.MouseSgrPixels,
        1035 => ManagedDecModeFlag.NumLockKeypad,
        1036 => ManagedDecModeFlag.AltEscapePrefix,
        1039 => ManagedDecModeFlag.AltSendsEscape,
        1045 => ManagedDecModeFlag.ReverseWrapExtended,
        2026 => ManagedDecModeFlag.SynchronizedOutput,
        2027 => ManagedDecModeFlag.GraphemeClusters,
        2031 => ManagedDecModeFlag.ColorSchemeReports,
        2033 => ManagedDecModeFlag.VisibilityReports,
        2048 => ManagedDecModeFlag.SizeReports,
        5522 => ManagedDecModeFlag.KittyClipboard,
        _ => ManagedDecModeFlag.None,
    };
}
