// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Controls;

public partial class TerminalControl
{
    private readonly TerminalVisibilityObserver _hostVisibility;

    private void OnHostVisibilityChanged(bool potentiallyVisible)
    {
        ApplyHostVisibility(_vtProcessor, potentiallyVisible);
        (Endpoint as ITerminalVisibilitySink)?.SetVisibility(potentiallyVisible);
    }

    private void ApplyHostVisibility(IVtProcessor? processor, bool potentiallyVisible)
    {
        if (_screen is not null && processor is ITerminalVisibilityState state)
        {
            // The gather/parse worker takes the same synchronization lease. Keep
            // state changes, parser replies and transition replies in one order.
            using (_screen.Synchronization.AcquireDemand())
                state.PotentiallyVisible = potentiallyVisible;
        }
    }
}
