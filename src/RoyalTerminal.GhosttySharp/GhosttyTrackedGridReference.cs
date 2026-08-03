// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

/// <summary>
/// Owned libghostty grid reference that follows its cell across scrolling,
/// pruning, and resize/reflow operations.
/// </summary>
public sealed class GhosttyTrackedGridReference : IDisposable
{
    private nint _handle;
    private bool _disposed;

    internal GhosttyTrackedGridReference(nint handle)
    {
        _handle = handle;
    }

    /// <summary>Gets whether the tracked reference currently resolves to a cell.</summary>
    public bool HasValue
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return GhosttyVtNative.TrackedGridRefHasValue(_handle);
        }
    }

    /// <summary>Moves this tracked reference to a new point in the terminal.</summary>
    public void Set(GhosttyTerminal terminal, GhosttyVtNative.GhosttyPoint point)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(terminal);
        ThrowIfFailed(
            GhosttyVtNative.TrackedGridRefSet(_handle, terminal.Handle, point),
            "ghostty_tracked_grid_ref_set");
    }

    /// <summary>Attempts to snapshot the tracked location as an untracked grid reference.</summary>
    public bool TrySnapshot(out GhosttyVtNative.GhosttyGridRef snapshot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.GhosttyGridRef value = GhosttyVtNative.GhosttyGridRef.CreateSized();
        GhosttyVtNative.GhosttyResult result = GhosttyVtNative.TrackedGridRefSnapshot(_handle, ref value);
        if (result == GhosttyVtNative.GhosttyResult.NoValue)
        {
            snapshot = default;
            return false;
        }

        ThrowIfFailed(result, "ghostty_tracked_grid_ref_snapshot");
        snapshot = value;
        return true;
    }

    /// <summary>Attempts to resolve the tracked location in a coordinate space.</summary>
    public unsafe bool TryGetPoint(
        GhosttyVtNative.GhosttyPointTag tag,
        out GhosttyVtNative.GhosttyPointCoordinate point)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.GhosttyPointCoordinate value = default;
        GhosttyVtNative.GhosttyResult result =
            GhosttyVtNative.TrackedGridRefPoint(_handle, tag, &value);
        if (result == GhosttyVtNative.GhosttyResult.NoValue)
        {
            point = default;
            return false;
        }

        ThrowIfFailed(result, "ghostty_tracked_grid_ref_point");
        point = value;
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GhosttyVtNative.TrackedGridRefFree(_handle);
        _handle = nint.Zero;
    }

    private static void ThrowIfFailed(GhosttyVtNative.GhosttyResult result, string operation)
    {
        if (result != GhosttyVtNative.GhosttyResult.Success)
        {
            throw new InvalidOperationException($"{operation} failed with {result}.");
        }
    }
}
