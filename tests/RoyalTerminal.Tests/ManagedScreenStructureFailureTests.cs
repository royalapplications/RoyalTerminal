// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using RoyalTerminal.Terminal.Theming;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty ScreenSet.getInit/switchTo, WT Terminal::UseAlternateScreenBuffer and
// xterm.js BufferSet.activateAltBuffer prepare storage before selecting it.
// We retain Ghostty's persistent alternate buffer rather than xterm.js's discard
// policy. CLR partial-mutation failures additionally prohibit COW publication.
public sealed class ManagedScreenStructureFailureTests
{
    [Theory]
    [InlineData(47, false)]
    [InlineData(47, true)]
    [InlineData(1047, false)]
    [InlineData(1047, true)]
    [InlineData(1049, false)]
    [InlineData(1049, true)]
    public void ProtocolSwitchFailureLatchesAlreadyChangedCursorAndModeRegisters(int mode, bool primary)
    {
        TerminalScreen screen = new(8, 2);
        using BasicVtProcessor processor = new(screen);
        if (primary) processor.Process(Encoding.ASCII.GetBytes($"\u001b[?{mode}h"));
        TerminalScreen retained = screen.CreateStateCopy();
        OutOfMemoryException failure = new("Protocol buffer allocation");
        screen.MutationCheckpoint = phase => { if (phase == SnapshotMutationCheckpoint.BufferSwitchPrepared) throw failure; };

        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() =>
            processor.Process(Encoding.ASCII.GetBytes($"\u001b[?{mode}{(primary ? 'l' : 'h')}"))));

        Assert.True(screen.SnapshotMutationFailed);
        Assert.False(retained.SnapshotMutationFailed);
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => processor.Process("A"u8)));
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => processor.GetBinarySnapshot()));
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(processor.Reset));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BufferSwitchFailureDistinguishesPreparationFromPublishedMutation(bool primary, bool published)
    {
        TerminalScreen screen = new(8, 2);
        screen.GetRow(0)[0].Codepoint = 'P';
        if (primary)
        {
            screen.SwitchToAlternateBuffer(false);
            screen.GetRow(0)[0].Codepoint = 'A';
        }
        TerminalRow original = screen.GetRow(0);
        TerminalScreen retained = screen.CreateStateCopy();
        SnapshotMutationCheckpoint target = published ? SnapshotMutationCheckpoint.BufferSwitchPublished : SnapshotMutationCheckpoint.BufferSwitchPrepared;
        OutOfMemoryException failure = new("Buffer switch allocation");
        screen.MutationCheckpoint = phase => { if (phase == target) throw failure; };

        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => Switch(screen, primary)));

        Assert.Equal(published, screen.SnapshotMutationFailed);
        Assert.Equal(published ? !primary : primary, screen.AlternateBufferActive);
        Assert.False(retained.SnapshotMutationFailed);
        Assert.Equal(primary, retained.AlternateBufferActive);
        Assert.Equal(primary ? 'A' : 'P', retained.GetRow(0).ReadOnlyCells[0].Codepoint);
        screen.MutationCheckpoint = null;
        if (published)
        {
            Assert.Same(failure, Assert.Throws<OutOfMemoryException>(screen.ClearAll));
            Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => retained.AdoptStateFrom(screen)));
        }
        else
        {
            Assert.Same(original, screen.GetRow(0));
            Switch(screen, primary);
            Assert.Equal(!primary, screen.AlternateBufferActive);
            Switch(screen, !primary);
            Assert.Same(original, screen.GetRow(0));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResetPreparesReplacementWithoutTouchingEitherBuffer(bool alternate)
    {
        TerminalScreen screen = new(8, 2);
        screen.GetRow(0)[0].Codepoint = 'P';
        screen.SwitchToAlternateBuffer(false);
        screen.GetRow(0)[0].Codepoint = 'A';
        if (!alternate) screen.SwitchToPrimaryBuffer();
        TerminalRow original = screen.GetRow(0);
        TerminalScreen retained = screen.CreateStateCopy();
        OutOfMemoryException failure = new("Reset allocation");
        screen.MutationCheckpoint = phase =>
        {
            // Reset must not normalize or switch a dormant buffer first.
            Assert.NotEqual(SnapshotMutationCheckpoint.BufferSwitchPrepared, phase);
            if (phase == SnapshotMutationCheckpoint.ResetPrepared) throw failure;
        };

        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(screen.ClearAll));

        Assert.False(screen.SnapshotMutationFailed);
        Assert.Equal(alternate, screen.AlternateBufferActive);
        Assert.Same(original, screen.GetRow(0));
        screen.MutationCheckpoint = null;
        screen.ClearAll();
        Assert.False(screen.AlternateBufferActive);
        Assert.Equal(2, screen.TotalRows);
        Assert.Equal(0, screen.GetRow(0).ReadOnlyCells[0].Codepoint);
        Assert.Equal(alternate ? 'A' : 'P', retained.GetRow(0).ReadOnlyCells[0].Codepoint);
        screen.SwitchToAlternateBuffer(false);
        Assert.Equal(0, screen.GetRow(0).ReadOnlyCells[0].Codepoint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecycledRowFailureCannotBeResetOrPublished(bool alternate)
    {
        TerminalScreen screen = new(8, 2, 0);
        if (alternate) screen.SwitchToAlternateBuffer(false);
        screen.GetRow(0)[0].Codepoint = 'A';
        TerminalScreen retained = screen.CreateStateCopy();
        OutOfMemoryException failure = new("After removal, before COW row recycling");
        screen.MutationCheckpoint = phase => { if (phase == SnapshotMutationCheckpoint.RowRecycling) throw failure; };

        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => screen.AddRow()));

        Assert.True(screen.SnapshotMutationFailed);
        Assert.Equal(1, screen.TotalRows);
        Assert.Equal(2, retained.TotalRows);
        Assert.Equal('A', retained.GetRow(0).ReadOnlyCells[0].Codepoint);
        Assert.False(retained.SnapshotMutationFailed);
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(screen.ClearAll));
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => screen.AddRow()));
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => screen.ScrollbackLimit = 10));
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(screen.DiscardInactiveAlternateBuffer));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecolorFailureLatchesOnlyItsOwner(bool alternate)
    {
        TerminalScreen screen = new(8, 2);
        if (alternate) screen.SwitchToAlternateBuffer(false);
        TerminalTheme original = screen.Theme;
        TerminalScreen retained = screen.CreateStateCopy();
        uint foreground = original.DefaultForeground ^ 0x00FFFFFF;
        TerminalTheme next = original.WithDefaultForeground(foreground);
        OutOfMemoryException failure = new("After first row recolor");
        screen.MutationCheckpoint = phase => { if (phase == SnapshotMutationCheckpoint.ThemeRowResolved) throw failure; };

        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => screen.ApplyTheme(next)));

        Assert.Same(original, screen.Theme);
        Assert.Equal(foreground, screen.GetRow(0).ReadOnlyCells[0].Foreground);
        Assert.Equal(original.DefaultForeground, retained.GetRow(0).ReadOnlyCells[0].Foreground);
        Assert.False(retained.SnapshotMutationFailed);
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => screen.ApplyTheme(original)));
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => retained.AdoptStateFrom(screen)));
    }

    [Fact]
    public void IdenticalCellThemeDoesNotDetachCowRows()
    {
        TerminalScreen screen = new(8, 2);
        TerminalScreen retained = screen.CreateStateCopy();
        object storage = screen.GetRow(0).SearchStorageIdentity;
        screen.MutationCheckpoint = phase => Assert.NotEqual(SnapshotMutationCheckpoint.ThemeRowResolved, phase);

        screen.ApplyTheme(screen.Theme);

        Assert.Same(storage, screen.GetRow(0).SearchStorageIdentity);
        Assert.Same(storage, retained.GetRow(0).SearchStorageIdentity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResizeFailureSkipsAllocatingCursorCleanupAndRestoresOriginal(bool completion)
    {
        TerminalScreen screen = TrackedScreen();
        using BasicVtProcessor processor = new(screen);
        processor.Process("\u001b[1m\u001b]8;;u\u001b\\"u8);
        TerminalRow original = screen.GetRow(0);
        OutOfMemoryException first = new("Resize allocation"), cleanup = new("Must not mask first failure");
        int restores = 0;
        screen.MutationCheckpoint = phase =>
        {
            if (phase != SnapshotMutationCheckpoint.CursorResizeRestore) return;
            restores++;
            throw completion && restores == 1 ? first : cleanup;
        };
        if (!completion) processor.ResizeCheckpoint = phase =>
        {
            if (phase == ManagedResizeCheckpoint.PrimaryRows) throw first;
        };

        Assert.Same(first, Assert.Throws<OutOfMemoryException>(() => processor.ResizeScreen(8, 3, 0, 0)));

        Assert.Equal(completion ? 1 : 0, restores);
        Assert.False(screen.SnapshotMutationFailed);
        Assert.Same(original, screen.GetRow(0));
        Assert.Equal(2, screen.ViewportRows);
        processor.ResizeCheckpoint = null;
        screen.MutationCheckpoint = null;
        processor.ResizeScreen(8, 3, 0, 0);
        processor.Process("A"u8);
        Assert.NotEmpty(processor.GetBinarySnapshot());
    }

    [Fact]
    public void AbandonedFaultedCursorLeaseDoesNotRestoreSharedTables()
    {
        TerminalScreen screen = TrackedScreen();
        using BasicVtProcessor processor = new(screen);
        processor.Process("\u001b[1m\u001b]8;;u\u001b\\"u8);
        int token = screen.SnapshotCursorHyperlinkToken(0, 0);
        uint counter = 0;
        GhosttySnapshotPageTracker.CursorResizeLease? lease = screen.BeginSnapshotCursorResize(0, 0,
            new(default, default, default, 1), ref token, ref counter);
        Assert.NotNull(lease);
        TerminalScreen retained = screen.CreateStateCopy();
        OutOfMemoryException failure = new("Discard private owner");
        screen.RecordSnapshotMutationFailure(failure);
        screen.MutationCheckpoint = phase => throw new InvalidOperationException("Cleanup must be skipped");

        lease!.Dispose();
        lease.Dispose();

        Assert.False(retained.SnapshotMutationFailed);
        Assert.Equal(0, retained.SnapshotCursorHyperlinkToken(0, -1));
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(screen.ThrowIfSnapshotMutationFailed));
    }

    private static void Switch(TerminalScreen screen, bool primary)
    {
        if (primary) screen.SwitchToPrimaryBuffer();
        else screen.SwitchToAlternateBuffer(false);
    }

    private static TerminalScreen TrackedScreen()
    {
        TerminalScreen owner = new(8, 2);
        TerminalScreen screen = TerminalScreen.CreateSnapshotStorage(8, 2, 100, owner.Theme);
        GhosttySnapshotPageAllocation page = new(new(8, 8, 8, 192, 0, 2048));
        screen.InstallSnapshotRows([new(8) { SnapshotAllocation = page }, new(8) { SnapshotAllocation = page, SnapshotAllocationRow = 1 }], null, 0);
        screen.SnapshotScrollbackQuota = new() { PageAlignment = 4096 };
        return screen;
    }
}
