// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.App - Main window backdrop coordination.

using System;
using System.Reactive.Disposables;
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

        ApplyActiveTransparencyHint(window);
    }

    public IDisposable Activate()
    {
        UpdateBackdropState(_window.ActualTransparencyLevel);

        CompositeDisposable disposables = [];
        disposables.Add(_window.GetObservable(TopLevel.ActualTransparencyLevelProperty)
            .Subscribe(UpdateBackdropState));
        _window.Activated += OnWindowActivated;
        _window.Deactivated += OnWindowDeactivated;
        disposables.Add(Disposable.Create(() =>
        {
            _window.Activated -= OnWindowActivated;
            _window.Deactivated -= OnWindowDeactivated;
        }));

        return disposables;
    }

    internal static bool IsMicaSupportedByCurrentPlatform =>
        OperatingSystem.IsWindows();

    internal void ApplyWindowActivationState(bool isActive)
    {
        if (!IsMicaSupportedByCurrentPlatform)
        {
            _viewModel.IsMicaBackdropEnabled = false;
            return;
        }

        if (isActive)
        {
            ApplyActiveTransparencyHint(_window);
            UpdateBackdropState(_window.ActualTransparencyLevel);
            return;
        }

        _window.TransparencyLevelHint = new WindowTransparencyLevelCollection(new[]
        {
            WindowTransparencyLevel.None,
        });
        _viewModel.IsMicaBackdropEnabled = false;
    }

    internal void UpdateBackdropState(WindowTransparencyLevel transparencyLevel)
    {
        bool micaEnabled = transparencyLevel == WindowTransparencyLevel.Mica;
        _viewModel.IsMicaBackdropEnabled = micaEnabled;
    }

    private static void ApplyActiveTransparencyHint(Window window)
    {
        window.TransparencyLevelHint = new WindowTransparencyLevelCollection(new[]
        {
            WindowTransparencyLevel.Mica,
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.Blur,
        });
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        ApplyWindowActivationState(true);
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        ApplyWindowActivationState(false);
    }
}
