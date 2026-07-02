// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;

namespace RoyalTerminal.Avalonia.App.Styles;

internal sealed class PopupRootThemeVariantBehavior
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<PopupRootThemeVariantBehavior, PopupRoot, bool>("IsEnabled");

    private PopupRootThemeVariantBehavior()
    {
    }

    static PopupRootThemeVariantBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<PopupRoot>(OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(PopupRoot popupRoot) => popupRoot.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(PopupRoot popupRoot, bool value) => popupRoot.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(PopupRoot popupRoot, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.OldValue is true)
        {
            popupRoot.Opened -= PopupRootOnOpened;
        }

        if (change.NewValue is true)
        {
            popupRoot.Opened += PopupRootOnOpened;
            ApplyThemeVariant(popupRoot);
        }
        else
        {
            popupRoot.RequestedThemeVariant = ThemeVariant.Default;
        }
    }

    private static void PopupRootOnOpened(object? sender, System.EventArgs e)
    {
        if (sender is PopupRoot popupRoot)
        {
            ApplyThemeVariant(popupRoot);
        }
    }

    private static void ApplyThemeVariant(PopupRoot popupRoot)
    {
        Application? app = Application.Current;
        if (app is null)
        {
            popupRoot.RequestedThemeVariant = ThemeVariant.Default;
            return;
        }

        popupRoot.RequestedThemeVariant = app.RequestedThemeVariant == ThemeVariant.Default
            ? app.ActualThemeVariant
            : app.RequestedThemeVariant;
    }
}
