// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.IntegrationTests.TestInfrastructure;
using Xunit;
using GhosttySelectionSnapshot = RoyalTerminal.GhosttySharp.GhosttySelection;

namespace RoyalTerminal.IntegrationTests;

public class GhosttySelectionTests
{
    [GhosttyNativeFact]
    public void SelectionHelpers_DeriveStoreFormatAndCompareSnapshots()
    {
        using GhosttyTerminal terminal = new(80, 24);
        terminal.Write("  Hello  \r\nWorld"u8);

        Assert.True(
            terminal.TryGetGridReference(
                GhosttyVtNative.GhosttyPoint.Active(3, 0),
                out GhosttyVtNative.GhosttyGridRef wordReference));
        Assert.True(terminal.TrySelectWord(in wordReference, [], out GhosttySelectionSnapshot word));
        Assert.True(
            terminal.TryFormatSelection(
                GhosttyVtNative.GhosttyFormatterFormat.Plain,
                word,
                unwrap: false,
                trim: false,
                out byte[] formattedWord));
        Assert.Equal("Hello", Encoding.UTF8.GetString(formattedWord));

        Assert.True(
            terminal.TryGetGridReference(
                GhosttyVtNative.GhosttyPoint.Active(20, 1),
                out GhosttyVtNative.GhosttyGridRef betweenStart));
        Assert.True(
            terminal.TryGetGridReference(
                GhosttyVtNative.GhosttyPoint.Active(0, 1),
                out GhosttyVtNative.GhosttyGridRef betweenEnd));
        Assert.True(
            terminal.TrySelectWordBetween(
                in betweenStart,
                in betweenEnd,
                [],
                out GhosttySelectionSnapshot between));
        Assert.True(
            terminal.TryFormatSelection(
                GhosttyVtNative.GhosttyFormatterFormat.Plain,
                between,
                unwrap: false,
                trim: false,
                out byte[] formattedBetween));
        Assert.Equal("World", Encoding.UTF8.GetString(formattedBetween));

        Assert.True(
            terminal.TryGetGridReference(
                GhosttyVtNative.GhosttyPoint.Active(0, 0),
                out GhosttyVtNative.GhosttyGridRef lineReference));
        Assert.True(
            terminal.TrySelectLine(
                in lineReference,
                [],
                semanticPromptBoundary: false,
                out GhosttySelectionSnapshot line));
        Assert.True(terminal.SelectionsEqual(word, line));
        Assert.False(terminal.TrySelectOutput(in lineReference, out _));

        terminal.SetSelection(word);
        Assert.True(terminal.TryGetSelection(out GhosttySelectionSnapshot stored));
        Assert.True(terminal.SelectionsEqual(word, stored));
        Assert.True(
            terminal.SelectionContains(
                stored,
                GhosttyVtNative.GhosttyPoint.Active(4, 0)));
        Assert.False(
            terminal.SelectionContains(
                stored,
                GhosttyVtNative.GhosttyPoint.Active(0, 1)));
        Assert.Equal(
            GhosttyVtNative.GhosttySelectionOrder.Forward,
            terminal.GetSelectionOrder(stored));
        Assert.True(
            terminal.SelectionsEqual(
                stored,
                terminal.OrderSelection(
                    stored,
                    GhosttyVtNative.GhosttySelectionOrder.Forward)));

        Assert.True(terminal.TrySelectAll(out GhosttySelectionSnapshot all));
        Assert.False(terminal.SelectionsEqual(word, all));
        Assert.False(
            terminal.SelectionsEqual(
                word,
                terminal.AdjustSelection(
                    word,
                    GhosttyVtNative.GhosttySelectionAdjust.Right)));

        terminal.SetSelection(null);
        Assert.False(terminal.TryGetSelection(out _));
        Assert.False(
            terminal.TryFormatSelection(
                GhosttyVtNative.GhosttyFormatterFormat.Plain,
                selection: null,
                unwrap: false,
                trim: false,
                out _));
    }

    [GhosttyNativeFact]
    public void SelectionGesture_PressUsesConfiguredWordBehavior()
    {
        using GhosttyTerminal terminal = new(5, 2);
        using GhosttySelectionGesture gesture = new();
        using GhosttySelectionGestureEvent press =
            new(GhosttyVtNative.GhosttySelectionGestureEventType.Press);

        terminal.Write("abc"u8);
        Assert.True(
            terminal.TryGetGridReference(
                GhosttyVtNative.GhosttyPoint.Active(1, 0),
                out GhosttyVtNative.GhosttyGridRef reference));

        press.SetReference(reference);
        press.SetBehaviors(
            new GhosttyVtNative.GhosttySelectionGestureBehaviors
            {
                SingleClick = GhosttyVtNative.GhosttySelectionGestureBehavior.Word,
                DoubleClick = GhosttyVtNative.GhosttySelectionGestureBehavior.Word,
                TripleClick = GhosttyVtNative.GhosttySelectionGestureBehavior.Line,
            });

        Assert.True(gesture.TryApply(terminal, press, out GhosttySelectionSnapshot selection));
        Assert.Equal((byte)1, gesture.GetClickCount(terminal));
        Assert.Equal(
            GhosttyVtNative.GhosttySelectionGestureBehavior.Word,
            gesture.GetBehavior(terminal));
        GhosttySelectionGestureSnapshot snapshot = gesture.GetSnapshot(terminal);
        Assert.Equal((byte)1, snapshot.ClickCount);
        Assert.False(snapshot.Dragged);
        Assert.Equal(
            GhosttyVtNative.GhosttySelectionGestureAutoscroll.None,
            snapshot.Autoscroll);
        Assert.Equal(
            GhosttyVtNative.GhosttySelectionGestureBehavior.Word,
            snapshot.Behavior);
        Assert.True(gesture.TryGetAnchor(terminal, out _));
        Assert.True(
            terminal.TryFormatSelection(
                GhosttyVtNative.GhosttyFormatterFormat.Plain,
                selection,
                unwrap: false,
                trim: false,
                out byte[] formatted));
        Assert.Equal("abc", Encoding.UTF8.GetString(formatted));

        gesture.Reset(terminal);
        Assert.Equal((byte)0, gesture.GetClickCount(terminal));
    }

    [GhosttyNativeFact]
    public void SelectionGesture_RepeatTimingPromotesConfiguredDoubleClickBehavior()
    {
        using GhosttyTerminal terminal = new(5, 2);
        using GhosttySelectionGesture gesture = new();
        using GhosttySelectionGestureEvent press =
            new(GhosttyVtNative.GhosttySelectionGestureEventType.Press);

        terminal.Write("abc"u8);
        Assert.True(
            terminal.TryGetGridReference(
                GhosttyVtNative.GhosttyPoint.Active(1, 0),
                out GhosttyVtNative.GhosttyGridRef reference));

        press.SetReference(reference);
        press.SetPosition(10, 10);
        press.SetRepeatDistance(2);
        press.SetRepeatIntervalNanoseconds(1_000_000_000);
        press.SetBehaviors(
            new GhosttyVtNative.GhosttySelectionGestureBehaviors
            {
                SingleClick = GhosttyVtNative.GhosttySelectionGestureBehavior.Cell,
                DoubleClick = GhosttyVtNative.GhosttySelectionGestureBehavior.Word,
                TripleClick = GhosttyVtNative.GhosttySelectionGestureBehavior.Line,
            });

        press.SetTimeNanoseconds(1_000_000);
        gesture.Apply(terminal, press);

        press.SetTimeNanoseconds(2_000_000);
        Assert.True(gesture.TryApply(terminal, press, out GhosttySelectionSnapshot selection));

        GhosttySelectionGestureSnapshot snapshot = gesture.GetSnapshot(terminal);
        Assert.Equal((byte)2, snapshot.ClickCount);
        Assert.False(snapshot.Dragged);
        Assert.Equal(
            GhosttyVtNative.GhosttySelectionGestureBehavior.Word,
            snapshot.Behavior);
        Assert.True(
            terminal.TryFormatSelection(
                GhosttyVtNative.GhosttyFormatterFormat.Plain,
                selection,
                unwrap: false,
                trim: false,
                out byte[] formatted));
        Assert.Equal("abc", Encoding.UTF8.GetString(formatted));
    }

    [GhosttyNativeFact]
    public void SelectionGesture_DragReleaseAndDeepPressExposeNativeState()
    {
        using GhosttyTerminal terminal = new(5, 2);
        using GhosttySelectionGesture gesture = new();
        using GhosttySelectionGestureEvent press =
            new(GhosttyVtNative.GhosttySelectionGestureEventType.Press);
        using GhosttySelectionGestureEvent drag =
            new(GhosttyVtNative.GhosttySelectionGestureEventType.Drag);
        using GhosttySelectionGestureEvent release =
            new(GhosttyVtNative.GhosttySelectionGestureEventType.Release);
        using GhosttySelectionGestureEvent deepPress =
            new(GhosttyVtNative.GhosttySelectionGestureEventType.DeepPress);

        terminal.Write("abcde\r\nfghij"u8);
        Assert.True(
            terminal.TryGetGridReference(
                GhosttyVtNative.GhosttyPoint.Active(1, 0),
                out GhosttyVtNative.GhosttyGridRef pressReference));
        Assert.True(
            terminal.TryGetGridReference(
                GhosttyVtNative.GhosttyPoint.Active(3, 1),
                out GhosttyVtNative.GhosttyGridRef dragReference));

        press.SetReference(pressReference);
        press.SetPosition(10, 10);
        gesture.Apply(terminal, press);

        drag.SetReference(dragReference);
        drag.SetPosition(36, 10);
        drag.SetRectangle(true);
        drag.SetGeometry(
            new GhosttyVtNative.GhosttySelectionGestureGeometry
            {
                Columns = 5,
                CellWidth = 10,
                PaddingLeft = 0,
                ScreenHeight = 20,
            });

        Assert.True(gesture.TryApply(terminal, drag, out GhosttySelectionSnapshot selection));
        Assert.True(selection.Rectangle);
        GhosttySelectionGestureSnapshot dragSnapshot = gesture.GetSnapshot(terminal);
        Assert.Equal((byte)1, dragSnapshot.ClickCount);
        Assert.True(dragSnapshot.Dragged);
        Assert.Equal(
            GhosttyVtNative.GhosttySelectionGestureAutoscroll.None,
            dragSnapshot.Autoscroll);

        release.SetReference(dragReference);
        gesture.Apply(terminal, release);
        GhosttySelectionGestureSnapshot releaseSnapshot = gesture.GetSnapshot(terminal);
        Assert.True(releaseSnapshot.Dragged);
        Assert.Equal(
            GhosttyVtNative.GhosttySelectionGestureAutoscroll.None,
            releaseSnapshot.Autoscroll);

        gesture.Reset(terminal);
        press.SetReference(pressReference);
        gesture.Apply(terminal, press);
        Assert.True(
            gesture.TryApply(
                terminal,
                deepPress,
                out GhosttySelectionSnapshot deepPressSelection));
        GhosttySelectionGestureSnapshot deepPressSnapshot = gesture.GetSnapshot(terminal);
        Assert.Equal((byte)0, deepPressSnapshot.ClickCount);
        Assert.True(deepPressSnapshot.Dragged);
        Assert.Equal(
            GhosttyVtNative.GhosttySelectionGestureAutoscroll.None,
            deepPressSnapshot.Autoscroll);
        Assert.True(
            terminal.TryFormatSelection(
                GhosttyVtNative.GhosttyFormatterFormat.Plain,
                deepPressSelection,
                unwrap: false,
                trim: false,
                out byte[] formatted));
        Assert.Equal("abcde", Encoding.UTF8.GetString(formatted));
    }

    [GhosttyNativeFact]
    public void SelectionGesture_AutoscrollTickUsesViewportAndGeometry()
    {
        using GhosttyTerminal terminal = new(5, 2);
        using GhosttySelectionGesture gesture = new();
        using GhosttySelectionGestureEvent press =
            new(GhosttyVtNative.GhosttySelectionGestureEventType.Press);
        using GhosttySelectionGestureEvent drag =
            new(GhosttyVtNative.GhosttySelectionGestureEventType.Drag);
        using GhosttySelectionGestureEvent tick =
            new(GhosttyVtNative.GhosttySelectionGestureEventType.AutoscrollTick);

        terminal.Write("abcde\r\nfghij"u8);
        Assert.True(
            terminal.TryGetGridReference(
                GhosttyVtNative.GhosttyPoint.Active(1, 0),
                out GhosttyVtNative.GhosttyGridRef pressReference));
        Assert.True(
            terminal.TryGetGridReference(
                GhosttyVtNative.GhosttyPoint.Active(3, 1),
                out GhosttyVtNative.GhosttyGridRef dragReference));
        GhosttyVtNative.GhosttySelectionGestureGeometry geometry = new()
        {
            Columns = 5,
            CellWidth = 10,
            PaddingLeft = 0,
            ScreenHeight = 20,
        };

        press.SetReference(pressReference);
        press.SetPosition(10, 10);
        gesture.Apply(terminal, press);

        drag.SetReference(dragReference);
        drag.SetPosition(36, 20);
        drag.SetGeometry(geometry);
        Assert.True(gesture.TryApply(terminal, drag, out _));
        Assert.Equal(
            GhosttyVtNative.GhosttySelectionGestureAutoscroll.Down,
            gesture.GetAutoscroll(terminal));

        tick.SetViewport(new GhosttyVtNative.GhosttyPointCoordinate { X = 3, Y = 1 });
        tick.SetPosition(36, 20);
        tick.SetGeometry(geometry);
        Assert.True(gesture.TryApply(terminal, tick, out GhosttySelectionSnapshot selection));

        GhosttySelectionGestureSnapshot snapshot = gesture.GetSnapshot(terminal);
        Assert.True(snapshot.Dragged);
        Assert.Equal(
            GhosttyVtNative.GhosttySelectionGestureAutoscroll.Down,
            snapshot.Autoscroll);
        Assert.True(
            terminal.TryFormatSelection(
                GhosttyVtNative.GhosttyFormatterFormat.Plain,
                selection,
                unwrap: false,
                trim: false,
                out byte[] formatted));
        Assert.NotEmpty(formatted);
    }

    [GhosttyNativeFact]
    public void SelectionGesture_SafelyResetsWhenReusedAcrossTerminals()
    {
        using GhosttyTerminal first = new(5, 2);
        using GhosttyTerminal second = new(5, 2);
        using GhosttySelectionGesture gesture = new();
        using GhosttySelectionGestureEvent press =
            new(GhosttyVtNative.GhosttySelectionGestureEventType.Press);

        first.Write("one"u8);
        second.Write("two"u8);
        press.SetBehaviors(
            new GhosttyVtNative.GhosttySelectionGestureBehaviors
            {
                SingleClick = GhosttyVtNative.GhosttySelectionGestureBehavior.Word,
                DoubleClick = GhosttyVtNative.GhosttySelectionGestureBehavior.Word,
                TripleClick = GhosttyVtNative.GhosttySelectionGestureBehavior.Line,
            });

        Assert.True(
            first.TryGetGridReference(
                GhosttyVtNative.GhosttyPoint.Active(1, 0),
                out GhosttyVtNative.GhosttyGridRef firstReference));
        press.SetReference(firstReference);
        Assert.True(gesture.TryApply(first, press, out _));

        Assert.True(
            second.TryGetGridReference(
                GhosttyVtNative.GhosttyPoint.Active(1, 0),
                out GhosttyVtNative.GhosttyGridRef secondReference));
        press.SetReference(secondReference);
        Assert.True(gesture.TryApply(second, press, out GhosttySelectionSnapshot selection));
        Assert.Equal((byte)1, gesture.GetClickCount(second));
        Assert.True(
            second.TryFormatSelection(
                GhosttyVtNative.GhosttyFormatterFormat.Plain,
                selection,
                unwrap: false,
                trim: false,
                out byte[] formatted));
        Assert.Equal("two", Encoding.UTF8.GetString(formatted));
    }

    [GhosttyNativeFact]
    public void SelectionGesture_CanBeReusedAfterPreviousTerminalIsDisposed()
    {
        using GhosttySelectionGesture gesture = new();
        using GhosttySelectionGestureEvent press =
            new(GhosttyVtNative.GhosttySelectionGestureEventType.Press);
        press.SetBehaviors(
            new GhosttyVtNative.GhosttySelectionGestureBehaviors
            {
                SingleClick = GhosttyVtNative.GhosttySelectionGestureBehavior.Word,
                DoubleClick = GhosttyVtNative.GhosttySelectionGestureBehavior.Word,
                TripleClick = GhosttyVtNative.GhosttySelectionGestureBehavior.Line,
            });

        GhosttyTerminal first = new(5, 2);
        first.Write("one"u8);
        Assert.True(
            first.TryGetGridReference(
                GhosttyVtNative.GhosttyPoint.Active(1, 0),
                out GhosttyVtNative.GhosttyGridRef firstReference));
        press.SetReference(firstReference);
        Assert.True(gesture.TryApply(first, press, out _));
        first.Dispose();

        using GhosttyTerminal second = new(5, 2);
        second.Write("two"u8);
        Assert.True(
            second.TryGetGridReference(
                GhosttyVtNative.GhosttyPoint.Active(1, 0),
                out GhosttyVtNative.GhosttyGridRef secondReference));
        press.SetReference(secondReference);
        Assert.True(gesture.TryApply(second, press, out GhosttySelectionSnapshot selection));
        Assert.True(
            second.TryFormatSelection(
                GhosttyVtNative.GhosttyFormatterFormat.Plain,
                selection,
                unwrap: false,
                trim: false,
                out byte[] formatted));
        Assert.Equal("two", Encoding.UTF8.GetString(formatted));
    }

    [GhosttyNativeFact]
    public void SetSelection_AfterTerminalDisposal_Throws()
    {
        GhosttyTerminal terminal = new(5, 2);
        terminal.Dispose();

        Assert.Throws<ObjectDisposedException>(() => terminal.SetSelection(null));
    }

    [GhosttyNativeFact]
    public void TrackedGridReference_ResolvesAndMoves()
    {
        using GhosttyTerminal terminal = new(5, 2);
        terminal.Write("abc"u8);

        using GhosttyTrackedGridReference tracked =
            terminal.TrackGridReference(GhosttyVtNative.GhosttyPoint.Active(1, 0));
        Assert.True(tracked.HasValue);
        Assert.True(tracked.TrySnapshot(out _));
        Assert.True(
            tracked.TryGetPoint(
                GhosttyVtNative.GhosttyPointTag.Active,
                out GhosttyVtNative.GhosttyPointCoordinate point));
        Assert.Equal((ushort)1, point.X);
        Assert.Equal(0u, point.Y);

        tracked.Set(terminal, GhosttyVtNative.GhosttyPoint.Active(2, 0));
        Assert.True(
            tracked.TryGetPoint(
                GhosttyVtNative.GhosttyPointTag.Active,
                out point));
        Assert.Equal((ushort)2, point.X);
        Assert.Equal(0u, point.Y);
    }

    [GhosttyNativeFact]
    public void TrackedGridReference_FollowsScrollAndResizeReflow()
    {
        using GhosttyTerminal terminal = new(5, 2, maxScrollback: 2_000_000);
        terminal.Write("ABCDEFGHIJ"u8);

        using GhosttyTrackedGridReference tracked =
            terminal.TrackGridReference(GhosttyVtNative.GhosttyPoint.Active(1, 1));
        AssertTrackedGrapheme(tracked, (uint)'G');

        terminal.Resize(10, 2);
        Assert.True(
            tracked.TryGetPoint(
                GhosttyVtNative.GhosttyPointTag.Active,
                out GhosttyVtNative.GhosttyPointCoordinate point));
        Assert.Equal((ushort)6, point.X);
        Assert.Equal(0u, point.Y);
        AssertTrackedGrapheme(tracked, (uint)'G');

        terminal.Write("\r\nKLMNO\r\nPQRST"u8);
        Assert.True(
            tracked.TryGetPoint(
                GhosttyVtNative.GhosttyPointTag.History,
                out point));
        Assert.Equal((ushort)6, point.X);
        Assert.Equal(0u, point.Y);
        AssertTrackedGrapheme(tracked, (uint)'G');

        terminal.Resize(5, 2);
        Assert.True(
            tracked.TryGetPoint(
                GhosttyVtNative.GhosttyPointTag.History,
                out point));
        Assert.Equal((ushort)1, point.X);
        Assert.Equal(1u, point.Y);
        AssertTrackedGrapheme(tracked, (uint)'G');
    }

    [GhosttyNativeFact]
    public void TrackedGridReference_IsInvalidatedAfterPruning()
    {
        using GhosttyTerminal terminal = new(5, 2, maxScrollback: 2_000_000);
        terminal.SetScrollbackMaxBytes(null);
        terminal.SetScrollbackMaxLines(2);
        terminal.Write("A"u8);

        using GhosttyTrackedGridReference tracked =
            terminal.TrackGridReference(GhosttyVtNative.GhosttyPoint.Active(0, 0));

        byte[] output = new byte[20_000 * 3];
        for (int offset = 0; offset < output.Length; offset += 3)
        {
            output[offset] = (byte)'X';
            output[offset + 1] = (byte)'\r';
            output[offset + 2] = (byte)'\n';
        }

        terminal.Write(output);

        Assert.False(tracked.HasValue);
        Assert.False(tracked.TrySnapshot(out _));
        Assert.False(
            tracked.TryGetPoint(
                GhosttyVtNative.GhosttyPointTag.Screen,
                out _));
    }

    private static unsafe void AssertTrackedGrapheme(
        GhosttyTrackedGridReference tracked,
        uint expected)
    {
        Assert.True(tracked.TrySnapshot(out GhosttyVtNative.GhosttyGridRef snapshot));
        uint[] graphemes = new uint[4];
        fixed (uint* graphemePtr = graphemes)
        {
            Assert.Equal(
                GhosttyVtNative.GhosttyResult.Success,
                GhosttyVtNative.GridRefGraphemes(
                    in snapshot,
                    graphemePtr,
                    (nuint)graphemes.Length,
                    out nuint written));
            Assert.Equal((nuint)1, written);
        }

        Assert.Equal(expected, graphemes[0]);
    }
}
