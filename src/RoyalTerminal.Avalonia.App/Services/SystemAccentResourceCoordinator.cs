// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.App - Platform accent resource coordination.

using System;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace RoyalTerminal.Avalonia.App.Services;

/// <summary>
/// Keeps app resources that should follow the platform accent color up to date.
/// </summary>
internal sealed class SystemAccentResourceCoordinator
{
    internal const string TerminalPaneActiveBorderBrushKey = "TerminalPaneActiveBorderBrush";
    internal const double TerminalPaneActiveBorderBrushOpacity = 0.5d;

    private static readonly Color s_fallbackAccentColor = Color.FromRgb(0, 120, 215);

    private readonly Window _window;
    private Color _accentColor = s_fallbackAccentColor;

    public SystemAccentResourceCoordinator(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    public IDisposable Activate()
    {
        CompositeDisposable disposables = new();
        IPlatformSettings? platformSettings = _window.GetPlatformSettings();
        _accentColor = platformSettings?.GetColorValues().AccentColor1 ?? s_fallbackAccentColor;
        ApplyAccentResources();

        if (platformSettings is not null)
        {
            platformSettings.ColorValuesChanged += OnColorValuesChanged;
            disposables.Add(Disposable.Create(() => platformSettings.ColorValuesChanged -= OnColorValuesChanged));
        }

        if (Application.Current is { } app)
        {
            disposables.Add(app.GetObservable(Application.RequestedThemeVariantProperty).Subscribe(_ => ApplyAccentResources()));
            disposables.Add(app.GetObservable(Application.ActualThemeVariantProperty).Subscribe(_ => ApplyAccentResources()));
        }

        return disposables;
    }

    internal void ApplyAccentResources(Color accentColor)
    {
        _accentColor = accentColor;
        ApplyAccentResources();
    }

    internal void ApplyAccentResources(Color accentColor, ThemeVariant? appThemeVariant)
    {
        _window.Resources[TerminalPaneActiveBorderBrushKey] = new SolidColorBrush(
            GetPaneIndicatorColor(accentColor, appThemeVariant),
            TerminalPaneActiveBorderBrushOpacity);
    }

    internal static Color GetPaneIndicatorColor(Color accentColor, ThemeVariant? appThemeVariant)
    {
        if (appThemeVariant == ThemeVariant.Dark)
        {
            return BlendColor(accentColor, Colors.White, 0.28d);
        }

        return BlendColor(accentColor, Colors.Black, 0.24d);
    }

    private void OnColorValuesChanged(object? sender, PlatformColorValues e)
    {
        _accentColor = e.AccentColor1;
        ApplyAccentResources();
    }

    private void ApplyAccentResources()
    {
        ApplyAccentResources(_accentColor, GetCurrentAppThemeVariant());
    }

    private static ThemeVariant? GetCurrentAppThemeVariant()
    {
        if (Application.Current is not { } app)
        {
            return null;
        }

        return app.RequestedThemeVariant == ThemeVariant.Default
            ? app.ActualThemeVariant
            : app.RequestedThemeVariant;
    }

    private static Color BlendColor(Color from, Color to, double amount)
    {
        double t = Math.Clamp(amount, 0.0d, 1.0d);
        byte a = BlendByte(from.A, to.A, t);
        byte r = BlendByte(from.R, to.R, t);
        byte g = BlendByte(from.G, to.G, t);
        byte b = BlendByte(from.B, to.B, t);
        return Color.FromArgb(a, r, g, b);
    }

    private static byte BlendByte(byte from, byte to, double amount)
    {
        return (byte)Math.Clamp(
            (int)Math.Round(from + ((to - from) * amount), MidpointRounding.AwayFromZero),
            byte.MinValue,
            byte.MaxValue);
    }
}
