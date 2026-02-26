// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.ControlCatalog;

internal static class CatalogSweepRunner
{
    public static CatalogScenarioResult Run(IReadOnlyList<ICatalogScenario> scenarios)
    {
        List<string> lines = ["Executing full catalog sweep.", string.Empty];
        bool success = true;

        for (int i = 0; i < scenarios.Count; i++)
        {
            ICatalogScenario scenario = scenarios[i];
            if (!scenario.IncludeInFullSweep)
            {
                continue;
            }

            CatalogScenarioResult result = scenario.Execute();
            lines.Add($"> {scenario.Title}: {(result.Success ? "PASS" : "FAIL")}");

            int shown = 0;
            for (int lineIndex = 0; lineIndex < result.Lines.Count; lineIndex++)
            {
                if (shown >= 8)
                {
                    break;
                }

                lines.Add($"    {result.Lines[lineIndex]}");
                shown++;
            }

            if (result.Lines.Count > shown)
            {
                lines.Add($"    ... ({result.Lines.Count - shown} more lines)");
            }

            lines.Add(string.Empty);
            success &= result.Success;
        }

        lines.Add($"Full sweep summary: {(success ? "PASS" : "FAIL")}");
        return new CatalogScenarioResult("Full catalog sweep", success, lines);
    }
}
