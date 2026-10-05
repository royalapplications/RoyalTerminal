// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia.Controls;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.App.Services;

internal sealed class DesktopTerminalWindowResizeHost(Window window, TerminalControl control,
    Func<bool> isSingleTerminalLayout) : ITerminalWindowResizeHost
{
    public void RequestResize(TerminalWindowResizeRequest request)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!control.AllowVtWindowResize || !window.IsVisible || !window.IsEnabled || !window.CanResize ||
            window.WindowState != WindowState.Normal || window.SizeToContent != SizeToContent.Manual ||
            !control.IsVisible || !ReferenceEquals(TopLevel.GetTopLevel(control), window) ||
            !isSingleTerminalLayout() || control.Renderer is not { } renderer) return;

        // Match the control's effective configured padding, not layout-balanced
        // padding. The remainder of the window client area is host chrome.
        var padding = control.Padding;
        static double Pad(double value) => double.IsFinite(value) && value > 0 ? value : 0;
        if (!TerminalWindowResizeGeometry.TryResolve(request, renderer.CellWidth, renderer.CellHeight,
            Pad(padding.Left) + Pad(padding.Right), Pad(padding.Top) + Pad(padding.Bottom), window.RenderScaling,
            out double width, out double height)) return;
        double chromeWidth = window.ClientSize.Width - control.Bounds.Width;
        double chromeHeight = window.ClientSize.Height - control.Bounds.Height;
        if (!double.IsFinite(chromeWidth) || chromeWidth < 0 || !double.IsFinite(chromeHeight) || chromeHeight < 0) return;
        if (width > 0) window.Width = width + chromeWidth;
        if (height > 0) window.Height = height + chromeHeight;
        // Layout owns the actual grid resize and PTY notification, including any
        // platform min/max constraints. Do not resize the parser separately.
    }
}
