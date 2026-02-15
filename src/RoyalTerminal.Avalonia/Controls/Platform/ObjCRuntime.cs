// Licensed under the MIT License.
// RoyalTerminal.Avalonia.Controls - ObjC runtime helpers for macOS native view/window hosting.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RoyalTerminal.Avalonia.Controls;

/// <summary>
/// ObjC runtime interop for creating NSView instances on macOS.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class ObjCRuntime
{
    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern nint objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.dylib")]
    private static extern nint sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_nint(nint receiver, nint selector);

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect
    {
        public double X, Y, Width, Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGSize
    {
        public double Width, Height;
    }

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_CGRect(nint receiver, nint selector, CGRect rect);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void_CGRect(nint receiver, nint selector, CGRect rect);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_void_CGSize(nint receiver, nint selector, CGSize size);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_bool(nint receiver, nint selector, byte value);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_nint_arg(nint receiver, nint selector, nint arg);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend_CGRect_uint_nint_byte(
        nint receiver, nint selector, CGRect rect, uint styleMask, nint backing, byte defer_);

    /// <summary>Creates a plain NSView for Ghostty Metal rendering.</summary>
    /// <remarks>
    /// We intentionally do NOT call setWantsLayer: here. Ghostty's Metal renderer
    /// creates a layer-hosting view by setting the view's layer property to its
    /// IOSurfaceLayer BEFORE setting wantsLayer = true. Pre-setting wantsLayer
    /// would make it a layer-backed view (AppKit-managed), which causes incorrect
    /// color space handling and undefined behavior when Ghostty replaces the layer.
    /// </remarks>
    public static nint CreateNSView()
    {
        var cls = objc_getClass("NSView");
        var allocSel = sel_registerName("alloc");
        var initSel = sel_registerName("initWithFrame:");

        var allocated = objc_msgSend_nint(cls, allocSel);
        var rect = new CGRect { X = 0, Y = 0, Width = 800, Height = 600 };
        var view = objc_msgSend_CGRect(allocated, initSel, rect);

        return view;
    }

    /// <summary>
    /// Creates an off-screen NSWindow and adds the given NSView as its
    /// content view. This ensures Metal can initialize its CAMetalLayer
    /// with a valid drawable, which is required for Ghostty's surface
    /// to compute font metrics and grid dimensions.
    /// Returns the NSWindow handle (caller must release it).
    /// </summary>
    public static nint CreateOffscreenWindowForView(nint nsView)
    {
        // NSWindow initWithContentRect:styleMask:backing:defer:
        var cls = objc_getClass("NSWindow");
        var allocSel = sel_registerName("alloc");
        var initSel = sel_registerName("initWithContentRect:styleMask:backing:defer:");
        var setContentViewSel = sel_registerName("setContentView:");
        var orderOutSel = sel_registerName("orderOut:");
        var setReleasedWhenClosedSel = sel_registerName("setReleasedWhenClosed:");

        var allocated = objc_msgSend_nint(cls, allocSel);

        // NSBorderlessWindowMask = 0, NSBackingStoreBuffered = 2
        var rect = new CGRect { X = -10000, Y = -10000, Width = 800, Height = 600 };
        var window = objc_msgSend_CGRect_uint_nint_byte(allocated, initSel, rect, 0, 2, 1);

        // Prevent auto-release on close
        objc_msgSend_bool(window, setReleasedWhenClosedSel, 0);

        // Set our view as the content view
        objc_msgSend_nint_arg(window, setContentViewSel, nsView);

        // Order out (keep it off-screen, not visible)
        objc_msgSend_nint_arg(window, orderOutSel, nint.Zero);

        return window;
    }

    /// <summary>
    /// Updates the frame size of an NSView in logical (point) units.
    /// </summary>
    public static void SetNSViewSize(nint view, double width, double height)
    {
        if (view == nint.Zero) return;

        double clampedWidth = Math.Max(1.0, width);
        double clampedHeight = Math.Max(1.0, height);

        var setFrameSel = sel_registerName("setFrame:");
        var frame = new CGRect { X = 0, Y = 0, Width = clampedWidth, Height = clampedHeight };
        objc_msgSend_void_CGRect(view, setFrameSel, frame);
    }

    /// <summary>
    /// Updates the content size of an NSWindow in logical (point) units.
    /// </summary>
    public static void SetNSWindowContentSize(nint window, double width, double height)
    {
        if (window == nint.Zero) return;

        double clampedWidth = Math.Max(1.0, width);
        double clampedHeight = Math.Max(1.0, height);

        var setContentSizeSel = sel_registerName("setContentSize:");
        var size = new CGSize { Width = clampedWidth, Height = clampedHeight };
        objc_msgSend_void_CGSize(window, setContentSizeSel, size);
    }

    /// <summary>Releases an NSWindow.</summary>
    public static void ReleaseNSWindow(nint window)
    {
        if (window == nint.Zero) return;
        var closeSel = sel_registerName("close");
        var releaseSel = sel_registerName("release");
        objc_msgSend_nint(window, closeSel);
        objc_msgSend_nint(window, releaseSel);
    }

    /// <summary>Releases an NSView.</summary>
    public static void ReleaseNSView(nint view)
    {
        if (view == nint.Zero) return;
        var releaseSel = sel_registerName("release");
        objc_msgSend_nint(view, releaseSel);
    }
}
