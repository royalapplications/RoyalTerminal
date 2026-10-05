// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RoyalTerminal.GhosttySharp.Native;

public static partial class GhosttyVtNative
{
    public enum GhosttySnapshotDecoderOption : int
    {
        MaxContinuationBytes = 0,
        RetainContinuation = 1,
    }

    public enum GhosttySnapshotDecoderData : int
    {
        Invalid = 0,
        MaxContinuationBytes = 1,
        SourceOffset = 2,
        HistoryRowsPrimary = 3,
        HistoryRowsAlternate = 4,
        ProgressScreen = 5,
        ProgressRows = 6,
        ProgressRemaining = 7,
        RetainContinuation = 8,
    }

    [LibraryImport(LibName, EntryPoint = "ghostty_snapshot_encode")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult SnapshotEncode(nint terminal, GhosttyWriter writer);

    [LibraryImport(LibName, EntryPoint = "ghostty_snapshot_encode_buf")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult SnapshotEncodeBuffer(
        nint terminal,
        byte* buffer,
        nuint bufferLength,
        out nuint written);

    [LibraryImport(LibName, EntryPoint = "ghostty_snapshot_encode_alloc")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult SnapshotEncodeAlloc(
        nint terminal,
        GhosttyAllocator* allocator,
        byte** output,
        out nuint outputLength);

    [LibraryImport(LibName, EntryPoint = "ghostty_snapshot_decoder_new")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult SnapshotDecoderNew(
        nint allocator,
        out nint decoder,
        GhosttyReader reader);

    [LibraryImport(LibName, EntryPoint = "ghostty_snapshot_decoder_new_buf")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult SnapshotDecoderNewBuffer(
        nint allocator,
        out nint decoder,
        byte* buffer,
        nuint bufferLength);

    [LibraryImport(LibName, EntryPoint = "ghostty_snapshot_decoder_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SnapshotDecoderFree(nint decoder);

    [LibraryImport(LibName, EntryPoint = "ghostty_snapshot_decoder_set")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult SnapshotDecoderSet(
        nint decoder,
        GhosttySnapshotDecoderOption option,
        void* value);

    [LibraryImport(LibName, EntryPoint = "ghostty_snapshot_decoder_ready")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult SnapshotDecoderReady(nint decoder, out nint terminal);

    [LibraryImport(LibName, EntryPoint = "ghostty_snapshot_decoder_next")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult SnapshotDecoderNext(nint decoder);

    [LibraryImport(LibName, EntryPoint = "ghostty_snapshot_decoder_decode")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial GhosttyResult SnapshotDecoderDecode(nint decoder, out nint terminal);

    [LibraryImport(LibName, EntryPoint = "ghostty_snapshot_decoder_get")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult SnapshotDecoderGet(
        nint decoder,
        GhosttySnapshotDecoderData data,
        void* value);

    [LibraryImport(LibName, EntryPoint = "ghostty_snapshot_decoder_get_multi")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static unsafe partial GhosttyResult SnapshotDecoderGetMulti(
        nint decoder,
        nuint count,
        GhosttySnapshotDecoderData* keys,
        void** values,
        nuint* written);
}
