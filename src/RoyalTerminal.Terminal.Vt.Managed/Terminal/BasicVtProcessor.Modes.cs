// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    // Stable Ghostty snapshot-v1 mode order, not native packed-struct layout.
    // The first four bits are ANSI KAM/IRM/SRM/LNM; remaining bits are DEC.
    private static ReadOnlySpan<int> SnapshotDecModes => TerminalModeRegistry.DecModes;

    internal const ulong SnapshotInitialModes = TerminalModeRegistry.InitialValues;

    private ulong _savedModeValues = SnapshotInitialModes;
    private ulong _defaultModeValues = SnapshotInitialModes;
    // Protocol mode values are independent of which buffer is actually active.
    // For example, resetting 47 after setting 1049 selects primary but leaves
    // the 1049 mode value set, just as Ghostty's ModeState does.
    private byte _alternateScreenModeBits;

    internal ulong SnapshotSavedModes => _savedModeValues;
    internal ulong SnapshotDefaultModes => _defaultModeValues;
    internal TerminalMouseModeState SnapshotMouseModes => _mouseModeState;

    // Only for unpublished snapshot assembly. Assign protocol bits, not VT
    // operations: no erase, resize, cursor save, screen switch, response or hold.
    internal void InstallSnapshotModes(Snapshots.GhosttySnapshotTerminalHeader header)
    {
        _extendedDecModesEnabled.EnsureCapacity(ExtendedDecModes.Length);
        ulong values = header.CurrentModes;
        for (int i = 0; i < TerminalModeRegistry.AnsiModes.Length; i++)
            SetPolicyModeValue(TerminalModeRegistry.AnsiModes[i], (values & (1UL << i)) != 0, ansi: true);
        for (int i = 0; i < SnapshotDecModes.Length; i++)
            SetPolicyModeValue(SnapshotDecModes[i], (values & (1UL << (i + 4))) != 0, ansi: false);
        _savedModeValues = header.SavedModes;
        _defaultModeValues = header.DefaultModes;
        _defaultCursorBlink = header.CursorDefaultBlink;
        // The header validates these enum registries, whose values match the
        // managed enums. Neither field is derived from the restored mode bank.
        _mouseModeState = new((TerminalMouseTrackingMode)header.MouseEvent, (TerminalMouseEncoding)header.MouseFormat);
        MouseShiftCaptureOverride = header.MouseShiftCapture;
    }

    /// <inheritdoc />
    public bool TrySetDefaultMode(int mode, bool enabled, bool ansi = false)
    {
        if (!TerminalModeRegistry.IsDefaultConfigurable(mode, ansi)) return false;
        TerminalModeState before = ModeState;
        ulong bit = 1UL << TerminalModeRegistry.IndexOf(mode, ansi);
        _defaultModeValues = enabled ? _defaultModeValues | bit : _defaultModeValues & ~bit;
        SetPolicyModeValue(mode, enabled, ansi);
        RaiseModeChangedIfNeeded(before);
        return true;
    }

    private void ApplyConfiguredModeDefaults()
    {
        // Reset already assigned the built-in defaults. The common no-policy
        // path does no additional work; only changed defaults need overriding.
        ulong differences = _defaultModeValues ^ SnapshotInitialModes;
        while (differences != 0)
        {
            int index = System.Numerics.BitOperations.TrailingZeroCount(differences);
            bool ansi = index < 4;
            int mode = ansi ? TerminalModeRegistry.AnsiModes[index] : SnapshotDecModes[index - 4];
            SetPolicyModeValue(mode, (_defaultModeValues & (1UL << index)) != 0, ansi);
            differences &= differences - 1;
        }
    }

    private void SetPolicyModeValue(int mode, bool enabled, bool ansi)
    {
        if (ansi) { HandleAnsiMode(mode, enabled); return; }
        // Do not call HandleDecMode: C mode_default writes bits only (notably
        // no pending-wrap reset for 7 or unsolicited size report for 2048).
        switch (mode)
        {
            case 1: _applicationCursorKeys = enabled; break;
            case 6: _originMode = enabled; break;
            case 7: _autoWrap = enabled; break;
            case 25: _cursorVisible = enabled; break;
            case 66: _applicationKeypad = enabled; break;
            case 67: _backarrowKeyMode = enabled; break;
            case 47: _alternateScreenModeBits = (byte)(enabled ? _alternateScreenModeBits | 1 : _alternateScreenModeBits & ~1); break;
            case 1047: _alternateScreenModeBits = (byte)(enabled ? _alternateScreenModeBits | 2 : _alternateScreenModeBits & ~2); break;
            case 1048: _saveCursorMode = enabled; break;
            case 1049: _alternateScreenModeBits = (byte)(enabled ? _alternateScreenModeBits | 4 : _alternateScreenModeBits & ~4); break;
            case 2004: _bracketedPaste = enabled; break;
            default: SetExtendedDecMode(mode, enabled); break;
        }
    }

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

    private void UpdateReportCellSize(int columns, int rows, uint widthPx, uint heightPx)
    {
        // Host cell geometry remains stable across VT-requested column changes.
        // Unknown geometry is zero, not an invented font size.
        _reportCellWidthPx = widthPx > 0 && columns > 0 ? Math.Max(1U, widthPx / (uint)columns) : 0;
        _reportCellHeightPx = heightPx > 0 && rows > 0 ? Math.Max(1U, heightPx / (uint)rows) : 0;
    }
}
