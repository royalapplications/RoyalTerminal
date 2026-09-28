// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia.Threading;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Services;

// UI-owned state machine: one live request and one replaceable pending target.
// Cancellation never abandons the live slot, including non-cooperating hosts.
internal sealed class TerminalHyperlinkPreviewCoordinator(Action<string?, string?> publish)
{
    private string? _target;
    private ITerminalHyperlinkPathPreviewSource? _source;
    private CancellationTokenSource? _active;
    private long _generation;
    private bool _pending;

    internal ITerminalHyperlinkPathPreviewSource? Source => _source;

    internal void Update(string? target, ITerminalHyperlinkPathPreviewSource? source)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (string.Equals(target, _target, StringComparison.Ordinal) && ReferenceEquals(source, _source)) return;
        _target = target;
        _source = source;
        _generation++;
        _pending = target is not null && source is not null && IsPathTarget(target);
        publish(target, target is null ? null : TerminalHyperlinkSafety.SanitizeDisplay(target));
        CancelActive();
        StartLatest();
    }

    internal void Cancel()
    {
        Dispatcher.UIThread.VerifyAccess();
        _target = null;
        _source = null;
        _pending = false;
        _generation++;
        CancelActive();
        publish(null, null);
    }

    private static bool IsPathTarget(string target)
    {
        if (target.AsSpan().StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return true;
        int colon = target.IndexOf(':');
        return colon <= 0 || !Uri.CheckSchemeName(target[..colon]);
    }

    private void CancelActive()
    {
        try { _active?.Cancel(); }
        catch (AggregateException) { /* A host callback cannot break hover/session cleanup. */ }
    }

    private void StartLatest()
    {
        if (_active is not null || !_pending || _source is null || _target is null) return;
        _pending = false;
        CancellationTokenSource cancellation = new();
        _active = cancellation;
        _ = ResolveAsync(_target, _source, _generation, cancellation);
    }

    private async Task ResolveAsync(string target, ITerminalHyperlinkPathPreviewSource source, long generation,
        CancellationTokenSource cancellation)
    {
        try
        {
            string display = await source.GetPathPreviewAsync(target, cancellation.Token);
            if (!cancellation.IsCancellationRequested && generation == _generation)
                publish(target, TerminalHyperlinkSafety.SanitizeDisplay(display ?? target));
        }
        catch { /* Retain the escaped fallback; a preview failure never opens a target. */ }
        finally
        {
            _active = null;
            cancellation.Dispose();
            StartLatest();
        }
    }
}
