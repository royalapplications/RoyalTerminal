// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

/// <summary>
/// Borrowed handle to the active screen's Kitty Graphics storage.
/// </summary>
public sealed class GhosttyKittyGraphics
{
    private readonly GhosttyTerminal _terminal;

    internal GhosttyKittyGraphics(GhosttyTerminal terminal, nint handle)
    {
        _terminal = terminal;
        Handle = handle;
    }

    internal nint Handle { get; }

    /// <summary>Gets whether the borrowed handle is valid.</summary>
    public bool IsValid => Handle != nint.Zero;

    /// <summary>Creates an owned placement iterator.</summary>
    public GhosttyKittyGraphicsPlacementIterator CreatePlacementIterator()
    {
        return new GhosttyKittyGraphicsPlacementIterator();
    }

    /// <summary>Populates an iterator from this graphics storage.</summary>
    public void Populate(GhosttyKittyGraphicsPlacementIterator iterator)
    {
        ArgumentNullException.ThrowIfNull(iterator);
        iterator.Populate(this);
    }

    /// <summary>Looks up an image by image id.</summary>
    public bool TryGetImage(uint imageId, out GhosttyKittyGraphicsImage image)
    {
        nint handle = GhosttyVtNative.KittyGraphicsImage(Handle, imageId);
        if (handle == nint.Zero)
        {
            image = default;
            return false;
        }

        image = new GhosttyKittyGraphicsImage(_terminal, handle);
        return true;
    }
}

/// <summary>
/// Borrowed handle to a Kitty Graphics image.
/// </summary>
public readonly struct GhosttyKittyGraphicsImage
{
    private readonly GhosttyTerminal _terminal;
    private readonly nint _handle;

    internal GhosttyKittyGraphicsImage(GhosttyTerminal terminal, nint handle)
    {
        _terminal = terminal;
        _handle = handle;
    }

    internal nint Handle => _handle;

    /// <summary>Gets whether the borrowed handle is valid.</summary>
    public bool IsValid => _handle != nint.Zero;

    /// <summary>Gets the image id.</summary>
    public uint GetId() => GetValue<uint>(GhosttyVtNative.GhosttyKittyGraphicsImageData.Id);

    /// <summary>Gets the image number.</summary>
    public uint GetNumber() => GetValue<uint>(GhosttyVtNative.GhosttyKittyGraphicsImageData.Number);

    /// <summary>Gets the image width in pixels.</summary>
    public uint GetWidth() => GetValue<uint>(GhosttyVtNative.GhosttyKittyGraphicsImageData.Width);

    /// <summary>Gets the image height in pixels.</summary>
    public uint GetHeight() => GetValue<uint>(GhosttyVtNative.GhosttyKittyGraphicsImageData.Height);

    /// <summary>Gets the image format.</summary>
    public GhosttyVtNative.GhosttyKittyImageFormat GetFormat()
        => GetValue<GhosttyVtNative.GhosttyKittyImageFormat>(GhosttyVtNative.GhosttyKittyGraphicsImageData.Format);

    /// <summary>Gets the image compression mode.</summary>
    public GhosttyVtNative.GhosttyKittyImageCompression GetCompression()
        => GetValue<GhosttyVtNative.GhosttyKittyImageCompression>(GhosttyVtNative.GhosttyKittyGraphicsImageData.Compression);

    /// <summary>Copies the decoded image pixel payload.</summary>
    public unsafe byte[] CopyData()
    {
        nint dataPtr = GetValue<nint>(GhosttyVtNative.GhosttyKittyGraphicsImageData.DataPtr);
        nuint dataLength = GetValue<nuint>(GhosttyVtNative.GhosttyKittyGraphicsImageData.DataLength);
        if (dataPtr == nint.Zero || dataLength == 0)
        {
            return [];
        }

        byte[] data = new byte[checked((int)dataLength)];
        Marshal.Copy(dataPtr, data, 0, data.Length);
        return data;
    }

    private unsafe T GetValue<T>(GhosttyVtNative.GhosttyKittyGraphicsImageData data) where T : unmanaged
    {
        T value = default;
        ThrowIfFailed(
            GhosttyVtNative.KittyGraphicsImageGet(_handle, data, &value),
            $"ghostty_kitty_graphics_image_get({data})");
        return value;
    }

    private static void ThrowIfFailed(GhosttyVtNative.GhosttyResult result, string operation)
    {
        if (result == GhosttyVtNative.GhosttyResult.Success)
        {
            return;
        }

        throw new InvalidOperationException($"{operation} failed with {result}.");
    }
}

/// <summary>
/// Owned iterator over Kitty Graphics placements.
/// </summary>
public sealed class GhosttyKittyGraphicsPlacementIterator : IDisposable
{
    private nint _handle;
    private bool _disposed;

    /// <summary>
    /// Creates a new placement iterator.
    /// </summary>
    public GhosttyKittyGraphicsPlacementIterator()
    {
        NativeLibraryLoader.Initialize();
        ThrowIfFailed(
            GhosttyVtNative.KittyGraphicsPlacementIteratorNew(nint.Zero, out _handle),
            "ghostty_kitty_graphics_placement_iterator_new");
    }

    internal nint Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _handle;
        }
    }

    /// <summary>Gets whether the iterator handle is valid.</summary>
    public bool IsValid => _handle != nint.Zero && !_disposed;

    /// <summary>Populates this iterator from the given graphics storage.</summary>
    public unsafe void Populate(GhosttyKittyGraphics graphics)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(graphics);
        nint iterator = _handle;
        ThrowIfFailed(
            GhosttyVtNative.KittyGraphicsGet(graphics.Handle, GhosttyVtNative.GhosttyKittyGraphicsData.PlacementIterator, &iterator),
            "ghostty_kitty_graphics_get(placement_iterator)");
        _handle = iterator;
    }

    /// <summary>Filters placements by z-layer.</summary>
    public unsafe void SetLayer(GhosttyVtNative.GhosttyKittyPlacementLayer layer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.GhosttyKittyPlacementLayer copy = layer;
        ThrowIfFailed(
            GhosttyVtNative.KittyGraphicsPlacementIteratorSet(
                _handle,
                GhosttyVtNative.GhosttyKittyGraphicsPlacementIteratorOption.Layer,
                &copy),
            "ghostty_kitty_graphics_placement_iterator_set(layer)");
    }

    /// <summary>Advances to the next placement.</summary>
    public bool MoveNext()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return GhosttyVtNative.KittyGraphicsPlacementNext(_handle);
    }

    /// <summary>Gets the current placement's image id.</summary>
    public uint GetImageId() => GetPlacementValue<uint>(GhosttyVtNative.GhosttyKittyGraphicsPlacementData.ImageId);

    /// <summary>Gets the current placement id.</summary>
    public uint GetPlacementId() => GetPlacementValue<uint>(GhosttyVtNative.GhosttyKittyGraphicsPlacementData.PlacementId);

    /// <summary>Gets whether the current placement is virtual.</summary>
    public bool GetIsVirtual() => GetPlacementValue<bool>(GhosttyVtNative.GhosttyKittyGraphicsPlacementData.IsVirtual);

    /// <summary>Gets the x pixel offset inside the starting cell.</summary>
    public uint GetXOffset() => GetPlacementValue<uint>(GhosttyVtNative.GhosttyKittyGraphicsPlacementData.XOffset);

    /// <summary>Gets the y pixel offset inside the starting cell.</summary>
    public uint GetYOffset() => GetPlacementValue<uint>(GhosttyVtNative.GhosttyKittyGraphicsPlacementData.YOffset);

    /// <summary>Gets the current placement z index.</summary>
    public int GetZIndex() => GetPlacementValue<int>(GhosttyVtNative.GhosttyKittyGraphicsPlacementData.Z);

    /// <summary>Resolves the current placement's source rectangle.</summary>
    public unsafe bool TryGetSourceRect(
        GhosttyKittyGraphicsImage image,
        out uint x,
        out uint y,
        out uint width,
        out uint height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        uint sourceX = 0;
        uint sourceY = 0;
        uint sourceWidth = 0;
        uint sourceHeight = 0;
        GhosttyVtNative.GhosttyResult result = GhosttyVtNative.KittyGraphicsPlacementSourceRect(
            _handle,
            image.Handle,
            &sourceX,
            &sourceY,
            &sourceWidth,
            &sourceHeight);
        if (result != GhosttyVtNative.GhosttyResult.Success)
        {
            x = y = width = height = 0;
            return false;
        }

        x = sourceX;
        y = sourceY;
        width = sourceWidth;
        height = sourceHeight;
        return true;
    }

    /// <summary>Gets the rendered pixel size of the current placement.</summary>
    public unsafe bool TryGetPixelSize(
        GhosttyKittyGraphicsImage image,
        GhosttyTerminal terminal,
        out uint width,
        out uint height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        uint pixelWidth = 0;
        uint pixelHeight = 0;
        GhosttyVtNative.GhosttyResult result = GhosttyVtNative.KittyGraphicsPlacementPixelSize(
            _handle,
            image.Handle,
            terminal.Handle,
            &pixelWidth,
            &pixelHeight);
        if (result != GhosttyVtNative.GhosttyResult.Success)
        {
            width = height = 0;
            return false;
        }

        width = pixelWidth;
        height = pixelHeight;
        return true;
    }

    /// <summary>Gets the grid size of the current placement.</summary>
    public unsafe bool TryGetGridSize(
        GhosttyKittyGraphicsImage image,
        GhosttyTerminal terminal,
        out uint columns,
        out uint rows)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        uint valueColumns = 0;
        uint valueRows = 0;
        GhosttyVtNative.GhosttyResult result = GhosttyVtNative.KittyGraphicsPlacementGridSize(
            _handle,
            image.Handle,
            terminal.Handle,
            &valueColumns,
            &valueRows);
        if (result != GhosttyVtNative.GhosttyResult.Success)
        {
            columns = rows = 0;
            return false;
        }

        columns = valueColumns;
        rows = valueRows;
        return true;
    }

    /// <summary>Gets the viewport-relative origin of the current placement.</summary>
    public unsafe bool TryGetViewportPosition(
        GhosttyKittyGraphicsImage image,
        GhosttyTerminal terminal,
        out int column,
        out int row)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int valueColumn = 0;
        int valueRow = 0;
        GhosttyVtNative.GhosttyResult result = GhosttyVtNative.KittyGraphicsPlacementViewportPosition(
            _handle,
            image.Handle,
            terminal.Handle,
            &valueColumn,
            &valueRow);
        if (result != GhosttyVtNative.GhosttyResult.Success)
        {
            column = row = 0;
            return false;
        }

        column = valueColumn;
        row = valueRow;
        return true;
    }

    /// <summary>Gets the grid rectangle of the current placement.</summary>
    public unsafe bool TryGetRect(
        GhosttyKittyGraphicsImage image,
        GhosttyTerminal terminal,
        out GhosttySelection selection)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.GhosttySelectionRange native = GhosttyVtNative.GhosttySelectionRange.CreateSized();
        GhosttyVtNative.GhosttyResult result = GhosttyVtNative.KittyGraphicsPlacementRect(
            _handle,
            image.Handle,
            terminal.Handle,
            &native);
        if (result != GhosttyVtNative.GhosttyResult.Success)
        {
            selection = default;
            return false;
        }

        selection = new GhosttySelection(native.Start, native.End, native.Rectangle);
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
        if (_handle != nint.Zero)
        {
            GhosttyVtNative.KittyGraphicsPlacementIteratorFree(_handle);
            _handle = nint.Zero;
        }
    }

    private unsafe T GetPlacementValue<T>(GhosttyVtNative.GhosttyKittyGraphicsPlacementData data) where T : unmanaged
    {
        T value = default;
        ThrowIfFailed(
            GhosttyVtNative.KittyGraphicsPlacementGet(_handle, data, &value),
            $"ghostty_kitty_graphics_placement_get({data})");
        return value;
    }

    private static void ThrowIfFailed(GhosttyVtNative.GhosttyResult result, string operation)
    {
        if (result == GhosttyVtNative.GhosttyResult.Success)
        {
            return;
        }

        throw new InvalidOperationException($"{operation} failed with {result}.");
    }
}
