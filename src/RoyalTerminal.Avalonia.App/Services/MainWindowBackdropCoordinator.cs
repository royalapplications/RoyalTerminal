// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.App - Main window backdrop coordination.

using System;
using Avalonia;
using Avalonia.Controls;
using RoyalTerminal.Avalonia.App.ViewModels;

namespace RoyalTerminal.Avalonia.App.Services;

/// <summary>
/// Coordinates platform backdrop state between the main window and shell view model.
/// </summary>
internal sealed class MainWindowBackdropCoordinator
{
    private readonly Window _window;
    private readonly MainWindowViewModel _viewModel;

    public MainWindowBackdropCoordinator(Window window, MainWindowViewModel viewModel)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    public static void ConfigureTransparencyHint(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (!IsMicaSupportedByCurrentPlatform)
        {
            return;
        }

        window.TransparencyLevelHint = new WindowTransparencyLevelCollection(new[]
        {
            WindowTransparencyLevel.Mica,
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.Blur,
        });
    }

    public IDisposable Activate()
    {
        UpdateBackdropState(_window.ActualTransparencyLevel);

        return _window.GetObservable(TopLevel.ActualTransparencyLevelProperty)
            .Subscribe(UpdateBackdropState);
    }

    internal static bool IsMicaSupportedByCurrentPlatform =>
        OperatingSystem.IsWindows();

    internal void UpdateBackdropState(WindowTransparencyLevel transparencyLevel)
    {
        bool micaEnabled = transparencyLevel == WindowTransparencyLevel.Mica;
        _viewModel.IsMicaBackdropEnabled = micaEnabled;
    }
}
