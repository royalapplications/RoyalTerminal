// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.ControlCatalog;

internal static class Program
{
    private static int Main()
    {
        List<ICatalogScenario> scenarios = CatalogScenarioFactory.Create();
        using CatalogCliRenderer renderer = new();
        CatalogApp app = new(renderer, scenarios);
        return app.Run();
    }
}
