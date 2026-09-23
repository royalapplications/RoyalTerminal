// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Text;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.Terminal;

public sealed partial class GhosttyVtProcessor
{
    private ulong _defaultModeValues = TerminalModeRegistry.InitialValues;

    /// <inheritdoc />
    public bool TrySetDefaultMode(int mode, bool enabled, bool ansi = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!TerminalModeRegistry.IsDefaultConfigurable(mode, ansi)) return false;
        TerminalModeState before = ModeState;
        _terminal.SetDefaultMode(GhosttyVtNative.CreateMode((ushort)mode, ansi), enabled);
        ulong bit = 1UL << TerminalModeRegistry.IndexOf(mode, ansi);
        _defaultModeValues = enabled ? _defaultModeValues | bit : _defaultModeValues & ~bit;
        _sixelOverlayProcessor?.TrySetDefaultMode(mode, enabled, ansi);
        RefreshStateAndScreenFromNative();
        RaiseModeChangedIfNeeded(before);
        return true;
    }

    private void ApplyConfiguredModeDefaultsAfterSessionReset()
    {
        // The history-preserving reset cannot call RIS. Reset the saved DEC
        // bank to the native initial bank without executing transition effects:
        // C mode writes change only bits, and XTSAVE only copies those bits.
        Span<byte> save = stackalloc byte[12];
        save[0] = 0x1B; save[1] = (byte)'['; save[2] = (byte)'?';
        foreach (int mode in TerminalModeRegistry.DecModes)
        {
            GhosttyVtNative.GhosttyMode tag = GhosttyVtNative.CreateMode((ushort)mode, ansi: false);
            bool current = _terminal.GetMode(tag);
            int index = TerminalModeRegistry.IndexOf(mode, false);
            _terminal.SetMode(tag, (TerminalModeRegistry.InitialValues & (1UL << index)) != 0);
            try
            {
                Utf8Formatter.TryFormat(mode, save[3..], out int written);
                save[3 + written] = (byte)'s';
                _terminal.Write(save[..(4 + written)]);
            }
            finally { _terminal.SetMode(tag, current); }
            if (TerminalModeRegistry.IsDefaultConfigurable(mode, false))
                _terminal.SetMode(tag, (_defaultModeValues & (1UL << index)) != 0);
        }
        foreach (int mode in TerminalModeRegistry.AnsiModes)
            _terminal.SetMode(GhosttyVtNative.CreateMode((ushort)mode, ansi: true),
                (_defaultModeValues & (1UL << TerminalModeRegistry.IndexOf(mode, true))) != 0);
    }

    private void ConfigureOverlayModeDefaults(BasicVtProcessor overlay)
    {
        foreach (int mode in TerminalModeRegistry.AnsiModes)
            overlay.TrySetDefaultMode(mode,
                (_defaultModeValues & (1UL << TerminalModeRegistry.IndexOf(mode, true))) != 0, ansi: true);
        foreach (int mode in TerminalModeRegistry.DecModes)
            if (TerminalModeRegistry.IsDefaultConfigurable(mode, false))
                overlay.TrySetDefaultMode(mode,
                    (_defaultModeValues & (1UL << TerminalModeRegistry.IndexOf(mode, false))) != 0);
    }
}
