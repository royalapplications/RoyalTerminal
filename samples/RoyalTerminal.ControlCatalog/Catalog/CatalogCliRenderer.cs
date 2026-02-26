// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;

namespace RoyalTerminal.ControlCatalog;

internal sealed class CatalogCliRenderer : IDisposable
{
    private readonly bool _ansi;
    private bool _disposed;

    public CatalogCliRenderer()
    {
        _ansi = !Console.IsOutputRedirected;
        if (_ansi)
        {
            Console.Write("\x1b[?1049h\x1b[?25l\x1b[2J\x1b[H");
        }
    }

    public void RenderMenu(
        string title,
        string subtitle,
        IReadOnlyList<ICatalogScenario> scenarios,
        int selectedIndex,
        string statusLine,
        string exitLabel = "Exit",
        string footerHint = "Enter: run selection  |  R: full sweep  |  Q/Esc: quit")
    {
        StringBuilder frame = new();
        if (_ansi)
        {
            frame.Append("\x1b[H\x1b[2J");
        }

        frame.Append("\x1b[1;36m").Append(title).Append("\x1b[0m").AppendLine();
        frame.Append(subtitle).AppendLine().AppendLine();
        frame.Append("Initial feature selection:").AppendLine();

        for (int i = 0; i < scenarios.Count; i++)
        {
            ICatalogScenario scenario = scenarios[i];
            bool selected = selectedIndex == i;
            if (selected)
            {
                frame.Append("\x1b[7m");
            }

            frame.Append(i + 1).Append(". ").Append(scenario.Title);
            if (selected)
            {
                frame.Append("\x1b[0m");
            }

            frame.AppendLine();
            frame.Append("    ").Append(scenario.Description).AppendLine();
        }

        bool exitSelected = selectedIndex == scenarios.Count;
        if (exitSelected)
        {
            frame.Append("\x1b[7m");
        }

        frame.Append("0. ").Append(exitLabel);
        if (exitSelected)
        {
            frame.Append("\x1b[0m");
        }

        frame.AppendLine();
        frame.AppendLine();
        frame.Append(footerHint).AppendLine();
        frame.Append(statusLine);

        Console.Write(frame.ToString());
    }

    public void RenderResult(CatalogScenarioResult result)
    {
        StringBuilder frame = new();
        if (_ansi)
        {
            frame.Append("\x1b[H\x1b[2J");
        }

        string state = result.Success
            ? "\x1b[32mPASS\x1b[0m"
            : "\x1b[31mFAIL\x1b[0m";
        frame.Append("\x1b[1m").Append(result.Title).Append("\x1b[0m").Append(" - ").Append(state).AppendLine();
        frame.AppendLine(new string('-', 80));

        for (int i = 0; i < result.Lines.Count; i++)
        {
            frame.AppendLine(result.Lines[i]);
        }

        frame.AppendLine();
        frame.Append("Press any key to return to menu.");
        Console.Write(frame.ToString());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ansi)
        {
            Console.Write("\x1b[0m\x1b[?25h\x1b[?1049l");
        }
    }
}
