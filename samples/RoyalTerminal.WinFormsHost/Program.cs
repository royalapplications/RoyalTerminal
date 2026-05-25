// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.WinFormsHost - Windows Forms embedding sample.

using Avalonia;
using Avalonia.Themes.Fluent;
using WinForms = System.Windows.Forms;

namespace RoyalTerminal.WinFormsHost;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        WinForms.Application.SetHighDpiMode(WinForms.HighDpiMode.PerMonitorV2);
        WinForms.Application.EnableVisualStyles();
        WinForms.Application.SetCompatibleTextRenderingDefault(false);

        AppBuilder.Configure<WinFormsAvaloniaApp>()
            .UsePlatformDetect()
            .SetupWithoutStarting();

        WinForms.Application.Run(new TerminalForm());
    }
}

internal sealed class WinFormsAvaloniaApp : global::Avalonia.Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }
}
