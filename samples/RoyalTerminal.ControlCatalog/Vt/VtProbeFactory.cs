// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.ControlCatalog;

internal static class VtProbeFactory
{
    public static List<VtProcessorProbe> CreateProbes(List<string> lines)
    {
        List<VtProcessorProbe> probes =
        [
            VtProcessorProbe.CreateManaged(columns: 120, rows: 40),
        ];

        if (VtProcessorProbe.TryCreateGhostty(columns: 120, rows: 40, out VtProcessorProbe? ghosttyProbe, out string? reason))
        {
            probes.Add(ghosttyProbe);
        }
        else
        {
            lines.Add($"[info] {CatalogConstants.GhosttyProbeName} unavailable: {reason}");
        }

        return probes;
    }

    public static void DisposeProbes(List<VtProcessorProbe> probes)
    {
        for (int i = 0; i < probes.Count; i++)
        {
            probes[i].Dispose();
        }
    }
}
