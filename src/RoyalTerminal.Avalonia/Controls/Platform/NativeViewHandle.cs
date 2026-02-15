// Licensed under the MIT License.
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
