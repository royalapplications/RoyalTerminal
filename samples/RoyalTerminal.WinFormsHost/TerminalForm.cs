// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.WinFormsHost - Main sample form.

using Avalonia;
using WinForms = System.Windows.Forms;

namespace RoyalTerminal.WinFormsHost;

internal sealed class TerminalForm : WinForms.Form
{
    private readonly TerminalWinFormsHost _terminalHost;

    public TerminalForm()
    {
        Text = "RoyalTerminal WinForms Host";
        StartPosition = WinForms.FormStartPosition.CenterScreen;
        AutoScaleMode = WinForms.AutoScaleMode.Dpi;
        ClientSize = new System.Drawing.Size(1000, 700);

        _terminalHost = new TerminalWinFormsHost
        {
            Dock = WinForms.DockStyle.Fill,
            TerminalContentPadding = new Thickness(8),
        };

        Controls.Add(_terminalHost);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _terminalHost.Terminal.StartPty();
    }

    protected override void OnFormClosed(WinForms.FormClosedEventArgs e)
    {
        _terminalHost.Terminal.StopPty();
        base.OnFormClosed(e);
    }
}
