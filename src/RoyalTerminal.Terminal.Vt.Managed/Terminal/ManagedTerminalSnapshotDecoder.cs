// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Incremental Ghostty binary snapshot restoration. READY transfers terminal ownership to the
/// caller; disposing or failing this decoder never disposes that terminal or the source stream.
/// Calls must be serialized with all access to the restored processor and screen.
/// </summary>
public sealed class ManagedTerminalSnapshotDecoder : IDisposable
{
    private readonly GhosttySnapshotStateReader _reader;
    private readonly ManagedTerminalSnapshotOptions _options;
    private ManagedTerminalSnapshot? _terminal;
    private GhosttySnapshotHistoryApplication? _history;
    private bool _failed;
    private bool _disposed;

    /// <summary>Creates a decoder borrowing immutable source memory until FINISH or disposal.</summary>
    public ManagedTerminalSnapshotDecoder(ReadOnlyMemory<byte> source, ManagedTerminalSnapshotOptions? options = null)
    {
        _options = options ?? new();
        _options.Validate();
        _reader = new(source, _options.DecodeLimits);
    }

    /// <summary>Creates a decoder borrowing a readable stream; it never reads beyond FINISH or closes it.</summary>
    public ManagedTerminalSnapshotDecoder(Stream source, ManagedTerminalSnapshotOptions? options = null)
    {
        _options = options ?? new();
        _options.Validate();
        _reader = new(source, _options.DecodeLimits);
    }

    /// <summary>Bytes consumed from the snapshot start, including records discarded during reconciliation.</summary>
    public long SourceOffset => _reader.SourceOffset;

    /// <summary>
    /// Transactionally restores resident state and replays its continuation exactly once.
    /// May be called once. The returned terminal remains usable if later history is corrupt.
    /// </summary>
    public ManagedTerminalSnapshot Ready()
    {
        EnsureUsable();
        if (_terminal is not null) throw new InvalidOperationException("Snapshot READY was already consumed.");
        BasicVtProcessor? processor = null;
        try
        {
            GhosttySnapshotReadyState ready = _reader.ReadReady();
            TerminalScreen screen = GhosttySnapshotLiveScreen.Stage(ready, _options.Theme, _options.ScrollbackLimit);
            screen.SnapshotScrollbackQuota = _options.ScrollbackQuota ?? new()
            {
                MaximumBytes = ready.Terminal.Header.MaximumScrollbackBytes,
                MaximumRows = ready.Terminal.Header.MaximumScrollbackRows,
            };
            BasicVtProcessorOptions policy = _options.ProcessorOptions;
            // Verify even when the caller does not want ongoing retention. Retention can
            // be disabled after replay without changing parser state or replaying again.
            policy = policy with { ContinuationMaxBytes = Math.Max(1, Math.Max(policy.ContinuationMaxBytes, ready.Continuation.Length)) };
            processor = new(screen, policy);
            processor.InstallSnapshot(ready, _options.Theme, _options.ProcessorOptions.ContinuationMaxBytes != 0);
            ulong primary = 0, alternate = 0;
            foreach (GhosttySnapshotScreen state in ready.Screens)
                if (state.State.Key == 0) primary = state.State.HistoryRows;
                else alternate = state.State.HistoryRows;
            _history = new(screen);
            _terminal = new(screen, processor, primary, alternate);
            return _terminal;
        }
        catch { processor?.Dispose(); _failed = true; throw; }
    }

    /// <summary>
    /// Consumes and validates one history page or Kitty image/frame payload, returning its
    /// application result, or null after FINISH (idempotent). Dropped pages cannot later fill
    /// gaps. On failure no more calls are valid, but the restored terminal remains usable.
    /// </summary>
    public ManagedTerminalSnapshotProgress? Next()
    {
        EnsureUsable();
        if (_terminal is null) throw new InvalidOperationException("Read snapshot READY before history.");
        ObjectDisposedException.ThrowIf(_terminal.IsDisposed, _terminal);
        try
        {
            if (_reader.ReadNext() is not { } next) return null;
            if (next.KittyPayload is { } payload)
            {
                int applied = _terminal.Processor.ApplySnapshotKittyPixels(payload);
                return new(payload.Key, 0, 0) { Kind = ManagedTerminalSnapshotProgressKind.KittyImage,
                    ImageId = payload.ImageId, ImageFrame = payload.Frame, ImageBytesApplied = applied };
            }
            GhosttySnapshotHistoryPage page = next.History!.Value;
            GhosttySnapshotHistoryProgress progress = _terminal.Processor.ApplySnapshotHistory(_history!, page);
            if (progress.Rows > 0 && page.KittyPlacements is { } placements)
                _terminal.Processor.ApplySnapshotKittyHistory(page.Key, page.Page, placements);
            if (_options.CompressHistory && progress.Rows > 0)
                _terminal.Processor.CompressSnapshotHistory(progress.Key, progress.Rows);
            return new(progress.Key, progress.Rows, progress.Remaining);
        }
        catch { _failed = true; throw; }
    }

    /// <summary>Releases decoder buffers without disposing the returned terminal or closing the source.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _reader.Dispose();
    }

    private void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_failed) throw new InvalidOperationException("Snapshot decoder is invalid after an earlier failure.");
    }
}

/// <summary>Result of consuming a history page or Kitty payload, including records discarded after live changes or quota exhaustion.</summary>
/// <param name="ScreenKey">Zero for primary, one for alternate.</param>
/// <param name="RowsApplied">Actual rows prepended; zero for image progress or when the page was discarded.</param>
/// <param name="PagesRemaining">Remaining pages in this screen's history sequence; zero for image progress.</param>
public readonly record struct ManagedTerminalSnapshotProgress(int ScreenKey, int RowsApplied, uint PagesRemaining)
{
    /// <summary>The kind of record consumed by this progress step.</summary>
    public ManagedTerminalSnapshotProgressKind Kind { get; init; }
    /// <summary>Image identity for image progress; zero for history progress.</summary>
    public uint ImageId { get; init; }
    /// <summary>One-based frame number for image progress; zero for history.</summary>
    public uint ImageFrame { get; init; }
    /// <summary>Pixel bytes installed; zero when live input or a quota retired the image.</summary>
    public int ImageBytesApplied { get; init; }
}

/// <summary>Incremental snapshot restoration work returned after READY.</summary>
public enum ManagedTerminalSnapshotProgressKind
{
    /// <summary>A complete history page was applied or discarded.</summary>
    History,
    /// <summary>A Kitty image or animation frame payload was applied or discarded.</summary>
    KittyImage,
}
