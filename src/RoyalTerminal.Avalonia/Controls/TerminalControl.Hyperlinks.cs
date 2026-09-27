// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia;
using Avalonia.Threading;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Controls;

public partial class TerminalControl
{
    /// <summary>Enables producer-supplied OSC 8 links independently of plain-text URL detection.</summary>
    public static readonly DirectProperty<TerminalControl, bool> EnableOsc8HyperlinksProperty =
        AvaloniaProperty.RegisterDirect<TerminalControl, bool>(nameof(EnableOsc8Hyperlinks),
            control => control.EnableOsc8Hyperlinks, (control, value) => control.EnableOsc8Hyperlinks = value, unsetValue: true);

    /// <summary>Application-owned host for confirmation, file inspection and blocked-target UI.</summary>
    public static readonly DirectProperty<TerminalControl, ITerminalHyperlinkHost?> HyperlinkHostProperty =
        AvaloniaProperty.RegisterDirect<TerminalControl, ITerminalHyperlinkHost?>(nameof(HyperlinkHost),
            control => control.HyperlinkHost, (control, value) => control.HyperlinkHost = value);

    private bool _enableOsc8Hyperlinks = true;
    private ITerminalHyperlinkHost? _hyperlinkHost;
    private CancellationTokenSource? _hyperlinkRequestCancellation;
    private string? _hoveredLinkDisplayText;

    /// <summary>Whether OSC 8 annotations can be hovered or activated; defaults to true.</summary>
    public bool EnableOsc8Hyperlinks
    {
        get => _enableOsc8Hyperlinks;
        set
        {
            Dispatcher.UIThread.VerifyAccess();
            if (!SetAndRaise(EnableOsc8HyperlinksProperty, ref _enableOsc8Hyperlinks, value)) return;
            CancelPendingHyperlinkRequest();
            UpdateRendererParityStateFromScreen();
        }
    }

    /// <summary>
    /// Optional host for non-direct links, configured on the UI thread. Null fails
    /// closed for custom schemes and files. The application owns host lifetime.
    /// </summary>
    public ITerminalHyperlinkHost? HyperlinkHost
    {
        get => _hyperlinkHost;
        set
        {
            Dispatcher.UIThread.VerifyAccess();
            if (SetAndRaise(HyperlinkHostProperty, ref _hyperlinkHost, value)) CancelPendingHyperlinkRequest();
        }
    }

    /// <summary>Gets a single-line, escaped preview of the hovered target. Never use this as a launch URI.</summary>
    public string? HoveredLinkDisplayText => _hoveredLinkDisplayText;

    private bool ActivateUntrustedHyperlink(string target)
    {
        TerminalHyperlinkRequest request = TerminalHyperlinkSafety.Classify(target);
        if (request.Disposition == TerminalHyperlinkDisposition.Allow) return TryActivateHyperlink(request.Uri!);
        if (_hyperlinkHost is { } host && _hyperlinkRequestCancellation is null)
        {
            CancellationTokenSource cancellation = new();
            _hyperlinkRequestCancellation = cancellation;
            _ = HandleHyperlinkRequestAsync(host, request, cancellation);
        }
        // A denied/unconfirmed target is handled, not a request to fall back
        // to another launcher or forward the modified click to terminal input.
        return true;
    }

    private async Task HandleHyperlinkRequestAsync(ITerminalHyperlinkHost host, TerminalHyperlinkRequest request,
        CancellationTokenSource cancellation)
    {
        try { await host.HandleAsync(request, cancellation.Token); }
        catch { /* A host failure must never enable the unrestricted opener. */ }
        finally
        {
            if (ReferenceEquals(_hyperlinkRequestCancellation, cancellation)) _hyperlinkRequestCancellation = null;
            cancellation.Dispose();
        }
    }

    private void CancelPendingHyperlinkRequest()
    {
        CancellationTokenSource? cancellation = Volatile.Read(ref _hyperlinkRequestCancellation);
        try { cancellation?.Cancel(); }
        catch (AggregateException) { /* Host cancellation callbacks cannot break detachment. */ }
        catch (ObjectDisposedException) { /* The operation completed concurrently with session shutdown. */ }
        // Keep the slot until the owned operation actually finishes. A canceled
        // filesystem/native-host call may still be running; replacement must not
        // start an unbounded succession of abandoned background operations.
    }
}
