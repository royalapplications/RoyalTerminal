// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.ControlCatalog;

internal static class PlainResultWriter
{
    public static void Write(CatalogScenarioResult result)
    {
        Console.WriteLine($"[{(result.Success ? "PASS" : "FAIL")}] {result.Title}");
        for (int i = 0; i < result.Lines.Count; i++)
        {
            Console.WriteLine(result.Lines[i]);
        }
    }
}
