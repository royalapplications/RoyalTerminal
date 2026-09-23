// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalScreenAnchorTests
{
    [Fact]
    public void RectangularScrollMovesOnlyAnchorsInsideBothAxes()
    {
        TerminalScreen screen = new(8, 5);
        TerminalScreenAnchor left = screen.CreateAnchor(2, 1);
        TerminalScreenAnchor right = screen.CreateAnchor(2, 6);
        TerminalScreenAnchor inside = screen.CreateAnchor(2, 3);
        TerminalScreenAnchor clipped = screen.CreateAnchor(1, 3);
        screen.ShiftAnchorsInViewportRows(1, 3, -1, 2, 5);
        AssertPosition(screen, left, 2, 1);
        AssertPosition(screen, right, 2, 6);
        AssertPosition(screen, inside, 1, 3);
        Assert.False(screen.TryResolveAnchor(clipped, out _));
    }

    [Fact]
    public void Anchor_TracksHistoryAndIsPrunedBeforeRowStorageIsReused()
    {
        TerminalScreen screen = new(8, 2, scrollbackLimit: 1);
        TerminalScreenAnchor first = screen.CreateAnchor(0, 3);
        TerminalScreenAnchor second = screen.CreateAnchor(1, 5);
        screen.AddRow();
        AssertPosition(screen, first, 0, 3);
        AssertPosition(screen, second, 1, 5);
        screen.AddRow();
        Assert.False(screen.TryResolveAnchor(first, out _));
        AssertPosition(screen, second, 0, 5);
        screen.AddRow();
        Assert.False(screen.TryResolveAnchor(second, out _));
    }

    [Fact]
    public void Anchor_ClearScrollbackAndLowerLimitPreserveSurvivingIdentity()
    {
        TerminalScreen screen = new(8, 2, 4);
        for (int i = 0; i < 4; i++) screen.AddRow();
        TerminalScreenAnchor old = screen.CreateAnchor(1, 3);
        TerminalScreenAnchor live = screen.CreateAnchor(4, 5);
        screen.ScrollbackLimit = 2;
        Assert.False(screen.TryResolveAnchor(old, out _));
        AssertPosition(screen, live, 2, 5);
        screen.ClearScrollback();
        AssertPosition(screen, live, 0, 5);
    }

    [Fact]
    public void Anchor_InPlaceScrollPrunesOnlyRegionAndMoveCanRestoreClippedPlacement()
    {
        TerminalScreen screen = new(8, 5);
        TerminalScreenAnchor outside = screen.CreateAnchor(0, 3);
        TerminalScreenAnchor clipped = screen.CreateAnchor(1, 4);
        TerminalScreenAnchor moved = screen.CreateAnchor(2, 5);
        screen.ShiftAnchorsInViewportRows(1, 3, -1);
        AssertPosition(screen, outside, 0, 3);
        Assert.False(screen.TryResolveAnchor(clipped, out _));
        AssertPosition(screen, moved, 1, 5);
        Assert.True(screen.MoveAnchor(clipped, 1, 4));
        AssertPosition(screen, clipped, 1, 4);
        screen.ShiftAnchorsInViewportRows(1, 3, 2);
        AssertPosition(screen, clipped, 3, 4);
        Assert.True(screen.ReleaseAnchor(clipped));
        Assert.False(screen.ReleaseAnchor(clipped));
        Assert.False(screen.MoveAnchor(clipped, 1, 4));
    }

    [Fact]
    public void Anchor_AlternateBuffersAreIsolatedAndClearOrDiscardInvalidatesOnlyAlternate()
    {
        TerminalScreen screen = new(8, 3);
        TerminalScreenAnchor primary = screen.CreateAnchor(1, 6);
        screen.SwitchToAlternateBuffer(clear: true);
        Assert.False(screen.TryResolveAnchor(primary, out _));
        Assert.False(screen.MoveAnchor(primary, 0, 0));
        TerminalScreenAnchor alternate = screen.CreateAnchor(2, 4);
        screen.SwitchToPrimaryBuffer();
        AssertPosition(screen, primary, 1, 6);
        Assert.False(screen.TryResolveAnchor(alternate, out _));
        screen.SwitchToAlternateBuffer(clear: false);
        AssertPosition(screen, alternate, 2, 4);
        screen.SwitchToAlternateBuffer(clear: true);
        Assert.False(screen.TryResolveAnchor(alternate, out _));
        alternate = screen.CreateAnchor(1, 1);
        screen.SwitchToPrimaryBuffer();
        screen.DiscardInactiveAlternateBuffer();
        screen.SwitchToAlternateBuffer(clear: false);
        Assert.False(screen.TryResolveAnchor(alternate, out _));
        screen.SwitchToPrimaryBuffer();
        AssertPosition(screen, primary, 1, 6);
    }

    [Fact]
    public void Anchor_StateCopyAndPublishRetainIdentityWithoutLeakingPositions()
    {
        TerminalScreen screen = new(8, 2, 0);
        TerminalScreenAnchor token = screen.CreateAnchor(1, 3);
        TerminalScreen copy = screen.CreateStateCopy();
        copy.AddRow();
        AssertPosition(screen, token, 1, 3);
        AssertPosition(copy, token, 0, 3);
        TerminalScreenAnchor newToken = copy.CreateAnchor(1, 2);
        Assert.False(screen.TryResolveAnchor(newToken, out _));
        screen.AdoptStateFrom(copy);
        AssertPosition(screen, token, 0, 3);
        AssertPosition(screen, newToken, 1, 2);
        TerminalScreen independent = screen.CreateStateCopy();
        independent.ReleaseAnchor(token);
        AssertPosition(screen, token, 0, 3);
        screen.ClearAll();
        Assert.False(screen.TryResolveAnchor(token, out _));
        Assert.False(screen.MoveAnchor(token, 0, 0));
    }

    [Fact]
    public void Anchor_ReflowFollowsTextAndBlankPinsClampWithoutAddingPhantomRows()
    {
        TerminalScreen screen = new(8, 3);
        for (int c = 0; c < 8; c++) screen.GetViewportRow(0)[c].Codepoint = 'A' + c;
        TerminalScreenAnchor text = screen.CreateAnchor(0, 6);
        TerminalScreenAnchor blank = screen.CreateAnchor(1, 7);
        screen.Resize(4, 3);
        AssertPosition(screen, text, 1, 2);
        AssertPosition(screen, blank, 2, 3);
        Assert.Equal(3, screen.TotalRows);
        screen.Resize(8, 3);
        AssertPosition(screen, text, 0, 6);
        AssertPosition(screen, blank, 1, 3);
    }

    [Fact]
    public void Anchor_ReflowWideCellAndSimultaneousHistoryTrimming()
    {
        TerminalScreen screen = new(6, 2, 1);
        using BasicVtProcessor processor = new(screen);
        processor.Process("AB界CD"u8);
        // Actual content, rather than disposable viewport padding, must push
        // the first reflowed row beyond the one-row history limit.
        screen.GetViewportRow(1)[0].Codepoint = 'Z';
        TerminalScreenAnchor wide = screen.CreateAnchor(0, 2);
        TerminalScreenAnchor tail = screen.CreateAnchor(0, 5);
        screen.Resize(3, 2);
        // AB moves out of the limited-history buffer. The wide lead starts the
        // surviving row and its trailing narrow cell is tracked separately.
        AssertPosition(screen, wide, 0, 0);
        AssertPosition(screen, tail, 1, 0);
    }

    [Fact]
    public void Anchor_AlternateHeightShrinkPrunesInsteadOfAliasingAnotherRow()
    {
        TerminalScreen screen = new(8, 4);
        screen.SwitchToAlternateBuffer(clear: true);
        TerminalScreenAnchor kept = screen.CreateAnchor(0, 6);
        TerminalScreenAnchor removed = screen.CreateAnchor(3, 2);
        screen.Resize(4, 2, reflowOnResize: false);
        AssertPosition(screen, kept, 0, 3);
        Assert.False(screen.TryResolveAnchor(removed, out _));
    }

    [Fact]
    public void Anchor_RejectsInvalidPositionsAndForeignIdentities()
    {
        TerminalScreen screen = new(8, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => screen.CreateAnchor(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => screen.CreateAnchor(2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => screen.CreateAnchor(0, 8));
        TerminalScreen other = new(8, 2);
        TerminalScreenAnchor token = other.CreateAnchor(0, 0);
        Assert.False(screen.TryResolveAnchor(token, out _));
        Assert.False(screen.MoveAnchor(token, 0, 0));
    }

    private static void AssertPosition(TerminalScreen screen, TerminalScreenAnchor token, int row, int column)
    {
        Assert.True(screen.TryResolveAnchor(token, out TerminalGridPosition position));
        Assert.Equal(row, position.Row);
        Assert.Equal(column, position.Column);
    }
}
