// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.App - Window icon theme coordination.

using System;
using System.Reactive.Disposables;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace RoyalTerminal.Avalonia.App.Services;

/// <summary>
/// Keeps the generated window icon readable against the current platform taskbar theme.
/// </summary>
internal sealed class MainWindowIconThemeCoordinator
{
    private readonly Window _window;

    public MainWindowIconThemeCoordinator(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    public IDisposable Activate()
    {
        ApplyIcon();

        IPlatformSettings? platformSettings = _window.GetPlatformSettings();
        if (platformSettings is null)
        {
            return Disposable.Empty;
        }

        platformSettings.ColorValuesChanged += OnColorValuesChanged;
        return Disposable.Create(() => platformSettings.ColorValuesChanged -= OnColorValuesChanged);
    }

    internal void ApplyIcon()
    {
        ThemeVariant themeVariant = GetCurrentPlatformThemeVariant(_window);
        _window.Icon = RoyalTerminalWindowIconHelper.CreateThemedWindowIcon(themeVariant);
    }

    internal static ThemeVariant ToIconThemeVariant(PlatformThemeVariant platformThemeVariant)
    {
        return platformThemeVariant == PlatformThemeVariant.Dark
            ? ThemeVariant.Dark
            : ThemeVariant.Light;
    }

    private void OnColorValuesChanged(object? sender, PlatformColorValues e)
    {
        ApplyIcon();
    }

    private static ThemeVariant GetCurrentPlatformThemeVariant(Window window)
    {
        IPlatformSettings? platformSettings = window.GetPlatformSettings();
        if (platformSettings is null)
        {
            return ThemeVariant.Light;
        }

        return ToIconThemeVariant(platformSettings.GetColorValues().ThemeVariant);
    }
}
