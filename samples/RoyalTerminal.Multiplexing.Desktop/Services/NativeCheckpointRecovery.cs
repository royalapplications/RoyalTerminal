// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia.Threading;
using RoyalTerminal.Multiplexing.Desktop.Controls;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Desktop.Services;

internal sealed record NativeCheckpointStart(IVtProcessor Processor, long Sequence, int? ExitCode);

internal static class NativeCheckpointRecovery
{
    internal static async ValueTask<NativeCheckpointStart> RecoverAsync(RemoteTerminalControl control, HttpClient http,
        MultiplexingTransportOptions options, CancellationToken token)
    {
        MultiplexingCheckpoint cut = await MultiplexingCheckpointClient.ReadAsync(http, options, token).ConfigureAwait(false);
        var replica = await Task.Run(() => ManagedTerminalSnapshot.Restore(cut.Binary, new()
        {
            DecodeLimits = new(MaximumPayloadBytes: 4 << 20, MaximumTotalPayloadBytes: 8 << 20, MaximumCells: 1 << 20,
                MaximumPages: 4096, MaximumSuffixCodepointsPerPage: 256 << 10, MaximumStringBytesPerRecord: 256 << 10,
                MaximumContinuationBytes: 64 << 10),
            ScrollbackQuota = new() { MaximumBytes = 16 << 20 },
            ProcessorOptions = new() { GlyphProtocolEnabled = false, SixelGraphicsEnabled = false,
                KittyGraphicsStorageLimitBytes = 0, KittyGraphicsMaxApcBytes = 0, KittyGraphicsMaxImageBytes = 0,
                ClipboardWriteLimitBytes = 0, UnknownSequenceMaxBytes = 0 },
        }), token).ConfigureAwait(false);
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                // Arrange the OLD parser first. The incoming parser already has exact remote pixel geometry.
                control.ApplyRemoteDimensions(new(replica.Screen.Columns, replica.Screen.ViewportRows, 0, 0));
                control.AdoptManagedReplica(replica);
            });
        }
        catch { if (!ReferenceEquals(control.Screen, replica.Screen)) replica.Dispose(); throw; }
        return new(replica.Processor, cut.Sequence, cut.ExitCode);
    }
}
