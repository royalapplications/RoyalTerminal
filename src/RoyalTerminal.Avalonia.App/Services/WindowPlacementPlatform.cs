// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.App - Native window placement helpers.

using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace RoyalTerminal.Avalonia.App.Services;

internal static class WindowPlacementPlatform
{
    private static readonly IntPtr s_hwndTop = IntPtr.Zero;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    public static PixelPoint GetWindowPosition(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        return TryGetWindowsWindowRect(window, out NativeRect rect)
            ? new PixelPoint(rect.Left, rect.Top)
            : window.Position;
    }

    public static void SetWindowPosition(Window window, PixelPoint position)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (TrySetWindowsWindowPosition(window, position))
        {
            return;
        }

        window.Position = position;
    }

    internal static AppWindowPlacement CreatePlacement(
        PixelPoint position,
        double width,
        double height,
        double minWidth,
        double minHeight,
        AppWindowState state)
    {
        return new AppWindowPlacement(
            X: position.X,
            Y: position.Y,
            Width: Math.Max(minWidth, width),
            Height: Math.Max(minHeight, height),
            State: state);
    }

    private static bool TryGetWindowsWindowRect(Window window, out NativeRect rect)
    {
        rect = default;
        return OperatingSystem.IsWindows() &&
            TryGetWindowsWindowHandle(window, out IntPtr hwnd) &&
            GetWindowRect(hwnd, out rect);
    }

    private static bool TrySetWindowsWindowPosition(Window window, PixelPoint position)
    {
        return OperatingSystem.IsWindows() &&
            TryGetWindowsWindowHandle(window, out IntPtr hwnd) &&
            SetWindowPos(
                hwnd,
                s_hwndTop,
                position.X,
                position.Y,
                cx: 0,
                cy: 0,
                SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    private static bool TryGetWindowsWindowHandle(Window window, out IntPtr hwnd)
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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
