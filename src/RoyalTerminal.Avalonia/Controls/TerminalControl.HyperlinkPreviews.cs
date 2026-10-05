// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Services;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Controls;

public partial class TerminalControl
{
    /// <summary>Optional display-only file path resolution supplied by the application.</summary>
    public static readonly DirectProperty<TerminalControl, ITerminalHyperlinkPathPreviewSource?> HyperlinkPathPreviewSourceProperty =
        AvaloniaProperty.RegisterDirect<TerminalControl, ITerminalHyperlinkPathPreviewSource?>(nameof(HyperlinkPathPreviewSource),
            control => control.HyperlinkPathPreviewSource, (control, value) => control.HyperlinkPathPreviewSource = value);

    /// <summary>The escaped, optionally canonicalized hover preview published on the UI thread.</summary>
    public static readonly DirectProperty<TerminalControl, string?> HoveredLinkDisplayTextProperty =
        AvaloniaProperty.RegisterDirect<TerminalControl, string?>(nameof(HoveredLinkDisplayText), control => control.HoveredLinkDisplayText);

    private readonly TerminalHyperlinkPreviewCoordinator _hyperlinkPreview;
    private readonly Action _refreshHyperlinkPreview;
    private ITerminalHyperlinkPathPreviewSource? _hyperlinkPathPreviewSource;
    private int _hyperlinkPreviewQueued, _hyperlinkPreviewSuppressed, _hyperlinkPreviewEpoch;
    private int _hyperlinkPreviewAppliedEpoch;
    private bool _hyperlinkPreviewAttached;

    /// <summary>
    /// Gets or sets the application-owned path preview source on the UI thread.
    /// Null keeps an escaped original target. This does not configure the opener.
    /// </summary>
    public ITerminalHyperlinkPathPreviewSource? HyperlinkPathPreviewSource
    {
        get => _hyperlinkPathPreviewSource;
        set
        {
            Dispatcher.UIThread.VerifyAccess();
            if (SetAndRaise(HyperlinkPathPreviewSourceProperty, ref _hyperlinkPathPreviewSource, value)) HyperlinkPreviewTargetChanged();
        }
    }

    private void HyperlinkPreviewTargetChanged()
    {
        // Even A -> B -> A changes coalesced into one dispatcher turn must
        // invalidate the first A operation, not revive its old filesystem result.
        Interlocked.Increment(ref _hyperlinkPreviewEpoch);
        QueueHyperlinkPreviewRefresh();
    }

    private void QueueHyperlinkPreviewRefresh()
    {
        // Output parsing can change the link beneath a stationary pointer.
        // Coalesce those changes without binding notifications under screen locks.
        if (Interlocked.Exchange(ref _hyperlinkPreviewQueued, 1) == 0)
            Dispatcher.UIThread.Post(_refreshHyperlinkPreview);
    }

    private void RefreshHyperlinkPreview()
    {
        Interlocked.Exchange(ref _hyperlinkPreviewQueued, 0);
        int epoch = Volatile.Read(ref _hyperlinkPreviewEpoch);
        if (epoch != _hyperlinkPreviewAppliedEpoch)
        {
            _hyperlinkPreviewAppliedEpoch = epoch;
            _hyperlinkPreview.Cancel();
        }
        if (!_hyperlinkPreviewAttached || Volatile.Read(ref _hyperlinkPreviewSuppressed) != 0)
        {
            _hyperlinkPreview.Cancel();
            return;
        }
        string? target;
        if (_screen is { } screen) { lock (screen.SyncRoot) target = _hoveredLinkUrl; }
        else target = _hoveredLinkUrl;
        _hyperlinkPreview.Update(target, _hyperlinkPathPreviewSource);
    }

    private void PublishHyperlinkPreview(string? target, string? display)
    {
        // A parser mutation may already be queued ahead of its UI refresh.
        // Never publish a completed result for that stale target/source/epoch.
        if (target is not null && (!_hyperlinkPreviewAttached || Volatile.Read(ref _hyperlinkPreviewSuppressed) != 0 ||
            _hyperlinkPreviewAppliedEpoch != Volatile.Read(ref _hyperlinkPreviewEpoch) ||
            !string.Equals(target, Volatile.Read(ref _hoveredLinkUrl), StringComparison.Ordinal) ||
            !ReferenceEquals(_hyperlinkPreview.Source, _hyperlinkPathPreviewSource))) return;
        SetAndRaise(HoveredLinkDisplayTextProperty, ref _hoveredLinkDisplayText, display);
    }

    private void PauseHyperlinkPreview()
    {
        Volatile.Write(ref _hyperlinkPreviewSuppressed, 1);
        int epoch = Interlocked.Increment(ref _hyperlinkPreviewEpoch);
        if (Dispatcher.UIThread.CheckAccess())
        {
            _hyperlinkPreviewAppliedEpoch = epoch;
            _hyperlinkPreview.Cancel();
        }
        else QueueHyperlinkPreviewRefresh();
    }

    private void ResumeHyperlinkPreview()
    {
        if (Interlocked.Exchange(ref _hyperlinkPreviewSuppressed, 0) != 0) QueueHyperlinkPreviewRefresh();
    }
}
