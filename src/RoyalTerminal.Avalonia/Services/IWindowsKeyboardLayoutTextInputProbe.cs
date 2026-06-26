// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia - Windows keyboard layout text-input probing.

using Avalonia.Input;

namespace RoyalTerminal.Avalonia.Services;

internal interface IWindowsKeyboardLayoutTextInputProbe
{
    bool MayProduceText(KeyEventArgs e);
}
