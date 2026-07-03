// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.App - Windows snap layout integration for custom caption buttons.

using System.Reactive.Disposables;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using RoyalTerminal.Avalonia.App.Views;

namespace RoyalTerminal.Avalonia.App.Services;

internal sealed class WindowsCaptionButtonSnapLayoutCoordinator
{
    internal const uint WindowMessageNonClientHitTest = 0x0084;
    internal const uint WindowMessageGetTitleBarInfoEx = 0x033F;
    internal const int HitTestMaxButton = 9;
    private const uint WindowMessageMouseLeave = 0x02A3;
    private const uint WindowMessageNonClientMouseLeave = 0x02A2;
    private const int TitleBarStateOffset = 20;
    private const int TitleBarRectOffset = 44;
    private const int TitleBarMaximizeButtonIndex = 3;
    private const int TitleBarInfoExSize = 140;
    private const string SnapLayoutHoverClass = "snapLayoutHover";

    private readonly Window _window;
    private readonly Button _captionMaximizeButton;
    private readonly Button _captionRestoreButton;
    private readonly Win32Properties.CustomWndProcHookCallback _hookCallback;

    public WindowsCaptionButtonSnapLayoutCoordinator(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));

        var controlRoot = (Control?)_window.FindControl<MainView>("MainView") ?? _window;
        _captionMaximizeButton = controlRoot.FindControl<Button>("CaptionMaximizeButton")
            ?? throw new InvalidOperationException("CaptionMaximizeButton was not found in MainWindow.");
        _captionRestoreButton = controlRoot.FindControl<Button>("CaptionRestoreButton")
            ?? throw new InvalidOperationException("CaptionRestoreButton was not found in MainWindow.");
        _hookCallback = OnWndProc;
    }

    public IDisposable Activate()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Disposable.Empty;
        }

        _window.Deactivated += OnWindowDeactivated;
        Win32Properties.AddWndProcHookCallback(_window, _hookCallback);
        return Disposable.Create(() =>
        {
            _window.Deactivated -= OnWindowDeactivated;
            Win32Properties.RemoveWndProcHookCallback(_window, _hookCallback);
            ClearSnapLayoutHover();
        });
    }

    internal static Point ExtractScreenPixelPoint(IntPtr lParam)
    {
        int value = unchecked((int)lParam.ToInt64());
        int x = (short)(value & 0xFFFF);
        int y = (short)((value >> 16) & 0xFFFF);
        return new Point(x, y);
    }

    internal static Point ConvertScreenPixelPointToWindowPoint(
        Point screenPixelPoint,
        PixelPoint windowPosition,
        double renderScaling)
    {
        double scale = renderScaling > 0d ? renderScaling : 1d;
        return new Point(
            (screenPixelPoint.X - windowPosition.X) / scale,
            (screenPixelPoint.Y - windowPosition.Y) / scale);
    }

    internal static PixelRect ConvertWindowRectToScreenPixelRect(
        Rect windowRect,
        PixelPoint windowPosition,
        double renderScaling)
    {
        double scale = renderScaling > 0d ? renderScaling : 1d;
        int x = windowPosition.X + (int)Math.Round(windowRect.X * scale);
        int y = windowPosition.Y + (int)Math.Round(windowRect.Y * scale);
        int width = Math.Max(0, (int)Math.Round(windowRect.Width * scale));
        int height = Math.Max(0, (int)Math.Round(windowRect.Height * scale));
        return new PixelRect(x, y, width, height);
    }

    internal static bool TryResolveSnapLayoutHitTest(
        WindowState windowState,
        bool canMaximize,
        bool maximizeButtonVisible,
        Rect maximizeButtonBounds,
        bool restoreButtonVisible,
        Rect restoreButtonBounds,
        Point windowPoint,
        out IntPtr result)
    {
        result = IntPtr.Zero;

        if (!canMaximize || windowState == WindowState.FullScreen)
        {
            return false;
        }

        Rect targetBounds;
        bool targetVisible;
        if (windowState == WindowState.Maximized)
        {
            targetVisible = restoreButtonVisible;
            targetBounds = restoreButtonBounds;
        }
        else
        {
            targetVisible = maximizeButtonVisible;
            targetBounds = maximizeButtonBounds;
        }

        if (!targetVisible ||
            targetBounds.Width <= 0d ||
            targetBounds.Height <= 0d ||
            !targetBounds.Contains(windowPoint))
        {
            return false;
        }

        result = new IntPtr(HitTestMaxButton);
        return true;
    }

    private IntPtr OnWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        _ = wParam;

        if (msg is WindowMessageMouseLeave or WindowMessageNonClientMouseLeave)
        {
            ClearSnapLayoutHover();
            return IntPtr.Zero;
        }

        if (hWnd == IntPtr.Zero ||
            !_window.CanMaximize ||
            _window.WindowState == WindowState.FullScreen)
        {
            ClearSnapLayoutHover();
            return IntPtr.Zero;
        }

        if (msg == WindowMessageGetTitleBarInfoEx)
        {
            return HandleGetTitleBarInfoEx(hWnd, msg, wParam, lParam, ref handled);
        }

        if (msg != WindowMessageNonClientHitTest)
        {
            return IntPtr.Zero;
        }

        var screenPixelPoint = ExtractScreenPixelPoint(lParam);
        var windowPoint = ConvertScreenPixelPointToWindowPoint(
            screenPixelPoint,
            _window.Position,
            _window.RenderScaling);

        bool handledHit = TryResolveSnapLayoutHitTest(
            _window.WindowState,
            _window.CanMaximize,
            IsButtonAvailable(_captionMaximizeButton),
            GetButtonBoundsInWindow(_captionMaximizeButton),
            IsButtonAvailable(_captionRestoreButton),
            GetButtonBoundsInWindow(_captionRestoreButton),
            windowPoint,
            out IntPtr result);

        if (!handledHit)
        {
            ClearSnapLayoutHover();
            return IntPtr.Zero;
        }

        SetSnapLayoutHover(GetActiveCaptionButton());
        handled = true;
        return result;
    }

    private IntPtr HandleGetTitleBarInfoEx(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (lParam == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr result = DefWindowProcW(hWnd, msg, wParam, lParam);
        if (Marshal.ReadInt32(lParam) < TitleBarInfoExSize)
        {
            return result;
        }

        var buttonBounds = GetButtonBoundsInWindow(GetActiveCaptionButton());
        if (buttonBounds.Width <= 0d || buttonBounds.Height <= 0d)
        {
            return result;
        }

        var screenRect = ConvertWindowRectToScreenPixelRect(
            buttonBounds,
            _window.Position,
            _window.RenderScaling);
        WriteTitleBarInfoExRect(lParam, TitleBarMaximizeButtonIndex, screenRect);
        WriteTitleBarInfoExState(lParam, TitleBarMaximizeButtonIndex, 0);

        handled = true;
        return result;
    }

    private Rect GetButtonBoundsInWindow(Button button)
    {
        if (!IsButtonAvailable(button) ||
            button.TranslatePoint(new Point(0, 0), _window) is not { } origin)
        {
            return default;
        }

        return new Rect(origin, button.Bounds.Size);
    }

    private Button GetActiveCaptionButton()
    {
        return _window.WindowState == WindowState.Maximized
            ? _captionRestoreButton
            : _captionMaximizeButton;
    }

    private void SetSnapLayoutHover(Button button)
    {
        var inactiveButton = ReferenceEquals(button, _captionMaximizeButton)
            ? _captionRestoreButton
            : _captionMaximizeButton;
        inactiveButton.Classes.Remove(SnapLayoutHoverClass);
        if (!button.Classes.Contains(SnapLayoutHoverClass))
        {
            button.Classes.Add(SnapLayoutHoverClass);
        }
    }

    private void ClearSnapLayoutHover()
    {
        _captionMaximizeButton.Classes.Remove(SnapLayoutHoverClass);
        _captionRestoreButton.Classes.Remove(SnapLayoutHoverClass);
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        ClearSnapLayoutHover();
    }

    private static bool IsButtonAvailable(Button button)
    {
        if (!button.IsVisible) return false;
        return button is { IsEffectivelyVisible: true, Bounds: { Width: > 0d, Height: > 0d } };
    }

    private static void WriteTitleBarInfoExRect(IntPtr titleBarInfo, int index, PixelRect rect)
    {
        int offset = TitleBarRectOffset + (index * 16);
        Marshal.WriteInt32(titleBarInfo, offset, rect.X);
        Marshal.WriteInt32(titleBarInfo, offset + 4, rect.Y);
        Marshal.WriteInt32(titleBarInfo, offset + 8, rect.X + rect.Width);
        Marshal.WriteInt32(titleBarInfo, offset + 12, rect.Y + rect.Height);
    }

    private static void WriteTitleBarInfoExState(IntPtr titleBarInfo, int index, int state)
    {
        Marshal.WriteInt32(titleBarInfo, TitleBarStateOffset + (index * 4), state);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
