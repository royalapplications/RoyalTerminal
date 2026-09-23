// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    /// <inheritdoc />
    public bool IsKeyEncodingAuthoritative => true;

    /// <inheritdoc />
    public bool ModifyOtherKeys2 { get; private set; }

    /// <inheritdoc />
    public bool TryEncodeKey(in TerminalKeyEncodingRequest request, out byte[] sequence)
    {
        sequence = [];
        if (!ManagedKittyKeyEncoder.IsSupportedKey(request.KeyId) ||
            request.Action is not (TerminalInputAction.Press or TerminalInputAction.Repeat or TerminalInputAction.Release) ||
            request.UnshiftedCodepoint != 0 && !System.Text.Rune.IsValid(request.UnshiftedCodepoint)) return false;
        // Kitty takes precedence over the legacy extension.
        if (KittyKeyboardFlags != 0) return ManagedKittyKeyEncoder.TryEncode(request, KittyKeyboardFlags, out sequence);
        return ManagedLegacyKeyEncoder.TryEncode(request, ApplicationCursorKeys, ApplicationKeypad,
            _extendedDecModesEnabled.Contains(1035), _extendedDecModesEnabled.Contains(1036),
            _backarrowKeyMode, ModifyOtherKeys2, out sequence);
    }

    private void SetModifyKeyFormat(char finalByte)
    {
        // Ghostty accepts colon separators here (as for all CSI m forms).
        // CSI > n ignores its parameters, reverting to numeric-except mode.
        if (finalByte == 'n') { ModifyOtherKeys2 = false; return; }
        if (_params.Count > 2) return;
        int resource = _params.Count == 0 ? 0 : _params[0];
        if (resource is not (0 or 1 or 2 or 4)) return;
        ModifyOtherKeys2 = resource == 4 && _params.Count == 2 && _params[1] == 2;
    }
}
