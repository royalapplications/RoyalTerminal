// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.ControlCatalog;

internal interface ICatalogScenario
{
    string Title { get; }

    string Description { get; }

    bool IncludeInFullSweep { get; }

    CatalogScenarioResult Execute();
}
