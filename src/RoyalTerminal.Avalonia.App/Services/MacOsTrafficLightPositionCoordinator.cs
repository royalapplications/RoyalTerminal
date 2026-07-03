// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.App - macOS native titlebar button positioning.

using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace RoyalTerminal.Avalonia.App.Services;

internal sealed class MacOsTrafficLightPositionCoordinator : IDisposable
{
    internal const double TitleBarHeight = 44d;
    internal const double CloseButtonCenterX = 28d;

    private const string NativeLibrary = "/usr/lib/libobjc.A.dylib";
    private const int CloseButton = 0;
    private const int MiniaturizeButton = 1;
    private const int ZoomButton = 2;

    private readonly Window _window;
    private bool _disposed;

    public MacOsTrafficLightPositionCoordinator(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    public IDisposable Activate()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return Disposable.Empty;
        }

        _window.Opened += OnWindowLayoutChanged;
        _window.Activated += OnWindowLayoutChanged;
        _window.SizeChanged += OnWindowLayoutChanged;
        QueueApply();
        return this;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _window.Opened -= OnWindowLayoutChanged;
        _window.Activated -= OnWindowLayoutChanged;
        _window.SizeChanged -= OnWindowLayoutChanged;
    }

    private void OnWindowLayoutChanged(object? sender, EventArgs e) => QueueApply();

    private void QueueApply()
    {
        Dispatcher.UIThread.Post(Apply, DispatcherPriority.Loaded);
    }

    private void Apply()
    {
        if (_disposed || !OperatingSystem.IsMacOS())
        {
            return;
        }

        IPlatformHandle? handle = _window.TryGetPlatformHandle();
        if (handle is null || handle.Handle == nint.Zero || !IsLikelyNsWindowHandle(handle))
        {
            return;
        }

        try
        {
            Apply(handle.Handle);
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
        catch (SEHException)
        {
        }
    }

    private static void Apply(nint nsWindow)
    {
        if (!SendBool(nsWindow, Selectors.RespondsToSelector, Selectors.StandardWindowButton))
        {
            return;
        }

        nint closeButton = GetStandardWindowButton(nsWindow, CloseButton);
        nint miniaturizeButton = GetStandardWindowButton(nsWindow, MiniaturizeButton);
        nint zoomButton = GetStandardWindowButton(nsWindow, ZoomButton);
        if (closeButton == nint.Zero || miniaturizeButton == nint.Zero || zoomButton == nint.Zero)
        {
            return;
        }

        CGRect closeFrame = GetFrame(closeButton);
        CGRect miniaturizeFrame = GetFrame(miniaturizeButton);
        CGRect zoomFrame = GetFrame(zoomButton);
        if (!IsValid(closeFrame) || !IsValid(miniaturizeFrame) || !IsValid(zoomFrame))
        {
            return;
        }

        nint superview = SendIntPtr(closeButton, Selectors.Superview);
        if (superview == nint.Zero)
        {
            return;
        }

        CGRect superviewBounds = GetBounds(superview);
        if (!IsValid(superviewBounds))
        {
            return;
        }

        double targetCloseCenterY = GetTargetCenterY(superview, superviewBounds);
        double closeCenterX = GetCenterX(closeFrame);
        double miniaturizeOffset = GetCenterX(miniaturizeFrame) - closeCenterX;
        double zoomOffset = GetCenterX(zoomFrame) - closeCenterX;

        SetCenter(closeButton, CloseButtonCenterX, targetCloseCenterY, closeFrame);
        SetCenter(miniaturizeButton, CloseButtonCenterX + miniaturizeOffset, targetCloseCenterY, miniaturizeFrame);
        SetCenter(zoomButton, CloseButtonCenterX + zoomOffset, targetCloseCenterY, zoomFrame);
    }

    private static double GetTargetCenterY(nint superview, CGRect superviewBounds)
    {
        bool isFlipped = SendBool(superview, Selectors.IsFlipped);
        double titleBarCenterFromTop = TitleBarHeight / 2d;
        return isFlipped
            ? titleBarCenterFromTop
            : superviewBounds.Size.Height - titleBarCenterFromTop;
    }

    private static void SetCenter(nint button, double centerX, double centerY, CGRect frame)
    {
        CGPoint origin = new(centerX - (frame.Size.Width / 2d), centerY - (frame.Size.Height / 2d));
        SendVoidCGPoint(button, Selectors.SetFrameOrigin, origin);
    }

    private static nint GetStandardWindowButton(nint nsWindow, int button)
    {
        return SendIntPtr(nsWindow, Selectors.StandardWindowButton, (nint)button);
    }

    private static CGRect GetFrame(nint view) => SendCGRect(view, Selectors.Frame);

    private static CGRect GetBounds(nint view) => SendCGRect(view, Selectors.Bounds);

    private static bool IsValid(CGRect rect)
    {
        return rect.Size.Width > 0d &&
            rect.Size.Height > 0d &&
            !double.IsNaN(rect.Origin.X) &&
            !double.IsNaN(rect.Origin.Y) &&
            !double.IsNaN(rect.Size.Width) &&
            !double.IsNaN(rect.Size.Height);
    }

    private static double GetCenterX(CGRect frame) => frame.Origin.X + (frame.Size.Width / 2d);

    private static CGRect SendCGRect(nint receiver, nint selector)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            CGRect_objc_msgSend_stret(out CGRect rect, receiver, selector);
            return rect;
        }

        return CGRect_objc_msgSend(receiver, selector);
    }

    private static nint SendIntPtr(nint receiver, nint selector)
    {
        return IntPtr_objc_msgSend(receiver, selector);
    }

    private static nint SendIntPtr(nint receiver, nint selector, nint argument)
    {
        return IntPtr_objc_msgSend_IntPtr(receiver, selector, argument);
    }

    private static bool SendBool(nint receiver, nint selector)
    {
        return Bool_objc_msgSend(receiver, selector);
    }

    private static bool SendBool(nint receiver, nint selector, nint argument)
    {
        return Bool_objc_msgSend_IntPtr(receiver, selector, argument);
    }

    private static void SendVoidCGPoint(nint receiver, nint selector, CGPoint point)
    {
        Void_objc_msgSend_CGPoint(receiver, selector, point);
    }

    private static bool IsLikelyNsWindowHandle(IPlatformHandle handle)
    {
        return handle.HandleDescriptor is null ||
            handle.HandleDescriptor.Contains("NSWindow", StringComparison.Ordinal);
    }

    [DllImport(NativeLibrary, EntryPoint = "sel_registerName")]
    private static extern nint sel_registerName(string selectorName);

    [DllImport(NativeLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint IntPtr_objc_msgSend(nint receiver, nint selector);

    [DllImport(NativeLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint IntPtr_objc_msgSend_IntPtr(nint receiver, nint selector, nint argument);

    [DllImport(NativeLibrary, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Bool_objc_msgSend(nint receiver, nint selector);

    [DllImport(NativeLibrary, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Bool_objc_msgSend_IntPtr(nint receiver, nint selector, nint argument);

    [DllImport(NativeLibrary, EntryPoint = "objc_msgSend")]
    private static extern CGRect CGRect_objc_msgSend(nint receiver, nint selector);

    [DllImport(NativeLibrary, EntryPoint = "objc_msgSend_stret")]
    private static extern void CGRect_objc_msgSend_stret(out CGRect rect, nint receiver, nint selector);

    [DllImport(NativeLibrary, EntryPoint = "objc_msgSend")]
    private static extern void Void_objc_msgSend_CGPoint(nint receiver, nint selector, CGPoint point);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGPoint
    {
        public CGPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public readonly double X;
        public readonly double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGSize
    {
        public readonly double Width;
        public readonly double Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGRect
    {
        public readonly CGPoint Origin;
        public readonly CGSize Size;
    }

    private static class Selectors
    {
        public static readonly nint Bounds = sel_registerName("bounds");
        public static readonly nint Frame = sel_registerName("frame");
        public static readonly nint IsFlipped = sel_registerName("isFlipped");
        public static readonly nint RespondsToSelector = sel_registerName("respondsToSelector:");
        public static readonly nint SetFrameOrigin = sel_registerName("setFrameOrigin:");
        public static readonly nint StandardWindowButton = sel_registerName("standardWindowButton:");
        public static readonly nint Superview = sel_registerName("superview");
    }

    private sealed class Disposable : IDisposable
    {
        public static readonly Disposable Empty = new();

        public void Dispose()
        {
        }
    }
}
