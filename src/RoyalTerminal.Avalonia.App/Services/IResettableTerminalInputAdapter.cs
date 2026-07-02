// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.App - Reset hook for app-owned terminal input decorators.

namespace RoyalTerminal.Avalonia.App.Services;

internal interface IResettableTerminalInputAdapter
{
    void ResetInputState();
}
