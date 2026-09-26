// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalMouseShapeTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Names()
    {
        yield return ["default", TerminalMouseShape.Default];
        yield return ["context-menu", TerminalMouseShape.ContextMenu];
        yield return ["help", TerminalMouseShape.Help];
        yield return ["pointer", TerminalMouseShape.Pointer];
        yield return ["progress", TerminalMouseShape.Progress];
        yield return ["wait", TerminalMouseShape.Wait];
        yield return ["cell", TerminalMouseShape.Cell];
        yield return ["crosshair", TerminalMouseShape.Crosshair];
        yield return ["text", TerminalMouseShape.Text];
        yield return ["vertical-text", TerminalMouseShape.VerticalText];
        yield return ["alias", TerminalMouseShape.Alias];
        yield return ["copy", TerminalMouseShape.Copy];
        yield return ["move", TerminalMouseShape.Move];
        yield return ["no-drop", TerminalMouseShape.NoDrop];
        yield return ["not-allowed", TerminalMouseShape.NotAllowed];
        yield return ["grab", TerminalMouseShape.Grab];
        yield return ["grabbing", TerminalMouseShape.Grabbing];
        yield return ["all-scroll", TerminalMouseShape.AllScroll];
        yield return ["col-resize", TerminalMouseShape.ColResize];
        yield return ["row-resize", TerminalMouseShape.RowResize];
        yield return ["n-resize", TerminalMouseShape.NResize];
        yield return ["e-resize", TerminalMouseShape.EResize];
        yield return ["s-resize", TerminalMouseShape.SResize];
        yield return ["w-resize", TerminalMouseShape.WResize];
        yield return ["ne-resize", TerminalMouseShape.NeResize];
        yield return ["nw-resize", TerminalMouseShape.NwResize];
        yield return ["se-resize", TerminalMouseShape.SeResize];
        yield return ["sw-resize", TerminalMouseShape.SwResize];
        yield return ["ew-resize", TerminalMouseShape.EwResize];
        yield return ["ns-resize", TerminalMouseShape.NsResize];
        yield return ["nesw-resize", TerminalMouseShape.NeswResize];
        yield return ["nwse-resize", TerminalMouseShape.NwseResize];
        yield return ["zoom-in", TerminalMouseShape.ZoomIn];
        yield return ["zoom-out", TerminalMouseShape.ZoomOut];
        yield return ["left_ptr", TerminalMouseShape.Default];
        yield return ["question_arrow", TerminalMouseShape.Help];
        yield return ["hand", TerminalMouseShape.Pointer];
        yield return ["left_ptr_watch", TerminalMouseShape.Progress];
        yield return ["watch", TerminalMouseShape.Wait];
        yield return ["cross", TerminalMouseShape.Crosshair];
        yield return ["xterm", TerminalMouseShape.Text];
        yield return ["dnd-link", TerminalMouseShape.Alias];
        yield return ["dnd-copy", TerminalMouseShape.Copy];
        yield return ["dnd-move", TerminalMouseShape.Move];
        yield return ["dnd-no-drop", TerminalMouseShape.NoDrop];
        yield return ["crossed_circle", TerminalMouseShape.NotAllowed];
        yield return ["hand1", TerminalMouseShape.Grab];
        yield return ["right_side", TerminalMouseShape.EResize];
        yield return ["top_side", TerminalMouseShape.NResize];
        yield return ["top_right_corner", TerminalMouseShape.NeResize];
        yield return ["top_left_corner", TerminalMouseShape.NwResize];
        yield return ["bottom_side", TerminalMouseShape.SResize];
        yield return ["bottom_right_corner", TerminalMouseShape.SeResize];
        yield return ["bottom_left_corner", TerminalMouseShape.SwResize];
        yield return ["left_side", TerminalMouseShape.WResize];
        yield return ["fleur", TerminalMouseShape.AllScroll];
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void AllNamesAndAliasesMatchNativeAtEveryInputSplit(string name, TerminalMouseShape expected)
    {
        if (!Available()) return;
        foreach (string terminator in new[] { "\u0007", "\u001b\\" })
        {
            byte[] bytes = Encoding.ASCII.GetBytes("\u001b]22;" + name + terminator);
            for (int split = 0; split <= bytes.Length; split++)
            {
                using GhosttyTerminal native = new(8, 3);
                using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
                native.Write(bytes.AsSpan(0, split)); managed.Process(bytes.AsSpan(0, split));
                native.Write(bytes.AsSpan(split)); managed.Process(bytes.AsSpan(split));
                Assert.Equal(expected, native.GetMouseInputState().Shape);
                Assert.Equal(expected, managed.MouseShape);
            }
        }
    }

    [Theory]
    [InlineData("22;")]
    [InlineData("22;Pointer")]
    [InlineData("22;pointer;help")]
    [InlineData("22;?pointer")]
    [InlineData("22; pointer")]
    [InlineData("022;pointer")]
    [InlineData("+22;pointer")]
    public void UnknownNamesAndNoncanonicalSelectorsDoNotChangeShape(string payload)
    {
        if (!Available()) return;
        using GhosttyVtProcessor native = new(new TerminalScreen(8, 3));
        using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
        native.Process("\u001b]22;crosshair\u0007"u8); managed.Process("\u001b]22;crosshair\u0007"u8);
        byte[] bytes = Encoding.UTF8.GetBytes("\u001b]" + payload + "\u0007");
        native.Process(bytes); managed.Process(bytes);
        Assert.Equal(TerminalMouseShape.Crosshair, native.MouseShape);
        Assert.Equal(native.MouseShape, managed.MouseShape);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShapeIsLiveDuringHoldAndSurvivesBuffersAndResets(bool native)
    {
        if (native && !Available()) return;
        using IVtProcessor processor = native ? new GhosttyVtProcessor(new TerminalScreen(8, 3)) : new BasicVtProcessor(new TerminalScreen(8, 3));
        ITerminalMouseShapeSource state = (ITerminalMouseShapeSource)processor;
        Assert.Equal(TerminalMouseShape.Text, state.MouseShape);
        processor.Process("\u001b[?2026h\u001b]22;pointer\u0007"u8);
        Assert.Equal(TerminalMouseShape.Pointer, state.MouseShape);
        processor.Process("\u001b[?1049h\u001b[!p\u001b[?1049l\u001bc"u8);
        Assert.Equal(TerminalMouseShape.Pointer, state.MouseShape);
        processor.Reset();
        Assert.Equal(TerminalMouseShape.Pointer, state.MouseShape);
        foreach (bool preserve in new[] { true, false })
        {
            ((ITerminalSessionHistoryController)processor).PrepareForNewSession(preserve);
            Assert.Equal(TerminalMouseShape.Pointer, state.MouseShape);
        }
    }

    [Fact]
    public void EverySnapshotShapeAndUnknownNormalizationMatchesNative()
    {
        if (!Available()) return;
        for (int wire = 0; wire <= 255; wire++)
        {
            List<SnapshotTestRecord> records = SnapshotTestRecords.Fixture();
            records[0].Payload[37] = (byte)wire;
            for (int i = 0; i < records.Count; i++)
                if (records[i].Tag == GhosttySnapshotRecordTag.Continuation) records[i] = new(records[i].Tag, []);
            using GhosttyTerminal native = GhosttySnapshot.Decode(SnapshotTestRecords.Encode(records));
            using GhosttySnapshotStateReader reader = new(GhosttySnapshot.Encode(native), new());
            using BasicVtProcessor managed = new(new TerminalScreen(2, 3));
            managed.InstallSnapshotModes(reader.ReadReady().Terminal.Header);
            Assert.Equal(wire <= 33 ? (TerminalMouseShape)wire : TerminalMouseShape.Text, native.GetMouseInputState().Shape);
            Assert.Equal(native.GetMouseInputState().Shape, managed.MouseShape);
        }
    }

    [Fact]
    public void WarmManagedShapeUpdatesDoNotAllocate()
    {
        using BasicVtProcessor managed = new(new TerminalScreen(8, 3));
        for (int i = 0; i < 1000; i++) managed.Process("\u001b]22;pointer\u0007\u001b]22;text\u0007"u8);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) managed.Process("\u001b]22;pointer\u0007\u001b]22;text\u0007"u8);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private bool Available()
    {
        bool available = GhosttyVtProcessor.IsAvailable(); output.WriteLine($"Native mouse shape available: {available}"); return available;
    }
}

