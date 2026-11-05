// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Xunit;

namespace RoyalTerminal.Multiplexing.Tests;

public sealed class PlacementTests
{
    [Fact]
    public async Task CrossWindowMoveRetainsIdentityControlHistoryAndProcess()
    {
        FakeFactory factory = new(); await using MultiplexerBroker broker = new(factory);
        var s = broker.CreateSession("one"); var w = broker.CreateWindow(s.Id, "two");
        var a = await broker.AddBlockAsync(s.Id, "a", MultiplexerBrokerTests.Options());
        var b = await broker.AddBlockAsync(s.Id, w.Id, "b", MultiplexerBrokerTests.Options());
        using var lease = broker.Attach(s.Id, a.Id, MultiplexerRole.Controller);
        var old = broker.GetSession(s.Id);
        var moved = broker.PlaceBlock(s.Id, a.Id, w.Id, MultiplexerPaneKind.Tiled, b.Id, MultiplexerSplitOrientation.Vertical, 0.35, true, expectedRevision: old.Revision);
        Assert.Null(moved.Windows[0].Layout);
        Assert.Equal(a.Id, moved.Windows[1].Layout!.First!.BlockId);
        Assert.Equal(b.Id, moved.Windows[1].Layout!.Second!.BlockId);
        Assert.Equal(0.35, moved.Windows[1].Layout!.Ratio);
        var live = moved.Blocks.Single(x => x.Id == a.Id);
        Assert.Equal(w.Id, live.WindowId); Assert.Equal(lease.Id, live.ControllerId);
        Assert.Equal(old.Blocks[0].LastSequence, live.LastSequence);
        Assert.Equal(old.Revision + 1, moved.Revision);
        Assert.Equal(2, factory.Transports.Count); Assert.All(factory.Transports, t => Assert.Equal(0, t.StopCount));
        Assert.Throws<MultiplexerException>(() => broker.SwapBlocks(s.Id, a.Id, b.Id, old.Revision));
        Assert.Equal(moved.Revision, broker.GetSession(s.Id).Revision);
    }

    [Fact]
    public async Task FloatSwapAndShelfPreserveExactMembershipAcrossWindows()
    {
        await using MultiplexerBroker broker = new(new FakeFactory());
        var s = broker.CreateSession("one"); var w = broker.CreateWindow(s.Id, "two");
        var a = await broker.AddBlockAsync(s.Id, "a", MultiplexerBrokerTests.Options());
        var b = await broker.AddBlockAsync(s.Id, w.Id, "b", MultiplexerBrokerTests.Options());
        MultiplexerFloatBounds bounds = new(20, 50, 6000, 7000);
        broker.PlaceBlock(s.Id, a.Id, a.WindowId, MultiplexerPaneKind.Floating, bounds: bounds);
        var swapped = broker.SwapBlocks(s.Id, a.Id, b.Id);
        Assert.Null(swapped.Windows[0].Layout); Assert.Equal(a.Id, swapped.Windows[1].Layout!.BlockId);
        var floating = swapped.Blocks.Single(x => x.Id == b.Id);
        Assert.Equal(bounds, floating.FloatingBounds); Assert.Equal(a.WindowId, floating.WindowId);
        Assert.Throws<ArgumentException>(() => broker.SetWindowLayout(s.Id, a.WindowId, MultiplexerLayoutNode.Leaf(b.Id)));
        Assert.Throws<ArgumentOutOfRangeException>(() => broker.PlaceBlock(s.Id, b.Id, w.Id, MultiplexerPaneKind.Floating, bounds: new(int.MaxValue, 0, 10, 10)));
        Assert.Equal(swapped.Revision, broker.GetSession(s.Id).Revision);
        var shelf = broker.PlaceBlock(s.Id, a.Id, w.Id, MultiplexerPaneKind.Unplaced);
        Assert.Null(shelf.Windows[1].Layout); Assert.Equal(MultiplexerPaneKind.Unplaced, shelf.Blocks[0].Placement);
        var restored = broker.PlaceBlock(s.Id, a.Id, a.WindowId, MultiplexerPaneKind.Tiled);
        Assert.Equal(a.Id, restored.Windows[0].Layout!.BlockId);
        await broker.CloseBlockAsync(s.Id, b.Id); Assert.Single(broker.GetSession(s.Id).Blocks);
    }

    [Fact]
    public async Task ClosingAWindowCannotImplicitlyTerminateItsContents()
    {
        FakeFactory factory = new(); await using MultiplexerBroker broker = new(factory);
        var s = broker.CreateSession("one"); var w = broker.CreateWindow(s.Id, "two");
        var a = await broker.AddBlockAsync(s.Id, w.Id, "a", MultiplexerBrokerTests.Options());
        Assert.Equal(w.Id, broker.ReorderWindow(s.Id, w.Id, 1).Windows[0].Id);
        Assert.Throws<MultiplexerException>(() => broker.CloseWindow(s.Id, w.Id));
        var closed = broker.CloseWindow(s.Id, w.Id, s.Windows[0].Id);
        Assert.Single(closed.Windows); Assert.Null(closed.Windows[0].Layout);
        Assert.Equal(a.Id, closed.Blocks[0].Id); Assert.Equal(MultiplexerPaneKind.Unplaced, closed.Blocks[0].Placement);
        Assert.Throws<MultiplexerException>(() => broker.CloseWindow(s.Id, closed.Windows[0].Id));
        Assert.True(factory.Transports[0].IsRunning);
    }

    [Fact]
    public async Task RevisionSubscriptionHasNoReadRegisterRaceAndCancelsIndependently()
    {
        await using MultiplexerBroker broker = new(new FakeFactory()); var s = broker.CreateSession("one");
        using CancellationTokenSource cancel = new();
        Task<MultiplexerSessionDescriptor> abandoned = broker.WaitForSessionChangeAsync(s.Id, s.Revision, cancel.Token).AsTask();
        Task<MultiplexerSessionDescriptor> active = broker.WaitForSessionChangeAsync(s.Id, s.Revision).AsTask();
        cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
        Assert.False(active.IsCompleted);
        var rename = broker.RenameSession(s.Id, "two");
        Assert.Equal(rename.Revision, (await active.WaitAsync(TimeSpan.FromSeconds(3))).Revision);
        Assert.Equal(rename.Revision, (await broker.WaitForSessionChangeAsync(s.Id, s.Revision)).Revision);
        var closed = broker.WaitForSessionChangeAsync(s.Id, rename.Revision).AsTask();
        await broker.CloseSessionAsync(s.Id);
        Assert.Equal(MultiplexerErrorCode.NotFound, (await Assert.ThrowsAsync<MultiplexerException>(() => closed)).Code);
    }
}
