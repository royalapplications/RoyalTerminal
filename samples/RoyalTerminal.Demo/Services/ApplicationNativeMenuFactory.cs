// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Demo - Application-level native menu composition.

using System;
using System.Globalization;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using RoyalTerminal.Demo.ViewModels;

namespace RoyalTerminal.Demo.Services;

internal static class ApplicationNativeMenuFactory
{
    public static NativeMenu Create(MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        NativeMenu menu = CreateShell();
        Bind(menu, viewModel);
        return menu;
    }

    public static NativeMenu CreateShell()
    {
        return new NativeMenu
        {
            CreateItem("_About RoyalTerminal"),
            new NativeMenuItemSeparator(),
            CreateItem("_Preferences...", gesture: new KeyGesture(Key.OemComma, KeyModifiers.Meta)),
            new NativeMenuItemSeparator(),
            CreateItem("_Quit RoyalTerminal", gesture: new KeyGesture(Key.Q, KeyModifiers.Meta)),
        };
    }

    public static void Bind(NativeMenu menu, MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(viewModel);

        BindItem(menu, "_About RoyalTerminal", viewModel.ShowAboutCommand);
        BindItem(menu, "_Preferences...", viewModel.PrepareSettingsPanelCommand, new KeyGesture(Key.OemComma, KeyModifiers.Meta));
        BindItem(menu, "_Quit RoyalTerminal", viewModel.QuitApplicationCommand, new KeyGesture(Key.Q, KeyModifiers.Meta));
    }

    private static NativeMenuItem CreateItem(string header, ICommand? command = null, KeyGesture? gesture = null)
    {
        return new NativeMenuItem(header)
        {
            Command = command,
            Gesture = gesture,
        };
    }

    private static void BindItem(NativeMenu menu, string header, ICommand command, KeyGesture? gesture = null)
    {
        NativeMenuItem item = FindItem(menu, header);
        item.Command = command;
        item.Gesture = gesture;
    }

    private static NativeMenuItem FindItem(NativeMenu menu, string header)
    {
        foreach (NativeMenuItemBase itemBase in menu.Items)
        {
            if (itemBase is not NativeMenuItem item)
            {
                continue;
            }

            if (string.Equals(Convert.ToString(item.Header, CultureInfo.InvariantCulture), header, StringComparison.Ordinal))
            {
                return item;
            }

            if (item.Menu is { } submenu)
            {
                try
                {
                    return FindItem(submenu, header);
                }
                catch (InvalidOperationException)
                {
                    // Continue searching sibling menus.
                }
            }
        }

        throw new InvalidOperationException($"Application native menu item '{header}' was not found.");
    }
}
