// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor : ITerminalVisibilityState
{
    private bool _potentiallyVisible = true;

    /// <inheritdoc />
    public bool PotentiallyVisible
    {
        get => _potentiallyVisible;
        set
        {
            if (_potentiallyVisible == value) return;
            _potentiallyVisible = value;
            if (_extendedDecModes.Contains(ManagedDecModeFlag.VisibilityReports)) EmitVisibilityReport();
        }
    }
}
