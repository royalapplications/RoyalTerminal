// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia.Threading;
using RoyalTerminal.Avalonia.Scrolling;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Controls;

public partial class TerminalControl
{
    /// <summary>Adopts an independently decoded managed screen and its matching parser, before attaching a transport.
    /// The caller must stop the previous transport, arrange the target dimensions, and give this control exclusive
    /// ownership of the restored processor. Do not dispose the snapshot after a successful transfer.</summary>
    public void AdoptManagedReplica(ManagedTerminalSnapshot snapshot)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(snapshot);
        if (TerminalSessionService.HasActiveTransport || Endpoint is not null)
            throw new InvalidOperationException("Detach before replacing a terminal replica.");
        if (_renderer is null || _scrollData is null || _screen is null ||
            snapshot.Screen.Columns != Columns || snapshot.Screen.ViewportRows != Rows)
            throw new InvalidOperationException("Arrange the target terminal grid before installing its replica.");
        if (ReferenceEquals(snapshot.Processor, _vtProcessor)) return;
        FlushPendingTransportOutput();
        var scroll = new VirtualizedTerminalScrollViewer(snapshot.Screen, _scrollData);
        ClearSelection(); ResetSelectionViewportSpanCache(); ResetKeyboardInputState();
        CancelPendingHyperlinkRequest(); PauseHyperlinkPreview();
        _timedRefreshTimer?.Stop();
        var previous = _vtProcessor;
        DetachShellIntegrationEventSource(previous);
        _screen = snapshot.Screen;
        _vtProcessor = snapshot.Processor;
        _vtProcessorHasProcessedOutput = true;
        _appliedVtProcessorPreference = VtProcessorPreference;
        _scrollViewer = scroll;
        ApplyHostVisibility(_vtProcessor, _hostVisibility.IsVisible);
        ApplyGlyphCoverageSource(_vtProcessor);
        AttachShellIntegrationEventSource(_vtProcessor);
        BindProgramStatusSource(_vtProcessor);
        // Do not install local effect, clipboard, or query-reply handlers from snapshot data.
        // Transport policy binds permitted callbacks before starting the ordered suffix.
        lock (_screen.SyncRoot) SyncHistoryMutationScrollStateLocked();
        _presenter?.SetRenderState(_renderer, _screen);
        UpdateAutoScrollPinnedToBottom(); UpdateRendererCursorForViewport();
        UpdateRendererParityStateFromScreen(invalidateViewportRows: true);
        _presenter?.Invalidate(fullRedraw: true); RaiseScrollInvalidated(); InvalidateVisual();
        previous?.Dispose();
    }
}
