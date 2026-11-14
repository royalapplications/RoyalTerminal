// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Multiplexing;
using Xunit;

namespace RoyalTerminal.Multiplexing.Tests;

public sealed class MultiplexerLayoutTests
{
    [Fact]
    public async Task SessionAndWindowLabelChangesRejectStaleRevisions()
    {
        await using MultiplexerBroker broker = new(new FakeFactory());
        MultiplexerSessionDescriptor original = broker.CreateSession("initial");
        MultiplexerSessionDescriptor renamed = broker.RenameSession(original.Id, "renamed", original.Revision);
        Assert.Equal(original.Revision + 1, renamed.Revision);
        Assert.Equal("initial", original.Label);
        Assert.Equal(MultiplexerErrorCode.Conflict,
            Assert.Throws<MultiplexerException>(() => broker.RenameSession(original.Id, "stale", original.Revision)).Code);
        MultiplexerWindowDescriptor window = broker.RenameWindow(original.Id, original.Windows[0].Id, "editor", renamed.Revision);
        Assert.Equal("editor", window.Label);
        Assert.Equal("renamed", broker.GetSession(original.Id).Label);
    }

    [Fact]
    public async Task SplitAndClosePreserveRemainingTreeAndProcess()
    {
        FakeFactory factory = new();
        await using MultiplexerBroker broker = new(factory);
        MultiplexerSessionDescriptor session = broker.CreateSession("layout");
        MultiplexerBlockDescriptor first = await broker.AddBlockAsync(session.Id, "first", MultiplexerBrokerTests.Options());
        long revision = broker.GetSession(session.Id).Revision;
        MultiplexerBlockDescriptor second = await broker.SplitBlockAsync(session.Id, first.Id,
            MultiplexerSplitOrientation.Vertical, 0.65, "second", MultiplexerBrokerTests.Options(), revision);
        MultiplexerLayoutNode root = broker.GetSession(session.Id).Windows[0].Layout!;
        Assert.Equal(MultiplexerSplitOrientation.Vertical, root.Orientation);
        Assert.Equal(0.65, root.Ratio);
        Assert.Equal(first.Id, root.First!.BlockId);
        Assert.Equal(second.Id, root.Second!.BlockId);
        await broker.CloseBlockAsync(session.Id, first.Id);
        MultiplexerSessionDescriptor remaining = broker.GetSession(session.Id);
        Assert.Equal(second.Id, remaining.Windows[0].Layout!.BlockId);
        Assert.Single(remaining.Blocks);
        Assert.Equal(1, factory.Transports[0].StopCount);
        Assert.True(factory.Transports[1].IsRunning);
    }

    [Fact]
    public async Task WindowLayoutMustContainItsOwnBlocksExactlyOnce()
    {
        await using MultiplexerBroker broker = new(new FakeFactory());
        MultiplexerSessionDescriptor session = broker.CreateSession("validation");
        MultiplexerWindowDescriptor other = broker.CreateWindow(session.Id, "other");
        MultiplexerBlockDescriptor first = await broker.AddBlockAsync(session.Id, "first", MultiplexerBrokerTests.Options());
        MultiplexerBlockDescriptor second = await broker.AddBlockAsync(session.Id, "second", MultiplexerBrokerTests.Options());
        MultiplexerBlockDescriptor foreign = await broker.AddBlockAsync(session.Id, other.Id, "other", MultiplexerBrokerTests.Options());
        Guid window = session.Windows[0].Id;
        Assert.Throws<ArgumentException>(() => broker.SetWindowLayout(session.Id, window, MultiplexerLayoutNode.Leaf(first.Id)));
        Assert.Throws<ArgumentException>(() => broker.SetWindowLayout(session.Id, window,
            MultiplexerLayoutNode.Split(MultiplexerSplitOrientation.Horizontal, MultiplexerLayoutNode.Leaf(first.Id), MultiplexerLayoutNode.Leaf(first.Id))));
        Assert.Throws<ArgumentException>(() => broker.SetWindowLayout(session.Id, window,
            MultiplexerLayoutNode.Split(MultiplexerSplitOrientation.Horizontal, MultiplexerLayoutNode.Leaf(first.Id), MultiplexerLayoutNode.Leaf(foreign.Id))));
        Assert.Throws<ArgumentException>(() => broker.SetWindowLayout(session.Id, window, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => broker.SetWindowLayout(session.Id, window,
            MultiplexerLayoutNode.Split(MultiplexerSplitOrientation.Vertical, MultiplexerLayoutNode.Leaf(first.Id), MultiplexerLayoutNode.Leaf(second.Id), double.NaN)));
        MultiplexerLayoutNode valid = MultiplexerLayoutNode.Split(MultiplexerSplitOrientation.Vertical,
            MultiplexerLayoutNode.Leaf(second.Id), MultiplexerLayoutNode.Leaf(first.Id), 0.4);
        Assert.Equal(valid, broker.SetWindowLayout(session.Id, window, valid).Layout);
    }

    [Fact]
    public async Task StaleSplitCannotCreateAProcessOrChangeLayout()
    {
        FakeFactory factory = new();
        await using MultiplexerBroker broker = new(factory);
        MultiplexerSessionDescriptor session = broker.CreateSession("conflict");
        MultiplexerBlockDescriptor first = await broker.AddBlockAsync(session.Id, "first", MultiplexerBrokerTests.Options());
        MultiplexerException error = await Assert.ThrowsAsync<MultiplexerException>(() => broker.SplitBlockAsync(session.Id,
            first.Id, MultiplexerSplitOrientation.Horizontal, 0.5, "second", MultiplexerBrokerTests.Options(), session.Revision).AsTask());
        Assert.Equal(MultiplexerErrorCode.Conflict, error.Code);
        Assert.Single(factory.Transports);
        Assert.Equal(first.Id, broker.GetSession(session.Id).Windows[0].Layout!.BlockId);
    }

    [Fact]
    public async Task WindowAndLabelQuotasAreValidatedBeforeMutation()
    {
        await using MultiplexerBroker broker = new(new FakeFactory(), new() { MaxWindowsPerSession = 1, MaxLabelLength = 5 });
        MultiplexerSessionDescriptor session = broker.CreateSession("valid");
        Assert.Throws<ArgumentException>(() => broker.RenameSession(session.Id, "longer"));
        Assert.Throws<ArgumentException>(() => broker.RenameSession(session.Id, "a\nb"));
        Assert.Equal(MultiplexerErrorCode.CapacityExceeded,
            Assert.Throws<MultiplexerException>(() => broker.CreateWindow(session.Id, "other")).Code);
        Assert.Equal(session.Revision, broker.GetSession(session.Id).Revision);
    }

    [Fact]
    public async Task CreationOrderRemainsStableAfterDeletingEarlierEntries()
    {
        await using MultiplexerBroker broker = new(new FakeFactory());
        MultiplexerSessionDescriptor first = broker.CreateSession("first");
        MultiplexerSessionDescriptor second = broker.CreateSession("second");
        await broker.CloseSessionAsync(first.Id);
        MultiplexerSessionDescriptor third = broker.CreateSession("third");
        Assert.Equal([second.Id, third.Id], broker.ListSessions().Select(session => session.Id).ToArray());
        MultiplexerBlockDescriptor a = await broker.AddBlockAsync(second.Id, "a", MultiplexerBrokerTests.Options());
        MultiplexerBlockDescriptor b = await broker.AddBlockAsync(second.Id, "b", MultiplexerBrokerTests.Options());
        await broker.CloseBlockAsync(second.Id, a.Id);
        MultiplexerBlockDescriptor c = await broker.AddBlockAsync(second.Id, "c", MultiplexerBrokerTests.Options());
        Assert.Equal([b.Id, c.Id], broker.GetSession(second.Id).Blocks.Select(block => block.Id).ToArray());
    }
}
