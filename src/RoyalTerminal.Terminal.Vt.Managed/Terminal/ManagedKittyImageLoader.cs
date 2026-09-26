// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace RoyalTerminal.Terminal;

/// <summary>Chunk accumulation, bounded decompression and decoded-pixel validation.</summary>
internal sealed class ManagedKittyImageLoader
{
    private ManagedKittyImageBuffer _buffer;
    private readonly IKittyGraphicsPngDecoder? _pngDecoder;
    private readonly int _limit;

    private ManagedKittyImageLoader(ManagedKittyGraphicsCommand command, IKittyGraphicsPngDecoder? pngDecoder, int limit, ReadOnlyMemory<byte> data)
    {
        InitialCommand = command;
        Quiet = command.Quiet;
        _pngDecoder = pngDecoder;
        _limit = limit;
        _buffer = new ManagedKittyImageBuffer(limit, data);
    }

    internal ManagedKittyGraphicsCommand InitialCommand { get; }
    internal int Quiet { get; private set; }

    internal static bool TryCreate(ManagedKittyGraphicsCommand command, IKittyGraphicsPngDecoder? pngDecoder, int maxBytes,
        [NotNullWhen(true)] out ManagedKittyImageLoader? loader, out string error, IKittyGraphicsMediumReader? mediumReader = null)
    {
        loader = null;
        error = "EINVAL: unsupported format";
        if (command.Get('f', 32) is not (0 or 24 or 32 or 100)) return false;
        error = "EINVAL: invalid data";
        if (maxBytes < 0 || command.Data.Length > maxBytes) return false;
        ReadOnlyMemory<byte> data = command.Data;
        uint medium = command.Get('t', 'd');
        if (medium != 'd')
        {
            error = "EINVAL: unsupported medium";
            if (mediumReader is null) return false;
            int? expectedBytes = null;
            if (command.Get('f', 32) != 100 && command.Get('o') != 'z')
            {
                uint width = command.Get('s');
                uint height = command.Get('v');
                error = "EINVAL: dimensions required";
                if (width == 0 || height == 0) return false;
                error = "EINVAL: dimensions too large";
                if (width > 10000 || height > 10000) return false;
                expectedBytes = checked((int)(width * height * (command.Get('f', 32) == 24 ? 3u : 4u)));
            }
            KittyGraphicsMediumRequest request = new(medium switch
            {
                'f' => KittyGraphicsMedium.File, 't' => KittyGraphicsMedium.TemporaryFile,
                _ => KittyGraphicsMedium.SharedMemory,
            }, command.Data, command.Get('O'), command.Get('S'), expectedBytes);
            if (!mediumReader.TryRead(request, maxBytes, out byte[]? externalData, out string? failure) || externalData is null)
            {
                error = failure ?? "EINVAL: invalid data";
                return false;
            }
            error = "EINVAL: invalid data";
            if (externalData.Length > maxBytes) return false;
            data = externalData;
        }
        loader = new ManagedKittyImageLoader(command, pngDecoder, maxBytes, data);
        error = "OK";
        return true;
    }

    internal bool TryAppend(ManagedKittyGraphicsCommand continuation, out string error)
    {
        // Metadata belongs to the initial command. Only nonzero quiet values
        // override its response policy while subsequent chunks are received.
        if (continuation.Quiet > 0) Quiet = continuation.Quiet;
        bool success = _buffer.TryAppend(continuation.Data.Span);
        error = success ? "OK" : "EINVAL: invalid data";
        return success;
    }

    internal bool TryComplete([NotNullWhen(true)] out ManagedKittyImagePixels? image, out string error)
    {
        image = null;
        if (InitialCommand.Get('o') == 'z' && !TryInflate())
        {
            error = "EINVAL: decompression failed";
            return false;
        }

        uint format = InitialCommand.Get('f', 32);
        if (format == 100)
        {
            error = "EINVAL: unsupported format";
            if (_pngDecoder is null) return false;
            error = "EINVAL: invalid data";
            if (!_pngDecoder.TryDecode(_buffer.Data.Span, _limit, out KittyGraphicsDecodedImage? decoded)) return false;
            // Enforce bounds even for an injected decoder with weaker limits.
            if (decoded is null || decoded.Width > 10000 || decoded.Height > 10000 || decoded.Rgba.Length > _limit)
                return false;
            image = new(decoded);
            error = "OK";
            return true;
        }

        uint width = InitialCommand.Get('s');
        uint height = InitialCommand.Get('v');
        error = "EINVAL: dimensions required";
        if (width == 0 || height == 0) return false;
        error = "EINVAL: dimensions too large";
        if (width > 10000 || height > 10000) return false;
        int channels = format == 24 ? 3 : 4;
        int expected = checked((int)(width * height * (uint)channels));
        int actual = _buffer.Data.Length;
        bool frame = InitialCommand.Action == 'f';
        error = frame && actual < expected ? "ENODATA: insufficient data" : "EINVAL: invalid data";
        if (actual < expected || (!frame && actual != expected)) return false;
        int rgbaLength = checked((int)(width * height * 4));
        if (rgbaLength > _limit) return false;

        // Keep native RGB storage until publication/composition needs RGBA.
        // The per-image safety limit above still bounds the largest view.
        byte[] pixels = _buffer.Take(expected);
        image = channels == 3
            ? ManagedKittyImagePixels.FromRgb((int)width, (int)height, pixels)
            : new(new KittyGraphicsDecodedImage((int)width, (int)height, pixels));
        error = "OK";
        return true;
    }

    private bool TryInflate()
    {
        if (!MemoryMarshal.TryGetArray(_buffer.Data, out ArraySegment<byte> source) || source.Array is null) return false;
        using MemoryStream input = new(source.Array, source.Offset, source.Count, writable: false);
        using ZLibStream decompressor = new(input, CompressionMode.Decompress);
        ManagedKittyImageBuffer decoded = new(_limit);
        byte[] scratch = ArrayPool<byte>.Shared.Rent(16384);
        try
        {
            int count;
            while ((count = decompressor.Read(scratch)) > 0)
                if (!decoded.TryAppend(scratch.AsSpan(0, count))) return false;
            _buffer = decoded;
            return true;
        }
        catch (InvalidDataException) { return false; }
        catch (IOException) { return false; }
        finally { ArrayPool<byte>.Shared.Return(scratch); }
    }
}
