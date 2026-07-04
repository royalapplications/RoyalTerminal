// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.App - Reusable terminal shell options.

namespace RoyalTerminal.Avalonia.App;

using RoyalTerminal.Avalonia.App.Services;

/// <summary>
/// Configures host-specific presentation options for the reusable main window shell.
/// </summary>
public sealed class MainWindowShellOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether macOS title-bar RoyalTerminal logos are shown.
    /// </summary>
    public bool ShowMacOsTitleBarLogos { get; set; } = true;

    /// <summary>
    /// Gets or sets the app-level preference store used by the shell.
    /// </summary>
    public IAppPreferencesStore? AppPreferencesStore { get; set; }
}
