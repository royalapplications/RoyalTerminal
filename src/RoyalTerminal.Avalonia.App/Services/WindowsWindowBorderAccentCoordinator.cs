// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.App - Windows DWM border accent integration.

using System;
using System.Reactive.Disposables;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace RoyalTerminal.Avalonia.App.Services;

internal sealed class WindowsWindowBorderAccentCoordinator
{
    private const int DwmWindowAttributeBorderColor = 34;
    private const uint DwmBorderColorDefault = 0xFFFFFFFF;
    private static readonly Color s_fallbackAccentColor = Color.FromRgb(0, 120, 215);

    private readonly Window _window;

    public WindowsWindowBorderAccentCoordinator(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    public IDisposable Activate()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Disposable.Empty;
        }

        _window.Opened += OnWindowChromeStateChanged;
        _window.Activated += OnWindowChromeStateChanged;
        _window.Deactivated += OnWindowChromeStateChanged;
        _window.Closed += OnWindowClosed;

        ScheduleApplyWindowBorder();

        return Disposable.Create(() =>
        {
            _window.Opened -= OnWindowChromeStateChanged;
            _window.Activated -= OnWindowChromeStateChanged;
            _window.Deactivated -= OnWindowChromeStateChanged;
            _window.Closed -= OnWindowClosed;
            ResetWindowBorder(_window);
        });
    }

    internal static uint ToDwmColorRef(Color color) => (uint)((color.B << 16) | (color.G << 8) | color.R);

    internal static Color GetInactiveWindowBorderColor(Color activeWindowBorderColor)
    {
        if (activeWindowBorderColor.A == 0)
        {
            return activeWindowBorderColor;
        }

        byte gray = (byte)Math.Round(
            (activeWindowBorderColor.R * 0.299d) +
            (activeWindowBorderColor.G * 0.587d) +
            (activeWindowBorderColor.B * 0.114d));

        return Color.FromRgb(gray, gray, gray);
    }

    private void OnWindowChromeStateChanged(object? sender, EventArgs e) => ScheduleApplyWindowBorder();

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        ResetWindowBorder(_window);
    }

    private void ScheduleApplyWindowBorder()
    {
        Dispatcher.UIThread.Post(ApplyWindowBorder, DispatcherPriority.Background);
    }

    private void ApplyWindowBorder()
    {
        if (!IsWindowBorderSupported() ||
            _window.WindowDecorations == WindowDecorations.None ||
            !TryGetWindowHandle(_window, out IntPtr hwnd))
        {
            return;
        }

        Color activeBorderColor = GetActiveWindowBorderColor(_window);
        Color borderColor = _window.IsActive
            ? activeBorderColor
            : GetInactiveWindowBorderColor(activeBorderColor);
        SetWindowBorderColor(hwnd, borderColor);
    }

    private static Color GetActiveWindowBorderColor(Window window)
    {
        return window.GetPlatformSettings()?.GetColorValues().AccentColor1 ?? s_fallbackAccentColor;
    }

    private static void ResetWindowBorder(Window window)
    {
        if (!IsWindowBorderSupported() || !TryGetWindowHandle(window, out IntPtr hwnd))
        {
            return;
        }

        SetWindowBorderDefault(hwnd);
    }

    private static bool TryGetWindowHandle(Window window, out IntPtr hwnd)
    {
        hwnd = default;

        if (window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND" } platformHandle ||
            platformHandle.Handle == IntPtr.Zero)
        {
            return false;
        }

        hwnd = platformHandle.Handle;
        return true;
    }

    [SupportedOSPlatform("windows10.0.22000")]
    private static void SetWindowBorderColor(IntPtr hwnd, Color color)
    {
        uint borderColor = ToDwmColorRef(color);
        _ = DwmSetWindowAttribute(
            hwnd,
            DwmWindowAttributeBorderColor,
            ref borderColor,
            sizeof(uint));
    }

    [SupportedOSPlatform("windows10.0.22000")]
    private static void SetWindowBorderDefault(IntPtr hwnd)
    {
        uint borderColor = DwmBorderColorDefault;
        _ = DwmSetWindowAttribute(
            hwnd,
            DwmWindowAttributeBorderColor,
            ref borderColor,
            sizeof(uint));
    }

    [SupportedOSPlatformGuard("windows10.0.22000")]
    private static bool IsWindowBorderSupported() => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref uint pvAttribute,
        int cbAttribute);
}
