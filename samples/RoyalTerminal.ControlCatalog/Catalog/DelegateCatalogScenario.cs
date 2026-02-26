// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.ControlCatalog;

internal sealed class DelegateCatalogScenario : ICatalogScenario
{
    private readonly Func<CatalogScenarioResult> _run;

    public DelegateCatalogScenario(
        string title,
        string description,
        bool includeInFullSweep,
        Func<CatalogScenarioResult> run)
    {
        Title = title;
        Description = description;
        IncludeInFullSweep = includeInFullSweep;
        _run = run;
    }

    public string Title { get; }

    public string Description { get; }

    public bool IncludeInFullSweep { get; }

    public CatalogScenarioResult Execute()
    {
        return _run();
    }
}
