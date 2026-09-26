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
