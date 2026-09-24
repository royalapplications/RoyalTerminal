// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Binary;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    // A screen switch copies the entering cursor but retains the departing pen.
    // Keep only the dormant fields not already represented by per-screen state.
    private GhosttySnapshotStyle _snapshotPrimaryPen, _snapshotAlternatePen;
    private bool _snapshotPrimaryProtected, _snapshotAlternateProtected;
    private int _snapshotPrimaryHyperlink, _snapshotAlternateHyperlink;

    private void CaptureDepartingSnapshotCursor()
    {
        if (_inAltScreen)
        {
            _snapshotAlternatePen = CaptureSnapshotPen();
            _snapshotAlternateProtected = _currentProtected;
            _snapshotAlternateHyperlink = 0; // switchScreen ends the departing link.
        }
        else
        {
            _snapshotPrimaryPen = CaptureSnapshotPen();
            _snapshotPrimaryProtected = _currentProtected;
            _snapshotPrimaryHyperlink = 0;
        }
    }

    private GhosttySnapshotStyle CaptureSnapshotPen()
    {
        TerminalCell cell = new()
        {
            ForegroundIdentity = GetColorIdentity(_currentFgKind, _currentFgPaletteIndex, _currentFg),
            BackgroundIdentity = CurrentBackgroundIdentity,
            UnderlineIdentity = _currentUnderlineIdentity, HasUnderlineColor = _currentHasUnderlineColor,
            Attributes = _currentAttrs, UnderlineStyle = _currentUnderlineStyle, Decorations = _currentDecorations,
        };
        return GhosttySnapshotLivePage.EncodeStyle(cell);
    }

    private void CaptureSnapshotTerminal(Stream output, bool alternateExists)
    {
        Span<byte> header = stackalloc byte[GhosttySnapshotTerminalHeader.Length];
        header.Clear();
        U16(header, 0, _screen.Columns); U16(header, 2, _screen.ViewportRows);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], _widthPx);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], _heightPx);
        U16(header, 12, _scrollTop); U16(header, 14, _scrollBottom);
        U16(header, 16, _scrollLeft); U16(header, 18, RightMargin);
        header[20] = _statusDisplay;
        U16(header, 21, _inAltScreen ? 1 : 0); U16(header, 23, alternateExists ? 2 : 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header[25..], _lastGraphicCodepoint < 0 ? uint.MaxValue : (uint)_lastGraphicCodepoint);
        header[29] = _cursorIsDefault ? (byte)1 : (byte)0;
        header[30] = SnapshotStyle(_defaultCursorStyle);
        header[31] = OptionalBool(_defaultCursorBlink);
        header[32] = (byte)_promptRedraw;
        header[33] = ModifyOtherKeys2 ? (byte)1 : (byte)0;
        header[34] = (byte)_mouseModeState.TrackingMode; header[35] = (byte)_mouseModeState.Encoding;
        header[36] = OptionalBool(MouseShiftCaptureOverride); header[37] = (byte)MouseShape;
        header[38] = PasswordInput ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt64LittleEndian(header[39..], SnapshotCurrentModes);
        BinaryPrimitives.WriteUInt64LittleEndian(header[47..], _savedModeValues);
        BinaryPrimitives.WriteUInt64LittleEndian(header[55..], _defaultModeValues);
        Dynamic(header[63..], _colors.GetSnapshotDynamic(11));
        Dynamic(header[71..], _colors.GetSnapshotDynamic(10));
        Dynamic(header[79..], _colors.GetSnapshotDynamic(12));
        // Preserve the explicit logical-page policy independently of the
        // managed host's hard row cap. Alignment is selected by the decoder.
        GhosttySnapshotScrollbackQuota? quota = _screen.SnapshotScrollbackQuota;
        BinaryPrimitives.WriteUInt64LittleEndian(header[87..], quota?.MaximumBytes ?? ulong.MaxValue);
        BinaryPrimitives.WriteUInt64LittleEndian(header[95..], quota is null ? (ulong)_screen.ScrollbackLimit : quota.MaximumRows ?? ulong.MaxValue);
        output.Write(header);
        byte[] tabs = new byte[(_screen.Columns + 7) / 8];
        foreach (int stop in _tabStops)
            if ((uint)stop < (uint)_screen.Columns) tabs[stop / 8] |= (byte)(1 << (stop % 8));
        output.Write(tabs);
        Span<byte> rgb = stackalloc byte[3];
        for (int i = 0; i < 256; i++) { Rgb(rgb, _colors.GetOriginalPalette(i)); output.Write(rgb); }
        Span<byte> mask = stackalloc byte[32]; mask.Clear();
        for (int i = 0; i < 256; i++) if (_colors.HasPaletteOverride(i)) mask[i / 8] |= (byte)(1 << (i % 8));
        output.Write(mask);
        for (int i = 0; i < 256; i++)
            if (_colors.HasPaletteOverride(i)) { Rgb(rgb, _colors.GetPalette(i)); output.Write(rgb); }
        WriteSnapshotString(output, _workingDirectory.Bytes); WriteSnapshotString(output, _title.Bytes);
    }

    private void CaptureSnapshotScreen(Stream output, int key, int pages, int historyRows, int maximumStringBytes)
    {
        bool active = (key == 1) == _inAltScreen;
        bool alternate = key == 1;
        Span<byte> header = stackalloc byte[GhosttySnapshotScreenState.HeaderLength]; header.Clear();
        U16(header, 0, key); U16(header, 2, pages);
        BinaryPrimitives.WriteUInt64LittleEndian(header[4..], (ulong)historyRows);
        U16(header, 12, active ? _cursorCol : alternate ? _savedAlternateCursorCol : _savedMainCursorCol);
        U16(header, 14, active ? _cursorRow : alternate ? _savedAlternateCursorRow : _savedMainCursorRow);
        header[16] = SnapshotStyle(alternate ? _alternateCursorStyle : _primaryCursorStyle);
        bool wrap = active ? _delayedWrap : alternate ? _savedAlternateDelayedWrap : _savedMainDelayedWrap;
        bool protect = active ? _currentProtected : alternate ? _snapshotAlternateProtected : _snapshotPrimaryProtected;
        SemanticPen semantic = alternate ? _alternateSemanticPen : _primarySemanticPen;
        header[17] = (byte)((wrap ? 1 : 0) | (protect ? 2 : 0) | ((int)semantic.Content << 2) | (semantic.ClearAtEndOfLine ? 16 : 0));
        (active ? CaptureSnapshotPen() : alternate ? _snapshotAlternatePen : _snapshotPrimaryPen).Write(header[18..]);
        BinaryPrimitives.WriteUInt32LittleEndian(header[34..], alternate ? _alternateHyperlinkImplicitCounter : _primaryHyperlinkImplicitCounter);
        U16(header, 38, active ? _charsets.Bits : alternate ? _alternateCharsets.Bits : _primaryCharsets.Bits);
        header[40] = (byte)(alternate ? _alternateProtectionMode : _primaryProtectionMode);
        ManagedKittyKeyboardState kitty = alternate ? _kittyKeyboardAlt : _kittyKeyboardMain;
        header[41] = (byte)kitty.Index; kitty.CopyFlagsTo(header[42..]);
        TerminalPromptClick click = (alternate ? _alternatePromptPolicy : _primaryPromptPolicy).Click;
        if (click != TerminalPromptClick.None)
        {
            header[50] = click <= TerminalPromptClick.Relative ? (byte)1 : (byte)2;
            header[51] = (byte)(click <= TerminalPromptClick.Relative ? click - TerminalPromptClick.Absolute : click - TerminalPromptClick.Line);
        }
        SavedCursorState? saved = alternate ? _alternateSavedCursor : _primarySavedCursor;
        header[52] = saved.HasValue ? (byte)1 : (byte)0;
        output.Write(header);
        if (saved is { } value)
        {
            TerminalCell pen = new()
            {
                ForegroundIdentity = value.Foreground, BackgroundIdentity = value.Background,
                UnderlineIdentity = value.Underline, HasUnderlineColor = value.HasUnderline,
                Attributes = value.Attributes, UnderlineStyle = value.UnderlineStyle, Decorations = value.Decorations,
            };
            GhosttySnapshotSavedCursor cursor = new(checked((ushort)value.Column), checked((ushort)value.Row),
                GhosttySnapshotLivePage.EncodeStyle(pen), (byte)((value.Protected ? 1 : 0) | (value.PendingWrap ? 2 : 0) | (value.Origin ? 4 : 0)),
                GhosttySnapshotCharset.Read(value.Charsets.Bits));
            Span<byte> encoded = stackalloc byte[GhosttySnapshotSavedCursor.Length]; cursor.Write(encoded); output.Write(encoded);
        }
        int linkToken = active ? _currentHyperlinkId : alternate ? _snapshotAlternateHyperlink : _snapshotPrimaryHyperlink;
        if (linkToken != 0 && _screen.TryGetHyperlink(linkToken, out TerminalHyperlink? link))
        {
            if ((long)link!.UriBytes.Length + link.ExplicitId.Length > maximumStringBytes)
                throw new InvalidDataException("Snapshot cursor hyperlink exceeds the string limit.");
            new GhosttySnapshotHyperlink(link!.IsExplicit, link.ImplicitId, link.ExplicitId, link.UriBytes).WriteTo(output);
        }
        else output.WriteByte(0);
    }

    private static byte SnapshotStyle(TerminalCursorStyle style) => style switch
    { TerminalCursorStyle.Bar => 0, TerminalCursorStyle.Block => 1, TerminalCursorStyle.Underline => 2, TerminalCursorStyle.BlockHollow => 3, _ => throw new InvalidDataException("Invalid cursor style.") };
    private static byte OptionalBool(bool? value) => value is null ? (byte)0 : value.Value ? (byte)2 : (byte)1;
    private static void U16(Span<byte> bytes, int offset, int value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes[offset..], checked((ushort)value));
    private static void Rgb(Span<byte> bytes, uint color) { bytes[0] = (byte)(color >> 16); bytes[1] = (byte)(color >> 8); bytes[2] = (byte)color; }
    private static void Dynamic(Span<byte> bytes, GhosttySnapshotDynamicColor color)
    {
        if (color.Default is { } value) { bytes[0] = 1; Rgb(bytes[1..], value); }
        if (color.Override is { } changed) { bytes[4] = 1; Rgb(bytes[5..], changed); }
    }
    private static void WriteSnapshotString(Stream output, ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)value.Length);
        output.Write(length); output.Write(value);
    }
}
