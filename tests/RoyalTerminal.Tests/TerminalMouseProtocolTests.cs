// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// Tests for terminal mouse mode tracking and protocol encoding.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalMouseProtocolTests
{
    [Fact]
    public void Tracker_SetAndResetModes_UpdatesSnapshot()
    {
        TerminalMouseModeTracker tracker = new();

        bool changed = tracker.Process("\x1b[?1000h"u8);
        Assert.True(changed);
        Assert.Equal(TerminalMouseTrackingMode.PressRelease, tracker.ModeState.TrackingMode);
        Assert.Equal(TerminalMouseEncoding.Default, tracker.ModeState.Encoding);

        changed = tracker.Process("\x1b[?1006h"u8);
        Assert.True(changed);
        Assert.Equal(TerminalMouseEncoding.Sgr, tracker.ModeState.Encoding);

        changed = tracker.Process("\x1b[?1016h"u8);
        Assert.True(changed);
        Assert.Equal(TerminalMouseEncoding.SgrPixels, tracker.ModeState.Encoding);

        changed = tracker.Process("\x1b[?1000l\x1b[?1006l\x1b[?1016l"u8);
        Assert.True(changed);
        Assert.Equal(TerminalMouseTrackingMode.None, tracker.ModeState.TrackingMode);
        Assert.Equal(TerminalMouseEncoding.Default, tracker.ModeState.Encoding);
    }

    [Fact]
    public void Tracker_SplitSequenceAcrossChunks_IsHandled()
    {
        TerminalMouseModeTracker tracker = new();

        Assert.False(tracker.Process("\x1b[?10"u8));
        Assert.True(tracker.Process("02h"u8));
        Assert.Equal(TerminalMouseTrackingMode.ButtonMotion, tracker.ModeState.TrackingMode);
    }

    [Fact]
    public void Tracker_X10Mode_SetAndReset_IsHandled()
    {
        TerminalMouseModeTracker tracker = new();

        bool changed = tracker.Process("\x1b[?9h"u8);
        Assert.True(changed);
        Assert.Equal(TerminalMouseTrackingMode.X10Press, tracker.ModeState.TrackingMode);
        Assert.Equal(TerminalMouseEncoding.Default, tracker.ModeState.Encoding);

        changed = tracker.Process("\x1b[?9l"u8);
        Assert.True(changed);
        Assert.Equal(TerminalMouseTrackingMode.None, tracker.ModeState.TrackingMode);
    }

    [Fact]
    public void Tracker_X10AndNormalTracking_FallsBackToX10_When1000Disabled()
    {
        TerminalMouseModeTracker tracker = new();

        tracker.Process("\x1b[?9h"u8);
        tracker.Process("\x1b[?1000h"u8);
        Assert.Equal(TerminalMouseTrackingMode.PressRelease, tracker.ModeState.TrackingMode);

        tracker.Process("\x1b[?1000l"u8);
        Assert.Equal(TerminalMouseTrackingMode.X10Press, tracker.ModeState.TrackingMode);
    }

    [Fact]
    public void Tracker_Ris_ResetsMouseModes()
    {
        TerminalMouseModeTracker tracker = new();
        tracker.Process("\x1b[?1003h"u8);
        tracker.Process("\x1b[?1006h"u8);
        Assert.Equal(TerminalMouseTrackingMode.AnyMotion, tracker.ModeState.TrackingMode);
        Assert.Equal(TerminalMouseEncoding.Sgr, tracker.ModeState.Encoding);

        bool changed = tracker.Process("\u001bc"u8);

        Assert.True(changed);
        Assert.Equal(TerminalMouseTrackingMode.None, tracker.ModeState.TrackingMode);
        Assert.Equal(TerminalMouseEncoding.Default, tracker.ModeState.Encoding);
    }

    [Fact]
    public void Encoder_DefaultPress_UsesLegacyByteProtocol()
    {
        TerminalMouseModeState mode = new(
            TerminalMouseTrackingMode.PressRelease,
            TerminalMouseEncoding.Default);
        TerminalPointerEvent pointerEvent = new(
            Kind: TerminalPointerEventKind.Button,
            X: 0,
            Y: 0,
            Button: TerminalMouseButton.Left,
            Action: TerminalInputAction.Press,
            Modifiers: TerminalModifiers.None);

        bool encoded = TerminalMouseProtocolEncoder.TryEncode(pointerEvent, mode, column: 10, row: 4, out byte[] sequence);

        Assert.True(encoded);
        Assert.Equal(new byte[] { 0x1B, (byte)'[', (byte)'M', 32, 42, 36 }, sequence);
    }

    [Fact]
    public void Encoder_Move_IsIgnoredWithoutMotionMode()
    {
        TerminalMouseModeState mode = new(
            TerminalMouseTrackingMode.PressRelease,
            TerminalMouseEncoding.Default);
        TerminalPointerEvent pointerEvent = new(
            Kind: TerminalPointerEventKind.Move,
            X: 0,
            Y: 0,
            Button: TerminalMouseButton.Left,
            Action: TerminalInputAction.Press,
            Modifiers: TerminalModifiers.None);

        bool encoded = TerminalMouseProtocolEncoder.TryEncode(pointerEvent, mode, column: 3, row: 2, out _);

        Assert.False(encoded);
    }

    [Fact]
    public void Encoder_SgrRelease_UsesReleasedButtonCodeAndLowercaseSuffix()
    {
        TerminalMouseModeState mode = new(
            TerminalMouseTrackingMode.PressRelease,
            TerminalMouseEncoding.Sgr);
        TerminalPointerEvent pointerEvent = new(
            Kind: TerminalPointerEventKind.Button,
            X: 0,
            Y: 0,
            Button: TerminalMouseButton.Left,
            Action: TerminalInputAction.Release,
            Modifiers: TerminalModifiers.None);

        bool encoded = TerminalMouseProtocolEncoder.TryEncode(pointerEvent, mode, column: 5, row: 7, out byte[] sequence);

        Assert.True(encoded);
        Assert.Equal("\x1b[<0;5;7m", Encoding.ASCII.GetString(sequence));
    }

    [Fact]
    public void Encoder_SgrButtonMotion_UsesMotionOffset()
    {
        TerminalMouseModeState mode = new(
            TerminalMouseTrackingMode.ButtonMotion,
            TerminalMouseEncoding.Sgr);
        TerminalPointerEvent pointerEvent = new(
            Kind: TerminalPointerEventKind.Move,
            X: 0,
            Y: 0,
            Button: TerminalMouseButton.Left,
            Action: TerminalInputAction.Press,
            Modifiers: TerminalModifiers.None);

        bool encoded = TerminalMouseProtocolEncoder.TryEncode(pointerEvent, mode, column: 8, row: 6, out byte[] sequence);

        Assert.True(encoded);
        Assert.Equal("\x1b[<32;8;6M", Encoding.ASCII.GetString(sequence));
    }

    [Fact]
    public void Encoder_SgrAnyMotionWithoutButton_UsesNoButtonMotionCode()
    {
        TerminalMouseModeState mode = new(
            TerminalMouseTrackingMode.AnyMotion,
            TerminalMouseEncoding.Sgr);
        TerminalPointerEvent pointerEvent = new(
            Kind: TerminalPointerEventKind.Move,
            X: 0,
            Y: 0,
            Button: TerminalMouseButton.None,
            Action: TerminalInputAction.Press,
            Modifiers: TerminalModifiers.None);

        bool encoded = TerminalMouseProtocolEncoder.TryEncode(pointerEvent, mode, column: 9, row: 6, out byte[] sequence);

        Assert.True(encoded);
        Assert.Equal("\x1b[<35;9;6M", Encoding.ASCII.GetString(sequence));
    }

    [Fact]
    public void Encoder_SgrPixels_UsesPixelCoordinates()
    {
        TerminalMouseModeState mode = new(
            TerminalMouseTrackingMode.PressRelease,
            TerminalMouseEncoding.SgrPixels);
        TerminalPointerEvent pointerEvent = new(
            Kind: TerminalPointerEventKind.Button,
            X: 40,
            Y: 80,
            Button: TerminalMouseButton.Left,
            Action: TerminalInputAction.Press,
            Modifiers: TerminalModifiers.None);

        bool encoded = TerminalMouseProtocolEncoder.TryEncode(
            pointerEvent,
            mode,
            column: 5,
            row: 7,
            pixelX: 41,
            pixelY: 81,
            out byte[] sequence);

        Assert.True(encoded);
        Assert.Equal("\x1b[<0;41;81M", Encoding.ASCII.GetString(sequence));
    }

    [Fact]
    public void BasicVtProcessor_SgrPixelsMouseMode_EncodesPointerFromManagedState()
    {
        TerminalScreen screen = new(80, 24, 0);
        BasicVtProcessor processor = new(screen);
        processor.Process("\x1b[?1000h\x1b[?1016h"u8);

        Assert.True(((ITerminalMouseReportingStateSource)processor).MouseReportingEnabled);

        TerminalPointerEvent pointerEvent = new(
            Kind: TerminalPointerEventKind.Button,
            X: 40,
            Y: 80,
            Button: TerminalMouseButton.Left,
            Action: TerminalInputAction.Press,
            Modifiers: TerminalModifiers.None);
        TerminalPointerEncodingContext context = new(
            ScreenWidthPx: 800,
            ScreenHeightPx: 384,
            CellWidthPx: 10,
            CellHeightPx: 16);

        bool encoded = processor.TryEncodePointer(pointerEvent, context, out byte[] sequence);

        Assert.True(encoded);
        Assert.Equal("\x1b[<0;41;81M", Encoding.ASCII.GetString(sequence));
    }

    [Fact]
    public void Encoder_UrxvtPress_UsesDecimalEncoding()
    {
        TerminalMouseModeState mode = new(
            TerminalMouseTrackingMode.PressRelease,
            TerminalMouseEncoding.Urxvt);
        TerminalPointerEvent pointerEvent = new(
            Kind: TerminalPointerEventKind.Button,
            X: 0,
            Y: 0,
            Button: TerminalMouseButton.Left,
            Action: TerminalInputAction.Press,
            Modifiers: TerminalModifiers.None);

        bool encoded = TerminalMouseProtocolEncoder.TryEncode(pointerEvent, mode, column: 10, row: 4, out byte[] sequence);

        Assert.True(encoded);
        Assert.Equal("\x1b[32;10;4M", Encoding.ASCII.GetString(sequence));
    }

    [Fact]
    public void Encoder_DefaultProtocol_ClampsCoordinatesToLegacyLimit()
    {
        TerminalMouseModeState mode = new(
            TerminalMouseTrackingMode.PressRelease,
            TerminalMouseEncoding.Default);
        TerminalPointerEvent pointerEvent = new(
            Kind: TerminalPointerEventKind.Button,
            X: 0,
            Y: 0,
            Button: TerminalMouseButton.Right,
            Action: TerminalInputAction.Press,
            Modifiers: TerminalModifiers.None);

        bool encoded = TerminalMouseProtocolEncoder.TryEncode(pointerEvent, mode, column: 500, row: 400, out byte[] sequence);

        Assert.True(encoded);
        Assert.Equal(255, sequence[4]);
        Assert.Equal(255, sequence[5]);
    }

    [Fact]
    public void Encoder_X10Mode_ReportsPressOnly()
    {
        TerminalMouseModeState mode = new(
            TerminalMouseTrackingMode.X10Press,
            TerminalMouseEncoding.Default);

        TerminalPointerEvent press = new(
            Kind: TerminalPointerEventKind.Button,
            X: 0,
            Y: 0,
            Button: TerminalMouseButton.Left,
            Action: TerminalInputAction.Press,
            Modifiers: TerminalModifiers.None);
        TerminalPointerEvent release = new(
            Kind: TerminalPointerEventKind.Button,
            X: 0,
            Y: 0,
            Button: TerminalMouseButton.Left,
            Action: TerminalInputAction.Release,
            Modifiers: TerminalModifiers.None);
        TerminalPointerEvent wheel = new(
            Kind: TerminalPointerEventKind.Scroll,
            X: 0,
            Y: 0,
            Button: TerminalMouseButton.None,
            Action: TerminalInputAction.Press,
            Modifiers: TerminalModifiers.None,
            DeltaX: 0,
            DeltaY: 1);

        Assert.True(TerminalMouseProtocolEncoder.TryEncode(press, mode, column: 2, row: 3, out _));
        Assert.False(TerminalMouseProtocolEncoder.TryEncode(release, mode, column: 2, row: 3, out _));
        Assert.False(TerminalMouseProtocolEncoder.TryEncode(wheel, mode, column: 2, row: 3, out _));
    }

    [Fact]
    public void Encoder_X10Mode_DoesNotEncodeModifierBits()
    {
        TerminalMouseModeState mode = new(
            TerminalMouseTrackingMode.X10Press,
            TerminalMouseEncoding.Default);
        TerminalPointerEvent pointerEvent = new(
            Kind: TerminalPointerEventKind.Button,
            X: 0,
            Y: 0,
            Button: TerminalMouseButton.Left,
            Action: TerminalInputAction.Press,
            Modifiers: TerminalModifiers.Shift | TerminalModifiers.Control);

        bool encoded = TerminalMouseProtocolEncoder.TryEncode(pointerEvent, mode, column: 10, row: 4, out byte[] sequence);

        Assert.True(encoded);
        Assert.Equal(new byte[] { 0x1B, (byte)'[', (byte)'M', 32, 42, 36 }, sequence);
    }
}
