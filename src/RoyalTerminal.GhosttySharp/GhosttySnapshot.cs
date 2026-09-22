// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using RoyalTerminal.GhosttySharp.Native;

namespace RoyalTerminal.GhosttySharp;

/// <summary>One-shot helpers for Ghostty's binary terminal snapshot format.</summary>
public static class GhosttySnapshot
{
    /// <summary>Encodes the complete terminal state into a binary snapshot.</summary>
    public static unsafe byte[] Encode(GhosttyTerminal terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        nuint required = 0;
        GhosttyVtNative.GhosttyResult probe = GhosttyVtNative.SnapshotEncodeBuffer(
            terminal.Handle,
            null,
            0,
            out required);
        if (probe != GhosttyVtNative.GhosttyResult.OutOfSpace)
        {
            ThrowIfFailed(probe, "ghostty_snapshot_encode_buf(probe)");
        }

        byte[] result = new byte[checked((int)required)];
        fixed (byte* pointer = result)
        {
            ThrowIfFailed(
                GhosttyVtNative.SnapshotEncodeBuffer(
                    terminal.Handle,
                    pointer,
                    (nuint)result.Length,
                    out nuint written),
                "ghostty_snapshot_encode_buf");
            if (written != (nuint)result.Length)
            {
                Array.Resize(ref result, checked((int)written));
            }
        }

        return result;
    }

    /// <summary>Streams the complete terminal state without an intermediate native buffer.</summary>
    public static void WriteTo(GhosttyTerminal terminal, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        GhosttyStreamWriter.Write(
            destination,
            writer => GhosttyVtNative.SnapshotEncode(terminal.Handle, writer),
            "ghostty_snapshot_encode");
    }

    /// <summary>Decodes a complete binary snapshot into a new owned terminal.</summary>
    public static GhosttyTerminal Decode(byte[] snapshot, bool retainContinuation = false)
    {
        using GhosttySnapshotDecoder decoder = new(snapshot);
        decoder.SetRetainContinuation(retainContinuation);
        return decoder.Decode();
    }

    private static void ThrowIfFailed(GhosttyVtNative.GhosttyResult result, string operation)
    {
        if (result != GhosttyVtNative.GhosttyResult.Success)
        {
            throw new InvalidOperationException($"{operation} failed with {result}.");
        }
    }
}

/// <summary>
/// Incremental decoder for Ghostty binary snapshots. The source buffer remains pinned
/// until the decoder reaches the end or is disposed.
/// </summary>
public sealed class GhosttySnapshotDecoder : IDisposable
{
    private readonly byte[]? _source;
    private readonly ReaderContext? _readerContext;
    private GCHandle _sourceHandle;
    private GCHandle _readerContextHandle;
    private nint _handle;
    private GhosttyTerminal.NativeLifetimeLease? _terminalLease;
    private bool _disposed;

    /// <summary>Creates a decoder over a borrowed managed byte array.</summary>
    public unsafe GhosttySnapshotDecoder(byte[] snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        NativeLibraryLoader.Initialize();
        _source = snapshot;
        _sourceHandle = GCHandle.Alloc(_source, GCHandleType.Pinned);
        try
        {
            byte* pointer = snapshot.Length == 0
                ? null
                : (byte*)_sourceHandle.AddrOfPinnedObject();
            ThrowIfFailed(
                GhosttyVtNative.SnapshotDecoderNewBuffer(
                    nint.Zero,
                    out _handle,
                    pointer,
                    (nuint)snapshot.Length),
                "ghostty_snapshot_decoder_new_buf");
        }
        catch
        {
            _sourceHandle.Free();
            throw;
        }
    }

    /// <summary>
    /// Creates a decoder over a synchronous managed stream. The stream remains caller-owned
    /// and must stay readable until the decoder is disposed.
    /// </summary>
    public unsafe GhosttySnapshotDecoder(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException("The source stream is not readable.", nameof(source));
        }

        NativeLibraryLoader.Initialize();
        _readerContext = new ReaderContext(source);
        _readerContextHandle = GCHandle.Alloc(_readerContext);
        try
        {
            GhosttyVtNative.GhosttyReader reader = new(
                (nint)(delegate* unmanaged[Cdecl]<nint, byte*, nuint, nuint*, byte>)&Read,
                GCHandle.ToIntPtr(_readerContextHandle));
            ThrowIfFailed(
                GhosttyVtNative.SnapshotDecoderNew(nint.Zero, out _handle, reader),
                "ghostty_snapshot_decoder_new");
        }
        catch
        {
            _readerContextHandle.Free();
            throw;
        }
    }

    /// <summary>Gets whether the native decoder handle is valid.</summary>
    public bool IsValid => _handle != nint.Zero && !_disposed;

    /// <summary>Sets the maximum accepted unfinished VT continuation size.</summary>
    public unsafe void SetMaxContinuationBytes(nuint value)
    {
        SetOption(GhosttyVtNative.GhosttySnapshotDecoderOption.MaxContinuationBytes, &value);
    }

    /// <summary>Sets whether decoded VT continuation tracking remains enabled.</summary>
    public unsafe void SetRetainContinuation(bool value)
    {
        SetOption(GhosttyVtNative.GhosttySnapshotDecoderOption.RetainContinuation, &value);
    }

    /// <summary>Decodes the renderable prefix and returns its caller-owned terminal.</summary>
    public GhosttyTerminal Ready()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.GhosttyResult nativeResult =
            GhosttyVtNative.SnapshotDecoderReady(_handle, out nint terminal);
        ThrowIfReaderFailed();
        ThrowIfFailed(nativeResult, "ghostty_snapshot_decoder_ready");
        GhosttyTerminal result = new(terminal, ownsHandle: true);
        _terminalLease = result.AcquireNativeLifetimeLease();
        return result;
    }

    /// <summary>Decodes one additional history page; returns false after FINISH.</summary>
    public bool Next()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.GhosttyResult result = GhosttyVtNative.SnapshotDecoderNext(_handle);
        ThrowIfReaderFailed();
        if (result == GhosttyVtNative.GhosttyResult.NoValue)
        {
            ReleaseTerminalLease();
            return false;
        }

        ThrowIfFailed(result, "ghostty_snapshot_decoder_next");
        return true;
    }

    /// <summary>Decodes the entire snapshot into a caller-owned terminal.</summary>
    public GhosttyTerminal Decode()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GhosttyVtNative.GhosttyResult result =
            GhosttyVtNative.SnapshotDecoderDecode(_handle, out nint terminal);
        ThrowIfReaderFailed();
        ThrowIfFailed(result, "ghostty_snapshot_decoder_decode");
        return new GhosttyTerminal(terminal, ownsHandle: true);
    }

    /// <summary>Gets the number of source bytes consumed so far.</summary>
    public nuint GetSourceOffset()
        => GetValue<nuint>(GhosttyVtNative.GhosttySnapshotDecoderData.SourceOffset);

    /// <summary>Gets the configured maximum unfinished VT continuation size.</summary>
    public nuint GetMaxContinuationBytes()
        => GetValue<nuint>(GhosttyVtNative.GhosttySnapshotDecoderData.MaxContinuationBytes);

    /// <summary>Gets whether decoded terminals retain ongoing continuation tracking.</summary>
    public bool GetRetainContinuation()
        => GetValue<bool>(GhosttyVtNative.GhosttySnapshotDecoderData.RetainContinuation);

    /// <summary>Gets the declared primary-screen history extent.</summary>
    public ulong GetPrimaryHistoryRows()
        => GetValue<ulong>(GhosttyVtNative.GhosttySnapshotDecoderData.HistoryRowsPrimary);

    /// <summary>Gets the declared alternate-screen history extent, or null if absent.</summary>
    public ulong? GetAlternateHistoryRows()
        => TryGetValue(GhosttyVtNative.GhosttySnapshotDecoderData.HistoryRowsAlternate, out ulong value)
            ? value
            : null;

    /// <summary>Gets the screen updated by the most recently decoded history page.</summary>
    public GhosttyVtNative.GhosttyTerminalScreen? GetProgressScreen()
        => TryGetValue(GhosttyVtNative.GhosttySnapshotDecoderData.ProgressScreen,
            out GhosttyVtNative.GhosttyTerminalScreen value) ? value : null;

    /// <summary>Gets the rows prepended by the most recently decoded history page.</summary>
    public nuint GetProgressRows()
        => GetValue<nuint>(GhosttyVtNative.GhosttySnapshotDecoderData.ProgressRows);

    /// <summary>Gets the pages remaining in the current screen history sequence.</summary>
    public uint GetProgressRemaining()
        => GetValue<uint>(GhosttyVtNative.GhosttySnapshotDecoderData.ProgressRemaining);

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
            GhosttyVtNative.SnapshotDecoderFree(_handle);
            _handle = nint.Zero;
        }

        ReleaseTerminalLease();

        if (_sourceHandle.IsAllocated)
        {
            _sourceHandle.Free();
        }

        if (_readerContextHandle.IsAllocated)
        {
            _readerContextHandle.Free();
        }

        GC.KeepAlive(_source);
        GC.KeepAlive(_readerContext);
    }

    private void ReleaseTerminalLease()
    {
        GhosttyTerminal.NativeLifetimeLease? lease = Interlocked.Exchange(ref _terminalLease, null);
        lease?.Dispose();
    }

    private unsafe void SetOption(GhosttyVtNative.GhosttySnapshotDecoderOption option, void* value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfFailed(GhosttyVtNative.SnapshotDecoderSet(_handle, option, value),
            $"ghostty_snapshot_decoder_set({option})");
    }

    private unsafe T GetValue<T>(GhosttyVtNative.GhosttySnapshotDecoderData data) where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        T value = default;
        ThrowIfFailed(GhosttyVtNative.SnapshotDecoderGet(_handle, data, &value),
            $"ghostty_snapshot_decoder_get({data})");
        return value;
    }

    private unsafe bool TryGetValue<T>(GhosttyVtNative.GhosttySnapshotDecoderData data, out T value)
        where T : unmanaged
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        T resultValue = default;
        GhosttyVtNative.GhosttyResult result = GhosttyVtNative.SnapshotDecoderGet(_handle, data, &resultValue);
        if (result == GhosttyVtNative.GhosttyResult.NoValue)
        {
            value = default;
            return false;
        }

        ThrowIfFailed(result, $"ghostty_snapshot_decoder_get({data})");
        value = resultValue;
        return true;
    }

    private static void ThrowIfFailed(GhosttyVtNative.GhosttyResult result, string operation)
    {
        if (result != GhosttyVtNative.GhosttyResult.Success)
        {
            throw new InvalidOperationException($"{operation} failed with {result}.");
        }
    }

    private void ThrowIfReaderFailed()
    {
        _readerContext?.Failure?.Throw();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe byte Read(nint userdata, byte* buffer, nuint capacity, nuint* outRead)
    {
        if (outRead is not null)
        {
            *outRead = 0;
        }

        ReaderContext? context = GCHandle.FromIntPtr(userdata).Target as ReaderContext;
        if (context is null || buffer is null || outRead is null)
        {
            return 0;
        }

        try
        {
            int length = capacity > int.MaxValue ? int.MaxValue : (int)capacity;
            int read = context.Stream.Read(new Span<byte>(buffer, length));
            *outRead = (nuint)read;
            return 1;
        }
        catch (Exception exception)
        {
            context.Failure = ExceptionDispatchInfo.Capture(exception);
            return 0;
        }
    }

    private sealed class ReaderContext(Stream stream)
    {
        internal Stream Stream { get; } = stream;

        internal ExceptionDispatchInfo? Failure { get; set; }
    }
}
