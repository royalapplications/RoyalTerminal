// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.Controls - Shared platform handle wrapper for native views.

using Avalonia.Platform;

namespace RoyalTerminal.Avalonia.Controls;

/// <summary>
/// Simple IPlatformHandle implementation for native view handles.
/// </summary>
internal sealed class NativeViewHandle : IPlatformHandle
{
    public nint Handle { get; }
    public string HandleDescriptor { get; }

    public NativeViewHandle(nint handle, string descriptor)
    {
        Handle = handle;
        HandleDescriptor = descriptor;
    }
}
