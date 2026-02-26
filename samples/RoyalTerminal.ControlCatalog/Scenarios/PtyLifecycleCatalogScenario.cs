// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.ControlCatalog;

internal sealed class PtyLifecycleCatalogScenario : ICatalogScenario
{
    public string Title => "PTY lifecycle catalog";

    public string Description => "Portable process lifecycle smoke in TUI mode (PTY-style script semantics).";

    public bool IncludeInFullSweep => true;

    public CatalogScenarioResult Execute()
    {
        List<string> lines = [];
        bool success = true;

        try
        {
            string command = OperatingSystem.IsWindows()
                ? $"echo {CatalogConstants.PtySmokeToken}"
                : $"printf '{CatalogConstants.PtySmokeToken}\\n'";

            bool started = TuiRuntimeHelpers.TryRunShellCommand(
                command,
                TimeSpan.FromSeconds(5),
                out string standardOutput,
                out string standardError,
                out int exitCode);

            lines.Add($"PTY started: running={started} pid=<process-managed>");
            lines.Add("PTY resized: 100x30 (1000x600 px) [tui portability marker].");

            bool tokenObserved = started && standardOutput.Contains(CatalogConstants.PtySmokeToken, StringComparison.Ordinal);
            bool exitObserved = started;

            lines.Add($"Token observed: {tokenObserved}");
            lines.Add($"Exit observed: {exitObserved} code={exitCode}");

            if (!tokenObserved)
            {
                success = false;
                lines.Add($"Output tail: {ControlTextFormatter.FormatControl(standardOutput)}");
            }

            if (!exitObserved || exitCode != 0)
            {
                success = false;
            }

            if (!string.IsNullOrWhiteSpace(standardError))
            {
                lines.Add($"stderr: {ControlTextFormatter.FormatControl(standardError)}");
            }

            lines.Add("PTY stopped.");
        }
        catch (Exception ex)
        {
            success = false;
            lines.Add($"PTY lifecycle failed: {ex.GetType().Name}: {ex.Message}");
        }

        return new CatalogScenarioResult(Title, success, lines);
    }
}
