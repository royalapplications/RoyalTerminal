// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.ControlCatalog;

internal sealed record CatalogScenarioResult(
    string Title,
    bool Success,
    IReadOnlyList<string> Lines);
