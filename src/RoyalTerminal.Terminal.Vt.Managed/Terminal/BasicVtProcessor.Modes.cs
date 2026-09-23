// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    // Stable Ghostty snapshot-v1 mode order, not native packed-struct layout.
    // The first four bits are ANSI KAM/IRM/SRM/LNM; remaining bits are DEC.
    private static ReadOnlySpan<int> SnapshotDecModes =>
    [
        1, 3, 4, 5, 6, 7, 8, 9, 12, 25, 40, 45, 47, 66, 67, 69,
        1000, 1002, 1003, 1004, 1005, 1006, 1007, 1015, 1016, 1035,
        1036, 1039, 1045, 1047, 1048, 1049, 2004, 2026, 2027, 2031,
        2033, 2048, 5522,
    ];

    internal const ulong SnapshotInitialModes = (1UL << 2) | (1UL << 9) |
        (1UL << 13) | (1UL << 26) | (1UL << 29) | (1UL << 30);

    private ulong _savedModeValues = SnapshotInitialModes;
    // Protocol mode values are independent of which buffer is actually active.
    // For example, resetting 47 after setting 1049 selects primary but leaves
    // the 1049 mode value set, just as Ghostty's ModeState does.
    private byte _alternateScreenModeBits;

    internal ulong SnapshotSavedModes => _savedModeValues;

    internal ulong SnapshotCurrentModes
    {
        get
        {
            ulong result = (_keyboardLocked ? 1UL : 0) | (_insertMode ? 2UL : 0) |
                (_sendReceiveMode ? 4UL : 0) | (_lineFeedNewLineMode ? 8UL : 0);
            ReadOnlySpan<int> modes = SnapshotDecModes;
            for (int index = 0; index < modes.Length; index++)
                if (GetDecPrivateModeReportStatus(modes[index]) == 1) result |= 1UL << (index + 4);
            return result;
        }
    }

    private void SaveOrRestoreDecModes(bool restore)
    {
        foreach (int mode in _params)
        {
            int index = SnapshotDecModes.IndexOf(mode);
            if (index < 0) continue; // Unknown/permanently reset/host-only modes are not in Ghostty's registry.
            ulong bit = 1UL << (index + 4);
            if (restore)
            {
                // Restore is not a pop: repeated restores use the last saved
                // value. Invoke the normal setter even if unchanged so origin,
                // margins, cursor save and screen-switch side effects run.
                HandleDecMode(mode, (_savedModeValues & bit) != 0);
            }
            else if (GetDecPrivateModeReportStatus(mode) == 1) _savedModeValues |= bit;
            else _savedModeValues &= ~bit;
        }
    }

    private void ResetSavedAndScreenModes()
    {
        _savedModeValues = SnapshotInitialModes;
        _alternateScreenModeBits = 0;
    }

    private void SetColumnMode(bool enabled)
    {
        if (!_extendedDecModesEnabled.Contains(40))
        {
            SetExtendedDecMode(3, false);
            return;
        }
        SetExtendedDecMode(3, enabled);
        // A VT-requested resize retains pixel geometry and does not emit the
        // host resize notification (Ghostty Terminal.deccolm/Handler.setMode).
        ResizeScreenCore(enabled ? 132 : 80, _screen.ViewportRows, _widthPx, _heightPx,
            reflowOnResize: true, Span<RoyalTerminal.Avalonia.Rendering.TerminalGridPosition>.Empty,
            preserveViewportTopOnRowsIncrease: false, reportSize: false);
        EraseInDisplay(2);
        HomeCursor();
    }
}
