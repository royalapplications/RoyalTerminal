// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class TerminalProgramStatusStoreTests
{
    [Fact]
    public void ApplicationInheritanceTracksReplacementClearAndTransientRemoval()
    {
        TerminalProgramStatusStore store = new();
        store.Apply(new(TerminalProgramStatusState.Idle, App: "root"));
        store.Apply(new(TerminalProgramStatusState.Done, "build", App: "cargo"));
        store.Apply(new(TerminalProgramStatusState.Done, "builder", App: "other"));
        store.Apply(new(TerminalProgramStatusState.Working, "build/test", App: "test"));
        Assert.Equal("test", store.GetApplication("build/test/child"));
        Assert.Equal("cargo", store.GetApplication("build/testing"));
        Assert.Equal("root", store.GetApplication("Build/test"));

        store.Apply(new(TerminalProgramStatusState.Done, "build/test"));
        Assert.Equal("cargo", store.GetApplication("build/test/child"));
        store.Apply(new(TerminalProgramStatusState.Working, "build", App: "new"));
        Assert.Equal("new", store.GetApplication("build/test/child"));
        Assert.True(store.ClearTransient());
        Assert.Equal("root", store.GetApplication("build/test/child"));
        Assert.Contains(store.Records, r => r.Id == "build/test");
        store.Apply(new(TerminalProgramStatusState.Done, "build/test", App: "again"));
        store.Apply(new(TerminalProgramStatusState.Clear, "build"));
        Assert.Equal("root", store.GetApplication("build/test/child"));
        Assert.Equal("other", store.GetApplication("builder/child"));
        store.Apply(new(TerminalProgramStatusState.Clear));
        Assert.Empty(store.Records);
        Assert.Equal("", store.GetApplication("builder/child"));
        store.Apply(new(TerminalProgramStatusState.Done, App: "restored"));
        Assert.Equal("restored", store.GetApplication("child"));
        Assert.True(store.Reset());
        Assert.Equal("", store.GetApplication("child"));
        Assert.False(store.Reset());
    }

    [Fact]
    public void EvictionAndRefreshKeepApplicationLookupInUpdateOrder()
    {
        TerminalProgramStatusStore store = new();
        store.Apply(new(TerminalProgramStatusState.Idle, App: "root"));
        store.Apply(new(TerminalProgramStatusState.Done, "parent", App: "parent-app"));
        for (int i = 2; i < TerminalProgramStatusStore.Capacity; i++)
            store.Apply(new(TerminalProgramStatusState.Done, $"parent/child{i}"));
        store.Apply(new(TerminalProgramStatusState.Idle, App: "root"));
        Assert.False(store.Apply(new(TerminalProgramStatusState.Idle, App: "root")));
        Assert.Equal("parent-app", store.GetApplication("parent/child2"));
        store.Apply(new(TerminalProgramStatusState.Done, "new"));
        Assert.Equal(TerminalProgramStatusStore.Capacity, store.Records.Count);
        Assert.Equal("root", store.GetApplication("parent/child2"));
        Assert.DoesNotContain(store.Records, r => r.Id == "parent");
        store.Apply(new(TerminalProgramStatusState.Done, "parent", App: "replacement"));
        Assert.Equal("replacement", store.GetApplication("parent/child255"));
    }
}
