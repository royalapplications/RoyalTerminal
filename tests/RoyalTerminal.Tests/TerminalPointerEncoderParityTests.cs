// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalPointerEncoderParityTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Modes()
    {
        for (int tracking = 0; tracking <= 4; tracking++)
        for (int format = 0; format <= 4; format++) yield return [tracking, format];
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void StatefulSequencesAndGeometryMatchIndependentNativeEncoder(int tracking, int format)
    {
        if (!Available()) return;
        using GhosttyVtProcessor native = new(new TerminalScreen(8, 3));
        using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
        using GhosttyMouseEncoder oracle = new();
        using GhosttyMouseEvent evt = new();
        oracle.SetTrackingMode((GhosttyVtNative.GhosttyMouseTrackingMode)tracking);
        oracle.SetFormat((GhosttyVtNative.GhosttyMouseFormat)format);
        oracle.SetTrackLastCell(true);
        byte[] modes = ModeBytes(tracking, format); native.Process(modes); managed.Process(modes);
        TerminalPointerEncodingContext[] contexts = [new(80, 60, 10, 20), new(83, 67, 10, 20, 5, 2, 4, 3),
            new(5000, 4000, 10, 20), new(1, 1, 10, 20), new(20, 20, 10, 20, 40, 40, 40, 40),
            new(80, 60, 10, 20, -1, -2, -3, -4)];
        byte pressed = 0;
        foreach (TerminalPointerEncodingContext context in contexts)
        {
            oracle.SetSize(Size(context));
            TerminalPointerEvent[] events = [
                Pointer(TerminalPointerEventKind.Move, -1, -1, TerminalMouseButton.Left),
                Pointer(TerminalPointerEventKind.Move, 1.1, 1.2),
                Pointer(TerminalPointerEventKind.Move, 1.4, 1.4),
                Pointer(TerminalPointerEventKind.Button, 1, 1, TerminalMouseButton.Left),
                Pointer(TerminalPointerEventKind.Move, 1.9, 1.9, TerminalMouseButton.Left),
                Pointer(TerminalPointerEventKind.Move, 21, 41, TerminalMouseButton.Left),
                Pointer(TerminalPointerEventKind.Move, 21, 41, TerminalMouseButton.Left) with { Modifiers = TerminalModifiers.Shift | TerminalModifiers.Control },
                Pointer(TerminalPointerEventKind.Move, -1.5, -2.5, TerminalMouseButton.Left),
                Pointer(TerminalPointerEventKind.Button, -1.5, -2.5, TerminalMouseButton.Left, TerminalInputAction.Release),
                Pointer(TerminalPointerEventKind.Move, -1.5, -2.5),
                Pointer(TerminalPointerEventKind.Button, -1, -1, TerminalMouseButton.Right),
                Pointer(TerminalPointerEventKind.Button, context.ScreenWidthPx + 1.5, context.ScreenHeightPx + 1.5, TerminalMouseButton.Right, TerminalInputAction.Release),
                Pointer(TerminalPointerEventKind.Button, context.ScreenWidthPx, context.ScreenHeightPx, TerminalMouseButton.Middle),
                Pointer(TerminalPointerEventKind.Button, 0, 0, TerminalMouseButton.Middle, TerminalInputAction.Release),
                Pointer(TerminalPointerEventKind.Button, 2220, 1, TerminalMouseButton.Left),
                Pointer(TerminalPointerEventKind.Button, 2230, 1, TerminalMouseButton.Left),
                Pointer(TerminalPointerEventKind.Move, 2231, 1, TerminalMouseButton.Left),
                Pointer(TerminalPointerEventKind.Button, 2231, 1, TerminalMouseButton.Left, TerminalInputAction.Release),
                Pointer(TerminalPointerEventKind.Scroll, 10.5, 20.5) with { DeltaY = 1, DeltaX = -1 },
                Pointer(TerminalPointerEventKind.Scroll, 10.5, 20.5) with { DeltaY = -1 },
                Pointer(TerminalPointerEventKind.Scroll, 10.5, 20.5) with { DeltaX = -1 },
                Pointer(TerminalPointerEventKind.Scroll, 10.5, 20.5) with { DeltaX = 1 },
                Pointer(TerminalPointerEventKind.Move, 10.4999999999, 20.4999999999),
                Pointer(TerminalPointerEventKind.Move, 10.5000000001, 20.5000000001),
            ];
            foreach (TerminalPointerEvent pointer in events)
            {
                byte[] expected = OracleEncode(oracle, evt, pointer, ref pressed);
                bool nativeSent = native.TryEncodePointer(pointer, context, out byte[] nativeBytes);
                bool managedSent = managed.TryEncodePointer(pointer, context, out byte[] managedBytes);
                Assert.Equal(expected.Length != 0, nativeSent); Assert.Equal(expected.Length != 0, managedSent);
                Assert.Equal(expected, nativeBytes); Assert.Equal(expected, managedBytes);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedMotionIsSuppressedWithoutAllocationsButPixelMotionIsNot(bool native)
    {
        if (native && !Available()) return;
        using IVtProcessor processor = native ? new GhosttyVtProcessor(new TerminalScreen(8, 3)) : new BasicVtProcessor(new TerminalScreen(8, 3));
        ITerminalPointerSequenceEncoderSource encoder = (ITerminalPointerSequenceEncoderSource)processor;
        TerminalPointerEncodingContext context = new(80, 60, 10, 20);
        TerminalPointerEvent pointer = Pointer(TerminalPointerEventKind.Move, 1, 1);
        processor.Process("\u001b[?1003;1006h"u8);
        Assert.True(encoder.TryEncodePointer(pointer, context, out _));
        for (int i = 0; i < 1000; i++) encoder.TryEncodePointer(pointer, context, out _);
        int reports = 0; long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) if (encoder.TryEncodePointer(pointer, context, out _)) reports++;
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before); Assert.Equal(0, reports);
        processor.Process("\u001b[?1016h"u8);
        for (int i = 0; i < 3; i++)
        {
            Assert.True(encoder.TryEncodePointer(pointer, context, out byte[] bytes));
            Assert.Equal("\u001b[<35;1;1M", Encoding.ASCII.GetString(bytes));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void CellProtocolsAllowFarNegativeReleaseCoordinates(int format)
    {
        if (!Available()) return;
        using GhosttyVtProcessor native = new(new TerminalScreen(8, 3));
        using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
        using GhosttyMouseEncoder oracle = new(); using GhosttyMouseEvent evt = new();
        byte[] mode = ModeBytes(2, format); native.Process(mode); managed.Process(mode);
        oracle.SetTrackingMode(GhosttyVtNative.GhosttyMouseTrackingMode.Normal);
        oracle.SetFormat((GhosttyVtNative.GhosttyMouseFormat)format);
        TerminalPointerEncodingContext context = new(80, 60, 10, 20); oracle.SetSize(Size(context));
        TerminalPointerEvent pointer = Pointer(TerminalPointerEventKind.Button, -1e10, -1e10, TerminalMouseButton.Left, TerminalInputAction.Release);
        byte pressed = 0; byte[] expected = OracleEncode(oracle, evt, pointer, ref pressed);
        Assert.NotEmpty(expected);
        Assert.True(native.TryEncodePointer(pointer, context, out byte[] nativeBytes));
        Assert.True(managed.TryEncodePointer(pointer, context, out byte[] managedBytes));
        Assert.Equal(expected, nativeBytes); Assert.Equal(expected, managedBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeometryModesResizeAndSessionResetsInvalidateMotionHistory(bool native)
    {
        if (native && !Available()) return;
        using IVtProcessor processor = native ? new GhosttyVtProcessor(new TerminalScreen(8, 3)) : new BasicVtProcessor(new TerminalScreen(8, 3));
        ITerminalPointerSequenceEncoderSource encoder = (ITerminalPointerSequenceEncoderSource)processor;
        TerminalPointerEncodingContext context = new(80, 60, 10, 20);
        TerminalPointerEvent pointer = Pointer(TerminalPointerEventKind.Move, 1, 1);
        processor.Process("\u001b[?1003;1006h"u8);
        void FirstThenDuplicate()
        {
            Assert.True(encoder.TryEncodePointer(pointer, context, out _));
            Assert.False(encoder.TryEncodePointer(pointer, context, out _));
        }
        FirstThenDuplicate();
        context = context with { PaddingLeftPx = 1 }; FirstThenDuplicate();
        processor.Process("\u001b[?1015h"u8); FirstThenDuplicate();
        processor.NotifyResize(8, 3); FirstThenDuplicate();
        processor.NotifyResize(8, 3, 80, 60); FirstThenDuplicate();
        processor.Reset(); processor.Process("\u001b[?1003;1006h"u8); FirstThenDuplicate();
        foreach (bool preserve in new[] { false, true })
        {
            ((ITerminalSessionHistoryController)processor).PrepareForNewSession(preserve);
            processor.Process("\u001b[?1003;1006h"u8); FirstThenDuplicate();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidHostGeometryIsRejectedBeforeNativeConversions(bool native)
    {
        if (native && !Available()) return;
        using IVtProcessor processor = native ? new GhosttyVtProcessor(new TerminalScreen(8, 3)) : new BasicVtProcessor(new TerminalScreen(8, 3));
        processor.Process("\u001b[?1003;1016h"u8);
        ITerminalPointerSequenceEncoderSource encoder = (ITerminalPointerSequenceEncoderSource)processor;
        TerminalPointerEncodingContext context = new(80, 60, 10, 20);
        foreach (double x in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.MaxValue, -double.MaxValue, 1e10, -1e10 })
        {
            Assert.False(encoder.TryEncodePointer(Pointer(TerminalPointerEventKind.Button, x, 0, TerminalMouseButton.Left), context, out byte[] bytes));
            Assert.Empty(bytes);
        }
        TerminalPointerEvent pointer = Pointer(TerminalPointerEventKind.Button, 1, 1, TerminalMouseButton.Left);
        foreach (TerminalPointerEncodingContext invalid in new[] { context with { ScreenWidthPx = 0 }, context with { ScreenHeightPx = -1 },
            context with { CellWidthPx = 0 }, context with { CellHeightPx = -1 }, context with { ScreenWidthPx = int.MaxValue, CellWidthPx = 1 } })
        {
            Assert.False(encoder.TryEncodePointer(pointer, invalid, out byte[] bytes)); Assert.Empty(bytes);
        }
        Assert.True(encoder.TryEncodePointer(pointer, context, out _));
    }

    private static TerminalPointerEvent Pointer(TerminalPointerEventKind kind, double x, double y,
        TerminalMouseButton button = TerminalMouseButton.None, TerminalInputAction action = TerminalInputAction.Press)
        => new(kind, x, y, button, action, TerminalModifiers.None);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnencodableUtf8CoordinateDoesNotMutateMotionHistory(bool native)
    {
        if (native && !Available()) return;
        using IVtProcessor processor = native ? new GhosttyVtProcessor(new TerminalScreen(8, 3)) : new BasicVtProcessor(new TerminalScreen(8, 3));
        ITerminalPointerSequenceEncoderSource encoder = (ITerminalPointerSequenceEncoderSource)processor;
        processor.Process("\u001b[?1003;1005h"u8);
        TerminalPointerEncodingContext context = new(60000, 60, 1, 20);
        TerminalPointerEvent pointer = Pointer(TerminalPointerEventKind.Move, 1, 1);
        Assert.True(encoder.TryEncodePointer(pointer, context, out _));
        // 55263 + 1 (wire cell) + 32 would be U+D800: native's utf8Encode
        // rejects that scalar. Guard it before calling native or changing state.
        Assert.False(encoder.TryEncodePointer(pointer with { X = 55263 }, context, out byte[] bytes));
        Assert.Empty(bytes);
        Assert.False(encoder.TryEncodePointer(pointer, context, out _));
    }

    private static byte[] ModeBytes(int tracking, int format)
    {
        int[] trackingModes = [9, 9, 1000, 1002, 1003], formats = [1006, 1005, 1006, 1015, 1016];
        return Encoding.ASCII.GetBytes($"\u001b[?{trackingModes[tracking]}{(tracking == 0 ? 'l' : 'h')}\u001b[?{formats[format]}{(format == 0 ? 'l' : 'h')}");
    }

    private static GhosttyVtNative.GhosttyMouseEncoderSize Size(TerminalPointerEncodingContext context) => new()
    {
        Size = (nuint)System.Runtime.CompilerServices.Unsafe.SizeOf<GhosttyVtNative.GhosttyMouseEncoderSize>(),
        ScreenWidth = (uint)context.ScreenWidthPx, ScreenHeight = (uint)context.ScreenHeightPx,
        CellWidth = (uint)context.CellWidthPx, CellHeight = (uint)context.CellHeightPx,
        PaddingLeft = (uint)Math.Max(0, context.PaddingLeftPx), PaddingRight = (uint)Math.Max(0, context.PaddingRightPx),
        PaddingTop = (uint)Math.Max(0, context.PaddingTopPx), PaddingBottom = (uint)Math.Max(0, context.PaddingBottomPx),
    };

    private static byte[] OracleEncode(GhosttyMouseEncoder encoder, GhosttyMouseEvent evt, TerminalPointerEvent pointer, ref byte pressed)
    {
        byte mask = pointer.Button switch { TerminalMouseButton.Left => 1, TerminalMouseButton.Middle => 2, TerminalMouseButton.Right => 4, _ => 0 };
        if (pointer.Kind == TerminalPointerEventKind.Button)
            pressed = pointer.Action == TerminalInputAction.Release ? (byte)(pressed & ~mask) : (byte)(pressed | mask);
        encoder.SetAnyButtonPressed(pressed != 0 || (pointer.Kind == TerminalPointerEventKind.Move && mask != 0));
        evt.SetPosition((float)pointer.X, (float)pointer.Y);
        GhosttyVtNative.GhosttyVtMods mods = 0;
        if ((pointer.Modifiers & TerminalModifiers.Shift) != 0) mods |= GhosttyVtNative.GhosttyVtMods.Shift;
        if ((pointer.Modifiers & TerminalModifiers.Control) != 0) mods |= GhosttyVtNative.GhosttyVtMods.Ctrl;
        evt.SetModifiers(mods);
        evt.SetAction(pointer.Kind == TerminalPointerEventKind.Move ? GhosttyVtNative.GhosttyMouseAction.Motion :
            pointer.Kind == TerminalPointerEventKind.Button && pointer.Action == TerminalInputAction.Release ? GhosttyVtNative.GhosttyMouseAction.Release : GhosttyVtNative.GhosttyMouseAction.Press);
        if (pointer.Kind == TerminalPointerEventKind.Scroll)
            evt.SetButton(pointer.DeltaY > 0 ? GhosttyVtNative.GhosttyMouseButtonId.Four : pointer.DeltaY < 0 ? GhosttyVtNative.GhosttyMouseButtonId.Five :
                pointer.DeltaX < 0 ? GhosttyVtNative.GhosttyMouseButtonId.Six : GhosttyVtNative.GhosttyMouseButtonId.Seven);
        else if (pointer.Button == TerminalMouseButton.None) evt.ClearButton();
        else evt.SetButton(pointer.Button switch { TerminalMouseButton.Left => GhosttyVtNative.GhosttyMouseButtonId.Left,
            TerminalMouseButton.Middle => GhosttyVtNative.GhosttyMouseButtonId.Middle, _ => GhosttyVtNative.GhosttyMouseButtonId.Right });
        return encoder.Encode(evt);
    }

    private bool Available()
    {
        bool available = GhosttyVtProcessor.IsAvailable(); output.WriteLine($"Native pointer encoder available: {available}"); return available;
    }
}
