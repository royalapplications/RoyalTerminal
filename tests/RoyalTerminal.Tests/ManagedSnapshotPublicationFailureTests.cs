// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty PageAllocation.prepend performs fallible work before list publication;
// setMaxBytes/setMaxLines enforce complete-page limits. WT AdaptDispatch controls
// rendering suspension and xterm.js RenderService buffers refreshes until mode
// reset/timeout. The managed host additionally owns COW frame transactions: a
// failed preparation is retryable, but partial quota retirement is owner-fatal.
public sealed class ManagedSnapshotPublicationFailureTests
{
    [Fact]
    public void FailedHoldPreparationKeepsVisibleOwnerAndModeUsable()
    {
        TerminalScreen screen = new(12, 2);
        using BasicVtProcessor processor = new(screen);
        processor.Process("A"u8);
        TerminalRow row = screen.GetViewportRow(0);
        OutOfMemoryException failure = new("After hold preparation");
        processor.PublicationCheckpoint = phase =>
        {
            if (phase == ManagedPublicationCheckpoint.HoldPrepared) throw failure;
        };

        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => processor.Process("\u001b[?2026h"u8)));

        processor.PublicationCheckpoint = null;
        Assert.False(screen.SnapshotMutationFailed);
        Assert.Same(row, screen.GetViewportRow(0));
        Assert.Null(processor.NextTimedRefreshDelay);
        AssertMode(processor, false);
        processor.Process("B"u8);
        Assert.Equal('B', screen.GetViewportRow(0).ReadOnlyCells[1].Codepoint);
        processor.Process("\u001b[?2026hC"u8);
        Assert.Equal(0, screen.GetViewportRow(0).ReadOnlyCells[2].Codepoint);
        AssertMode(processor, true);
        processor.Process("\u001b[?2026l"u8);
        Assert.Equal('C', screen.GetViewportRow(0).ReadOnlyCells[2].Codepoint);
    }

    [Fact]
    public void ThrowingHoldClockCannotLeaveAnUnpublishedUnheldOwner()
    {
        ThrowingClock clock = new();
        TerminalScreen screen = new(12, 2);
        using BasicVtProcessor processor = new(screen, new() { TimeProvider = clock });
        processor.Process("A"u8);
        // First timestamp belongs to the completed-prefix animation tick;
        // the second constructs the hold's deadline.
        clock.CallsBeforeFailure = 2;

        Assert.Same(clock.Failure, Assert.Throws<OutOfMemoryException>(() => processor.Process("\u001b[?2026h"u8)));

        AssertMode(processor, false);
        Assert.Null(processor.NextTimedRefreshDelay);
        processor.Process("B"u8);
        Assert.Equal('B', screen.GetViewportRow(0).ReadOnlyCells[1].Codepoint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedPublicationRetainsHoldAndCanRetry(bool timeout)
    {
        ThrowingClock clock = new();
        TerminalScreen screen = new(12, 2);
        using BasicVtProcessor processor = new(screen, new() { TimeProvider = clock });
        processor.Process("A\u001b[?2026hB"u8);
        TerminalRow published = screen.GetViewportRow(0);
        OutOfMemoryException failure = new("Before frame ownership transfer");
        processor.PublicationCheckpoint = phase =>
        {
            if (phase == ManagedPublicationCheckpoint.HoldPublishing) throw failure;
        };
        try
        {
            if (timeout)
            {
                clock.Timestamp = 1000;
                Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => processor.RefreshTimedState()));
            }
            else Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => processor.Process("\u001b[?2026l"u8)));
            Assert.False(screen.SnapshotMutationFailed);
            Assert.Same(published, screen.GetViewportRow(0));
            Assert.Equal(0, published.ReadOnlyCells[1].Codepoint);
            Assert.NotNull(processor.NextTimedRefreshDelay);
            // Mode queries parse only before timeout; an expired hold first
            // retries publication at the next input boundary.
            if (!timeout) AssertMode(processor, true);
        }
        finally { processor.PublicationCheckpoint = null; }

        if (timeout) Assert.True(processor.RefreshTimedState());
        else processor.Process("\u001b[?2026l"u8);
        Assert.Null(processor.NextTimedRefreshDelay);
        AssertMode(processor, false);
        Assert.Equal('B', screen.GetViewportRow(0).ReadOnlyCells[1].Codepoint);
    }

    [Theory]
    [InlineData("protocol")]
    [InlineData("timeout")]
    [InlineData("dispose")]
    public void FailedHeldQuotaRetirementNeverPublishes(string ending)
    {
        ThrowingClock clock = new();
        TerminalScreen screen = new(80, 2, 10_000) { SnapshotScrollbackQuota = new() { PageAlignment = 4096 } };
        using BasicVtProcessor processor = new(screen, new() { TimeProvider = clock });
        processor.Process("visible"u8);
        TerminalRow published = screen.GetViewportRow(0);
        OutOfMemoryException failure = new("Private quota retirement failed");
        bool armed = false;
        screen.MutationCheckpoint = phase =>
        {
            if (armed && phase == SnapshotMutationCheckpoint.RowRetirement) throw failure;
        };
        processor.Process("\u001b[?2026h"u8);
        int rows = new GhosttySnapshotAllocation(4096).InitialRows(80) * 4;
        for (int i = 0; i < rows; i++) processor.Process("hidden\r\n"u8);
        // The published owner has no history to retire. Its changed policy
        // only encounters the new pages when the held owner synchronizes it.
        screen.SnapshotScrollbackQuota = new() { PageAlignment = 4096, MaximumBytes = 0 };
        armed = true;

        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() =>
        {
            if (ending == "dispose") processor.Dispose();
            else if (ending == "timeout")
            {
                clock.Timestamp = 1000;
                processor.RefreshTimedState();
            }
            else processor.Process("\u001b[?2026l"u8);
        }));

        Assert.False(screen.SnapshotMutationFailed);
        Assert.Same(published, screen.GetViewportRow(0));
        Assert.Equal(2, screen.TotalRows);
        Assert.Equal('v', published.ReadOnlyCells[0].Codepoint);
        Assert.Null(processor.NextTimedRefreshDelay);
        Assert.False(processor.RefreshTimedState());
        processor.Dispose();
        Assert.Same(published, screen.GetViewportRow(0));
    }

    [Fact]
    public void RepeatedCowPageReplacementKeepsEarlierOwnersIndependent()
    {
        TerminalScreen screen = new(80, 2) { SnapshotScrollbackQuota = new() { PageAlignment = 4096 } };
        using BasicVtProcessor processor = new(screen);
        processor.Process("A"u8);
        GhosttySnapshotPageAllocation allocation = screen.GetViewportRow(0).SnapshotAllocation!;
        for (int i = 0; i < 32; i++)
        {
            TerminalScreen retained = screen.CreateStateCopy();
            TerminalCell before = retained.GetViewportRow(0).ReadOnlyCells[0];
            processor.Process(i % 2 == 0 ? "\r\u001b[31mB"u8 : "\r\u001b[32mC"u8);
            Assert.Same(allocation, screen.GetViewportRow(0).SnapshotAllocation);
            Assert.Equal(before, retained.GetViewportRow(0).ReadOnlyCells[0]);
            Assert.False(retained.SnapshotMutationFailed);
            using BasicVtProcessor independent = new(retained);
            Assert.NotEmpty(independent.GetBinarySnapshot());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedOwnerCannotBeTransferredOrOverwritten(bool failedSource)
    {
        TerminalScreen target = new(4, 1), source = new(4, 1);
        target.GetViewportRow(0)[0].Codepoint = 'T';
        source.GetViewportRow(0)[0].Codepoint = 'S';
        TerminalRow original = target.GetViewportRow(0);
        OutOfMemoryException failure = new("Unusable owner");
        (failedSource ? source : target).RecordSnapshotMutationFailure(failure);

        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => target.AdoptStateFrom(source)));

        Assert.Same(original, target.GetViewportRow(0));
        Assert.Equal('T', original.ReadOnlyCells[0].Codepoint);
        Assert.Equal('S', source.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
    }

    [Fact]
    public void SameIdentityMetadataReplacementRetainsCowOwnership()
    {
        TerminalScreen screen = new(80, 2) { SnapshotScrollbackQuota = new() { PageAlignment = 4096 } };
        using BasicVtProcessor processor = new(screen);
        processor.Process("\u001b[1mA"u8);
        TerminalScreen retained = screen.CreateStateCopy();
        GhosttySnapshotPageAllocation allocation = screen.GetViewportRow(0).SnapshotAllocation!;
        List<TerminalRow> rows = [];
        for (int i = 0; i < screen.TotalRows; i++)
            if (ReferenceEquals(screen.GetRow(i).SnapshotAllocation, allocation)) rows.Add(screen.GetRow(i));

        Assert.Same(allocation, screen.SnapshotAllocationReplaced(allocation, allocation, rows));

        processor.Process("\rB"u8);
        Assert.Same(allocation, screen.GetViewportRow(0).SnapshotAllocation);
        Assert.Equal('B', screen.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
        Assert.Equal('A', retained.GetViewportRow(0).ReadOnlyCells[0].Codepoint);
        Assert.Equal(CellAttributes.Bold, retained.GetViewportRow(0).ReadOnlyCells[0].Attributes);
        Assert.NotEmpty(processor.GetBinarySnapshot());
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public void QuotaFailureFaultsOwnerAndRestoresActiveBuffer(bool alternate, int failAtRetirement)
    {
        TerminalScreen screen = TwoBuffers(alternate);
        TerminalScreen retained = screen.CreateStateCopy();
        OutOfMemoryException failure = new("Partial quota metadata retirement");
        int retirements = 0;
        screen.MutationCheckpoint = phase =>
        {
            if (phase == SnapshotMutationCheckpoint.RowRetirement && ++retirements == failAtRetirement) throw failure;
        };

        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() =>
            screen.SnapshotScrollbackQuota = new() { PageAlignment = 4096, MaximumBytes = 0 }));

        Assert.Equal(failAtRetirement, retirements);
        Assert.True(screen.SnapshotMutationFailed);
        Assert.Equal(alternate, screen.AlternateBufferActive);
        Assert.False(retained.SnapshotMutationFailed);
        Assert.Equal(10, retained.GetSnapshotRows(0)!.Count);
        Assert.Equal(10, retained.GetSnapshotRows(1)!.Count);
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => screen.SnapshotScrollbackQuota = null));
        using BasicVtProcessor processor = new(screen);
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => processor.Process("X"u8)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeAndHostHistoryClearLatchRetirementFailure(bool native)
    {
        TerminalScreen screen = TwoBuffers(false);
        OutOfMemoryException failure = new("Before retired rows leave the buffer");
        screen.MutationCheckpoint = phase =>
        {
            if (phase == SnapshotMutationCheckpoint.RowRetirement) throw failure;
        };
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() =>
        {
            if (native) screen.EraseActiveHistory();
            else screen.ClearScrollback();
        }));
        Assert.True(screen.SnapshotMutationFailed);
        Assert.Equal(10, screen.TotalRows); // Payload removal did not claim success.
    }

    [Fact]
    public void InactiveScopePreparationFailureDoesNotChangeLiveFields()
    {
        TerminalScreen screen = TwoBuffers(false);
        TerminalRow row = screen.GetViewportRow(0);
        OutOfMemoryException failure = new("Preparing missing dormant raster collections");
        screen.MutationCheckpoint = phase =>
        {
            if (phase == SnapshotMutationCheckpoint.InactiveBufferPrepared) throw failure;
        };
        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() =>
        {
            using TerminalScreen.InactiveResizeScope scope = screen.EnterInactiveResize(80, 2);
        }));
        Assert.False(screen.AlternateBufferActive);
        Assert.False(screen.SnapshotMutationFailed);
        Assert.Same(row, screen.GetViewportRow(0));
        screen.MutationCheckpoint = null;
        using (TerminalScreen.InactiveResizeScope scope = screen.EnterInactiveResize(80, 2))
        {
            Assert.True(scope.Available);
            Assert.True(screen.AlternateBufferActive);
        }
        Assert.Same(row, screen.GetViewportRow(0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HistoryPreparationFailureDoesNotPublishRowsLinksOrAnchors(bool linked)
    {
        TerminalScreen screen = new(4, 2, 20), owner = new(4, 1);
        TerminalRow original = screen.GetRow(0), history = new(4);
        history[0].Codepoint = 'H';
        if (linked) history[0].HyperlinkId = owner.RegisterHyperlink("history"u8, [], 1);
        GhosttySnapshotPage page = GhosttySnapshotLivePage.Capture([history], owner, 4);
        TerminalScreenAnchor anchor = screen.CreateAnchor(0, 0);
        OutOfMemoryException failure = new("Prepared history was not committed");
        screen.MutationCheckpoint = phase =>
        {
            if (phase == SnapshotMutationCheckpoint.HistoryPrepared) throw failure;
        };

        Assert.Same(failure, Assert.Throws<OutOfMemoryException>(() => screen.PrependSnapshotHistory(0, page)));

        Assert.False(screen.SnapshotMutationFailed);
        Assert.Equal(2, screen.TotalRows);
        Assert.Same(original, screen.GetRow(0));
        Assert.False(screen.TryGetHyperlink(1, out _));
        Assert.True(screen.TryResolveAnchor(anchor, out TerminalGridPosition unchanged));
        Assert.Equal(new(0, 0), unchanged);
        screen.MutationCheckpoint = null;
        Assert.Equal(1, screen.PrependSnapshotHistory(0, page));
        Assert.Same(original, screen.GetRow(1));
        Assert.True(screen.TryResolveAnchor(anchor, out TerminalGridPosition moved));
        Assert.Equal(new(0, 1), moved);
    }

    private static TerminalScreen TwoBuffers(bool alternate)
    {
        TerminalScreen owner = new(80, 2);
        TerminalScreen screen = TerminalScreen.CreateSnapshotStorage(80, 2, 100, owner.Theme);
        screen.InstallSnapshotRows(Pages(), Pages(), alternate ? 1 : 0);
        screen.SnapshotScrollbackQuota = new() { PageAlignment = 4096 };
        return screen;

        static TerminalRow[] Pages()
        {
            TerminalRow[] rows = new TerminalRow[10];
            for (int i = 0; i < rows.Length; i += 2)
            {
                GhosttySnapshotPageAllocation allocation = new(new(80, 2, 0, 0, 0, 0));
                rows[i] = new(80) { SnapshotAllocation = allocation, SnapshotAllocationRow = 0 };
                rows[i + 1] = new(80) { SnapshotAllocation = allocation, SnapshotAllocationRow = 1 };
            }
            return rows;
        }
    }

    private static void AssertMode(BasicVtProcessor processor, bool enabled)
    {
        string? reply = null;
        processor.ResponseCallback = bytes => reply = Encoding.ASCII.GetString(bytes);
        processor.Process("\u0018\u001b[?2026$p"u8);
        Assert.Equal(enabled ? "\u001b[?2026;1$y" : "\u001b[?2026;2$y", reply);
        processor.ResponseCallback = null;
    }

    private sealed class ThrowingClock : TimeProvider
    {
        internal OutOfMemoryException Failure { get; } = new("Hold clock failure");
        internal int CallsBeforeFailure { get; set; }
        internal long Timestamp { get; set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp()
        {
            if (CallsBeforeFailure > 0 && --CallsBeforeFailure == 0) throw Failure;
            return Timestamp;
        }
    }
}
