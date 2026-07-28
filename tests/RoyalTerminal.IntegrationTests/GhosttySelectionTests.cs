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
}
