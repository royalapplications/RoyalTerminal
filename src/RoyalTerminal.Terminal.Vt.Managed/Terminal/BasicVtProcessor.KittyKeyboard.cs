// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Terminal.Snapshots;

namespace RoyalTerminal.Terminal;

public sealed partial class BasicVtProcessor
{
    private ManagedKittyKeyboardState _kittyKeyboardMain;
    private ManagedKittyKeyboardState _kittyKeyboardAlt;
    private ref ManagedKittyKeyboardState ActiveKittyKeyboard => ref (_inAltScreen ? ref _kittyKeyboardAlt : ref _kittyKeyboardMain);

    internal ManagedKittyKeyboardState GetSnapshotKittyKeyboard(int key) => key switch
    {
        0 => _kittyKeyboardMain,
        1 => _kittyKeyboardAlt,
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    // Used only while assembling an unpublished processor; no protocol replay,
    // mode events, buffer switches or discard of inactive ring slots.
    internal void InstallSnapshotKittyKeyboard(GhosttySnapshotScreenState screen)
    {
        ManagedKittyKeyboardState state = ManagedKittyKeyboardState.FromSnapshot(screen.KittyKeyboardIndex, screen.KittyKeyboardFlags);
        if (screen.Key == 0) _kittyKeyboardMain = state;
        else _kittyKeyboardAlt = state;
    }

    private void HandleKittyKeyboardSet()
    {
        int flags = _params.Count > 0 ? _params[0] : 0;
        int operation = _params.Count > 1 ? _params[1] : 1;
        if ((uint)flags > 31) return;
        switch (operation)
        {
            case 1: ActiveKittyKeyboard.Set(flags); break;
            case 2: ActiveKittyKeyboard.Set(KittyKeyboardFlags | flags); break;
            case 3: ActiveKittyKeyboard.Set(KittyKeyboardFlags & ~flags); break;
        }
    }

    private void HandleKittyKeyboardPush()
    {
        int flags = _params.Count == 1 ? _params[0] : 0;
        if ((uint)flags <= 31) ActiveKittyKeyboard.Push(flags);
    }

    private void HandleKittyKeyboardPop()
        => ActiveKittyKeyboard.Pop(_params.Count == 1 ? _params[0] : 1);
}
