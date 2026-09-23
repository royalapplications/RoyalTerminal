// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class GhosttyKeyEventTextLifetimeTests(ITestOutputHelper output)
{
    [Fact]
    public void EventOwnsUtf8UntilReplacementAcrossCompactingCollections()
    {
        if (!Available()) return;
        using GhosttyKeyEvent evt = new();
        using GhosttyKeyEncoder encoder = new();
        evt.SetKey(GhosttyVtNative.GhosttyVtKey.A);
        evt.SetAction(GhosttyVtNative.GhosttyVtKeyAction.Press);
        foreach (string text in new[] { "a", "é😀", new string('漢', 2048), "b", "\0", "\ud800", "" })
        {
            evt.SetText(text);
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            Assert.Equal(Encoding.UTF8.GetBytes(text), encoder.Encode(evt));
        }
        evt.SetText(null);
        Assert.Empty(encoder.Encode(evt));
        evt.Dispose(); Assert.Throws<ObjectDisposedException>(() => evt.SetText("x"));
    }

    [Fact]
    public void WarmTextUpdatesReuseOwnedPinnedStorageWithoutAllocating()
    {
        if (!Available()) return;
        using GhosttyKeyEvent evt = new();
        for (int i = 0; i < 1000; i++) evt.SetText("é😀");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { evt.SetText("a"); evt.SetText(null); evt.SetText("é😀"); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private bool Available()
    {
        bool available = GhosttyVtProcessor.IsAvailable(); output.WriteLine($"Native key text lifetime available: {available}"); return available;
    }
}
