// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using RoyalTerminal.IntegrationTests.TestInfrastructure;
using Xunit;

namespace RoyalTerminal.IntegrationTests;

public sealed class GhosttyDragDropTests
{
    [Fact]
    public unsafe void ItemLayoutMatchesCHeader()
    {
        GhosttyVtNative.RoyalDndItem item = default;
        Assert.Equal(IntPtr.Size * 4, Unsafe.SizeOf<GhosttyVtNative.RoyalDndItem>());
        Assert.Equal(IntPtr.Size, (byte*)&item.MimeLength - (byte*)&item);
        Assert.Equal(IntPtr.Size * 2, (byte*)&item.Data - (byte*)&item);
        Assert.Equal(IntPtr.Size * 3, (byte*)&item.DataLength - (byte*)&item);
    }

    [GhosttyNativeFact]
    public unsafe void MetadataProbesAndInvalidInputDoNotMutateRegistration()
    {
        using GhosttyTerminal terminal = new(10, 4);
        terminal.Write("\x1b]72;t=a;i\x1b\\"u8);
        Assert.True(terminal.GetDragDropState().Registered);
        Assert.Null(terminal.GetDragDropState().AcceptedOperation);
        Assert.Equal(new[] { "i" }, terminal.GetRegisteredDropMimeTypes());
        using var lease = terminal.AcquireNativeLifetimeLease();
        Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue, GhosttyVtNative.DragDropState(0, out _, out _));
        Assert.Equal(GhosttyVtNative.GhosttyResult.OutOfSpace, GhosttyVtNative.DragDropMimes(terminal.Handle, null, 0, out nuint length));
        Assert.Equal((nuint)1, length);
        Assert.Equal(GhosttyVtNative.GhosttyResult.InvalidValue,
            GhosttyVtNative.DragDropEvent(terminal.Handle, 1, 0, 0, 0, 0, 1, null, 1, default));
        Assert.True(terminal.GetDragDropState().Registered);
    }

    [GhosttyNativeFact]
    public void ResetPreservesRegistrationButExplicitSessionUnregisterDoesNot()
    {
        using GhosttyTerminal terminal = new(10, 4);
        terminal.Write("\x1b]72;t=a:m=1;text/\x1b\\"u8);
        terminal.Reset();
        Assert.True(terminal.GetDragDropState().Registered);
        terminal.SendDragDropEvent(5);
        Assert.False(terminal.GetDragDropState().Registered);
        Assert.Empty(terminal.GetRegisteredDropMimeTypes());
        terminal.Dispose();
        Assert.Throws<ObjectDisposedException>(() => terminal.SendDragDropEvent(1));
        Assert.Throws<ObjectDisposedException>(() => terminal.GetDragDropState());
    }
}
