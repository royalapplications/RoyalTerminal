// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed partial class ManagedSnapshotMetadataFailureTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RepeatedRefusedStartsDiscardOnlyNewRegistrations(bool explicitId, bool held)
    {
        TerminalScreen screen = Screen(8, 4096, CeilingCapacity(GhosttySnapshotCapacityDimension.StringBytes));
        using BasicVtProcessor processor = new(screen);
        if (held) Process(processor, "\u001b[?2026h");
        for (int i = 0; i < 32; i++)
        {
            string uri = i + new string('u', 4096);
            Process(processor, "\u001b]8;" + (explicitId ? "id=" + i : "") + ";" + uri + "\u001b\\");
        }
        if (held) Process(processor, "\u001b[?2026l");
        Assert.Equal(0, screen.SnapshotCursorHyperlinkToken(0, -1));
        for (int token = 1; token <= 32; token++)
        {
            Assert.False(screen.TryGetHyperlink(token, out _));
            Assert.False(screen.TryGetHyperlinkUrl(token, out _));
        }
        Process(processor, Open("r") + "A");
        int accepted = screen.GetViewportRow(0).ReadOnlyCells[0].HyperlinkId;
        Assert.Equal(1, accepted);
        Assert.True(screen.TryGetHyperlink(accepted, out TerminalHyperlink? link));
        Assert.Equal(0U, link!.ImplicitId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARefusedExistingRegistrationRemainsStableForPublicAndCowOwners(bool explicitId)
    {
        TerminalScreen screen = Screen(8, 4096, CeilingCapacity(GhosttySnapshotCapacityDimension.StringBytes));
        byte[] uri = Encoding.UTF8.GetBytes(new string('u', 4096));
        byte[] id = explicitId ? "same"u8.ToArray() : [];
        int token = screen.RegisterHyperlink(uri, id, 0);
        TerminalScreen retained = screen.CreateStateCopy();
        using BasicVtProcessor processor = new(screen);
        Process(processor, "\u001b]8;" + (explicitId ? "id=same" : "") + ";" + Encoding.UTF8.GetString(uri) + "\u001b\\");
        Assert.Equal(0, screen.SnapshotCursorHyperlinkToken(0, -1));
        Assert.True(screen.TryGetHyperlink(token, out TerminalHyperlink? value));
        Assert.True(retained.TryGetHyperlink(token, out TerminalHyperlink? copy));
        Assert.Same(value, copy);
        Assert.Equal(token, screen.RegisterHyperlink(uri, id, 0));
    }

    [Fact]
    public void RefusedMigrationDiscardsTheNewIdentityButKeepsTheSourceCellAndCounter()
    {
        TerminalScreen screen = Screen(8, 4096, new(8, 1, 16, 192, 1024, 2048),
            CeilingCapacity(GhosttySnapshotCapacityDimension.StringBytes, stringBytes: 0));
        using BasicVtProcessor processor = new(screen);
        Process(processor, Open("u") + "A");
        int source = screen.GetViewportRow(0).ReadOnlyCells[0].HyperlinkId;
        TerminalScreen retained = screen.CreateStateCopy();
        Process(processor, "\u001b[2;1HB");
        Assert.False(screen.TryGetHyperlink(source + 1, out _));
        Assert.False(screen.TryGetHyperlinkUrl(source + 1, out _));
        Assert.True(screen.TryGetHyperlink(source, out _));
        Assert.True(retained.TryGetHyperlink(source, out _));
        Process(processor, "\u001b[1;2H" + Open("r") + "C");
        int next = screen.GetViewportRow(0).ReadOnlyCells[1].HyperlinkId;
        Assert.Equal(source + 1, next);
        Assert.True(screen.TryGetHyperlink(next, out TerminalHyperlink? link));
        Assert.Equal(1U, link!.ImplicitId);
    }

    [Fact]
    public void RefusedResizeRestartDiscardsItsPendingIdentity()
    {
        TerminalScreen screen = Screen(8, 4096, CeilingCapacity(GhosttySnapshotCapacityDimension.StringBytes));
        using BasicVtProcessor processor = new(screen);
        Process(processor, Open(new string('u', 1984)) + "A");
        int source = screen.GetViewportRow(0).ReadOnlyCells[0].HyperlinkId;
        uint counter = 1;
        int accepted = screen.RestoreSnapshotResizeCursor(0, 0, default, source, ref counter);
        Assert.Equal(0, accepted);
        Assert.Equal(1U, counter);
        Assert.False(screen.TryGetHyperlink(source + 1, out _));
        Assert.True(screen.TryGetHyperlink(source, out _));
    }

    [Fact]
    public void ThrownMetadataPreparationAlsoDiscardsThePendingRegistration()
    {
        TerminalScreen screen = Screen(8, 4096, new GhosttySnapshotPageCapacity(8, 1, 16, 192, 1024, 2048));
        using BasicVtProcessor processor = new(screen);
        Process(processor, "A");
        screen.GetViewportRow(0)[1].Attributes = CellAttributes.Bold;
        OutOfMemoryException failure = new("Before accepting pending hyperlink");
        screen.MutationCheckpoint = phase =>
        {
            if (phase == SnapshotMutationCheckpoint.MetadataReconciliation) throw failure;
        };
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => Process(processor, Open("u"))));
        Assert.False(screen.TryGetHyperlink(1, out _));
        Assert.False(screen.TryGetHyperlinkUrl(1, out _));
    }
}
