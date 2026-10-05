// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.IntegrationTests.TestInfrastructure;
using Xunit;

namespace RoyalTerminal.IntegrationTests;

public sealed class GhosttyWindowResizeTests
{
    [GhosttyNativeFact]
    public void BorrowedWrapperDisposalRemovesItsCallbackWithoutFreeingTheTerminal()
    {
        using GhosttyTerminal owner = new(12, 5);
        using GhosttyTerminal borrowed = new(owner.Handle);
        int requests = 0;
        borrowed.SetWindowResizeCallback((_, _, _, _) => requests++);
        owner.Write("\u001b[8;24;80t"u8);
        Assert.Equal(1, requests);
        borrowed.Dispose();
        GC.Collect(); GC.WaitForPendingFinalizers();
        owner.Write("\u001b[8;40;120t"u8);
        Assert.Equal(1, requests);
    }

    [GhosttyNativeFact]
    public void DisposingLeasedTerminalRemovesCallbackBeforeDelayedNativeFree()
    {
        using GhosttyTerminal owner = new(12, 5);
        using GhosttyTerminal.NativeLifetimeLease lease = owner.AcquireNativeLifetimeLease();
        using GhosttyTerminal borrowed = new(owner.Handle);
        int requests = 0;
        owner.SetWindowResizeCallback((_, _, _, _) => requests++);
        owner.Dispose();
        GC.Collect(); GC.WaitForPendingFinalizers();
        borrowed.Write("\u001b[8;40;120t"u8);
        Assert.Equal(0, requests);
    }

    [GhosttyNativeFact]
    public void CallbackSurvivesCollectionIsReplaceableAndUnregisters()
    {
        using GhosttyTerminal terminal = new(12, 5);
        List<(ushort Rows, ushort Columns)> requests = [];
        terminal.SetWindowResizeCallback((_, _, rows, columns) => requests.Add((rows, columns)));
        GC.Collect(); GC.WaitForPendingFinalizers();
        terminal.Write("\u001b[8;24;80t\u001b[8;;90t\u001b[8;30t\u001b[8t"u8);
        Assert.Equal(new (ushort, ushort)[] { (24, 80), (0, 90), (30, 0), (0, 0) }, requests);
        int replacement = 0;
        terminal.SetWindowResizeCallback((_, _, _, _) => replacement++);
        terminal.Write("\u001b[8;40;120t"u8);
        terminal.SetWindowResizeCallback(null);
        terminal.Write("\u001b[8;40;120t"u8);
        Assert.Equal(1, replacement); Assert.Equal(4, requests.Count);
        Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue, GhosttyVtNative.SetRoyalWindowResizeCallback(0, 0, 0));
        terminal.Dispose();
        Assert.Throws<ObjectDisposedException>(() => terminal.SetWindowResizeCallback(null));
    }

    [GhosttyNativeFact]
    public void NativeParserRejectsPrivateColonIntermediateAndExtraParameters()
    {
        using GhosttyTerminal terminal = new(12, 5);
        int requests = 0;
        terminal.SetWindowResizeCallback((_, _, _, _) => requests++);
        terminal.Write("\u001b[?8;1;2t\u001b[8:1:2t\u001b[8;1;2 t\u001b[8;1;2;3t\u001b[8;1;2;;t"u8);
        Assert.Equal(0, requests);
        terminal.Write("\u001b[8;1;2;t"u8);
        Assert.Equal(1, requests);
    }
}
