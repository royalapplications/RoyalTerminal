// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.ControlCatalog;

internal readonly record struct QueryCheck(
    string Name,
    string Sequence,
    Func<string, bool> Validator);
