// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.IntegrationTests.TestInfrastructure;
using Xunit;

namespace RoyalTerminal.IntegrationTests;

public sealed class GhosttyNotificationTests
{
    // The native notification effect borrows its callback just like Ghostty's
    // other effects. Match WT ConptyConnection.Close's no-callback-after-close
    // guarantee and xterm.js's disposable OSC registrations: a surviving native
    // terminal must not call a disposed managed registration owner.
    [GhosttyNativeFact]
    public unsafe void BorrowedWrapperDisposalUnregistersNotificationsAndResetCallbacks()
    {
        using GhosttyTerminal owner = new(10, 4);
        using GhosttyTerminal borrowed = new(owner.Handle);
        int requests = 0;
        GhosttyVtNative.RoyalNotificationCallback callback = (_, _, _, _, _, _, _) => requests++;
        borrowed.SetNotificationCallback(callback);
        owner.Write("\x1b]99;;before\a"u8);
        Assert.Equal(1, requests);

        borrowed.Dispose();
        borrowed.Dispose();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        owner.Write("\x1b]99;;after\a\u001bc"u8);
        // Keep a root in this regression even on a broken build, so failure is
        // an assertion rather than an unmanaged call to a collected delegate.
        GC.KeepAlive(callback);
        Assert.Equal(1, requests);
        Assert.True(owner.IsValid);
    }

    [GhosttyNativeFact]
    public unsafe void LeasedTerminalDisposalUnregistersNotificationsBeforeNativeFree()
    {
        using GhosttyTerminal owner = new(10, 4);
        using GhosttyTerminal.NativeLifetimeLease lease = owner.AcquireNativeLifetimeLease();
        using GhosttyTerminal borrowed = new(owner.Handle);
        int requests = 0;
        GhosttyVtNative.RoyalNotificationCallback callback = (_, _, _, _, _, _, _) => requests++;
        owner.SetNotificationCallback(callback);

        owner.Dispose();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        borrowed.Write("\x1b]99;;after\a"u8);
        GC.KeepAlive(callback);
        Assert.Equal(0, requests);
        Assert.False(owner.IsValid);
    }

    [GhosttyNativeFact]
    public unsafe void CallbackUsesBorrowedSlicesAndSurvivesCollectionUntilUnregistered()
    {
        using GhosttyTerminal terminal = new(10, 4);
        List<(string Metadata, string Payload, byte Kind)> received = new();
        terminal.SetNotificationCallback((_, _, metadata, metadataLength, payload, payloadLength, kind) =>
            received.Add((Encoding.UTF8.GetString(new ReadOnlySpan<byte>(metadata, (int)metadataLength)),
                Encoding.UTF8.GetString(new ReadOnlySpan<byte>(payload, (int)payloadLength)), kind)));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        terminal.Write("\x1b]99;i=one;hello\a\x1b]99;i=two;world\x1b\\\u001bc"u8);
        Assert.Equal(new[] { ("i=one", "hello", (byte)1), ("i=two", "world", (byte)0), ("", "", (byte)2) }, received);
        terminal.SetNotificationCallback(null);
        terminal.Write("\x1b]99;;ignored\a"u8);
        Assert.Equal(3, received.Count);
        Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue, GhosttyVtNative.SetRoyalNotificationCallback(0, 0, 0));
        terminal.Dispose();
        Assert.Throws<ObjectDisposedException>(() => terminal.SetNotificationCallback(null));
    }
}
