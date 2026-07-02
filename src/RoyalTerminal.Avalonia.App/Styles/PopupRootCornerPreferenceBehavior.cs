// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Platform;

namespace RoyalTerminal.Avalonia.App.Styles;

internal sealed class PopupRootCornerPreferenceBehavior
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const uint DwmBorderColorDefault = 0xFFFFFFFF;
    private const uint DwmBorderColorNone = 0xFFFFFFFE;

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<PopupRootCornerPreferenceBehavior, PopupRoot, bool>("IsEnabled");

    private PopupRootCornerPreferenceBehavior()
    {
    }

    static PopupRootCornerPreferenceBehavior()
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
            ApplyPopupChrome(popupRoot, PopupWindowCornerPreference.Round, DwmBorderColorNone);
        }
        else
        {
            ApplyPopupChrome(popupRoot, PopupWindowCornerPreference.Default, DwmBorderColorDefault);
        }
    }

    private static void PopupRootOnOpened(object? sender, EventArgs e)
    {
        if (sender is PopupRoot popupRoot)
        {
            ApplyPopupChrome(popupRoot, PopupWindowCornerPreference.Round, DwmBorderColorNone);
        }
    }

    private static void ApplyPopupChrome(PopupRoot popupRoot, PopupWindowCornerPreference cornerPreference, uint borderColor)
    {
        if (!IsDwmPopupChromeSupported() ||
            popupRoot.TryGetPlatformHandle() is not { HandleDescriptor: "HWND" } platformHandle ||
            platformHandle.Handle == IntPtr.Zero)
        {
            return;
        }

        ApplyPopupChrome(platformHandle, cornerPreference, borderColor);
    }

    [SupportedOSPlatform("windows10.0.22000")]
    private static void ApplyPopupChrome(IPlatformHandle platformHandle, PopupWindowCornerPreference cornerPreference, uint borderColor)
    {
        int preference = cornerPreference switch
        {
            PopupWindowCornerPreference.DoNotRound => 1,
            PopupWindowCornerPreference.Round => 2,
            PopupWindowCornerPreference.RoundSmall => 3,
            _ => 0,
        };

        _ = DwmSetWindowAttribute(platformHandle.Handle, DwmwaWindowCornerPreference, ref preference, sizeof(int));
        _ = DwmSetWindowAttribute(platformHandle.Handle, DwmwaBorderColor, ref borderColor, sizeof(uint));
    }

    [SupportedOSPlatformGuard("windows10.0.22000")]
    private static bool IsDwmPopupChromeSupported() => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref uint pvAttribute, int cbAttribute);

    private enum PopupWindowCornerPreference
    {
        Default = 0,
        DoNotRound = 1,
        Round = 2,
        RoundSmall = 3,
    }
}
