// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.WinFormsHost - WinForms to Avalonia terminal bridge.

using Avalonia;
using Avalonia.Threading;
using Avalonia.Win32.Interoperability;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;
using WinForms = System.Windows.Forms;

namespace RoyalTerminal.WinFormsHost;

internal sealed class TerminalWinFormsHost : WinFormsAvaloniaControlHost
{
    private const int WmSetFocus = 0x0007;
    private const int WmKillFocus = 0x0008;

    public TerminalWinFormsHost()
    {
        TabStop = true;
        Terminal = new TerminalControl
        {
            VtProcessorPreference = VtProcessorPreference.Managed,
            Background = global::Avalonia.Media.Brushes.Black,
        };
        Content = Terminal;
    }

    public TerminalControl Terminal { get; }

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Thickness TerminalContentPadding
    {
        get => Terminal.Padding;
        set => Terminal.Padding = value;
    }

    protected override void WndProc(ref WinForms.Message m)
    {
        base.WndProc(ref m);

        if (m.Msg == WmSetFocus)
        {
            PostAvaloniaFocus();
        }
        else if (m.Msg == WmKillFocus)
        {
            // Do not move focus while Win32 is processing WM_KILLFOCUS.
        }
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        PostAvaloniaFocus();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        PostDpiScale();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        PostDpiScale();
    }

    private void PostAvaloniaFocus()
    {
        TerminalControl terminal = Terminal;
        Dispatcher.UIThread.Post(
            () => terminal.Focus(),
            DispatcherPriority.Input);
    }

    private void PostDpiScale()
    {
        double scale = DeviceDpi > 0 ? DeviceDpi / 96d : 1d;
        TerminalControl terminal = Terminal;
        Dispatcher.UIThread.Post(
            () => terminal.SetContentScale(scale, scale),
            DispatcherPriority.Input);
    }
}
