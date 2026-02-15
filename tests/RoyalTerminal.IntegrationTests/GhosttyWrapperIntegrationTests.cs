// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.IntegrationTests — Integration tests for core Ghostty wrappers.

using System.Runtime.InteropServices;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.GhosttySharp.Native;
using Xunit;

namespace RoyalTerminal.IntegrationTests;

public class GhosttyWrapperIntegrationTests
{
    [Fact]
    public void Ghostty_InitializeAndGetInfo_Smoke()
    {
        bool initialized = Ghostty.Initialize();
        if (!initialized)
        {
            return;
        }

        GhosttyLibraryInfo info = Ghostty.GetInfo();
        Assert.False(string.IsNullOrWhiteSpace(info.Version));
    }

    [Fact]
    public void GhosttyConfig_CreateFinalizeClone_Smoke()
    {
        if (!Ghostty.Initialize())
        {
            return;
        }

        using GhosttyConfig config = new();
        config.LoadDefaultFiles();
        config.Finalize_();

        using GhosttyConfig clone = config.Clone();
        Assert.True(config.IsValid);
        Assert.True(clone.IsValid);

        _ = config.DiagnosticsCount;
        _ = GhosttyConfig.GetOpenPath();
    }

    [Fact]
    public void GhosttyApp_CreateAndBasicCalls_Smoke()
    {
        if (!Ghostty.Initialize())
        {
            return;
        }

        using GhosttyConfig config = new();
        config.LoadDefaultFiles();
        config.Finalize_();

        using GhosttyApp app = new(config);
        Assert.True(app.IsValid);

        app.Tick();
        app.SetFocus(true);
        app.SetFocus(false);
        app.NotifyKeyboardChanged();
        _ = app.NeedsConfirmQuit;
        _ = app.HasGlobalKeybinds;
    }

    [Fact]
    public unsafe void GhosttySurfaceAndInspector_OnMac_CanCreateAndDispose()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        if (!ObjCRuntimeTestHelper.IsMainThread())
        {
            return;
        }

        if (!Ghostty.Initialize())
        {
            return;
        }

        nint nsView = ObjCRuntimeTestHelper.CreateNSView();
        if (nsView == nint.Zero)
        {
            return;
        }

        nint nsWindow = ObjCRuntimeTestHelper.CreateOffscreenWindowForView(nsView);
        if (nsWindow == nint.Zero)
        {
            ObjCRuntimeTestHelper.ReleaseNSView(nsView);
            return;
        }

        try
        {
            using GhosttyConfig config = new();
            config.LoadDefaultFiles();
            config.Finalize_();

            using GhosttyApp app = new(config);

            GhosttySurfaceConfig surfaceConfig = GhosttyNative.SurfaceConfigNew();
            surfaceConfig.PlatformTag = GhosttyPlatform.MacOS;
            surfaceConfig.Platform = new GhosttyPlatformUnion
            {
                MacOS = new GhosttyPlatformMacOS
                {
                    NSView = nsView,
                },
            };
            surfaceConfig.ScaleFactor = 1.0;
            surfaceConfig.FontSize = 14.0f;
            surfaceConfig.Context = GhosttySurfaceContext.Window;

            using GhosttySurface surface = new(app, ref surfaceConfig);
            Assert.True(surface.IsValid);

            surface.SetContentScale(1.0, 1.0);
            surface.SetSize(800, 600);
            surface.SetFocus(true);
            surface.Refresh();

            using GhosttyInspector inspector = new(surface);
            Assert.True(inspector.IsValid);

            inspector.SetFocus(true);
            inspector.SetContentScale(1.0, 1.0);
            inspector.SetSize(640, 480);
            inspector.SendMousePos(10, 10);
            inspector.SendMouseScroll(0, -1);
        }
        finally
        {
            ObjCRuntimeTestHelper.ReleaseNSWindow(nsWindow);
            ObjCRuntimeTestHelper.ReleaseNSView(nsView);
        }
    }

    private static class ObjCRuntimeTestHelper
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct CGRect
        {
            public double X;
            public double Y;
            public double Width;
            public double Height;
        }

        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern nint objc_getClass(string name);

        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern nint sel_registerName(string name);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern nint objc_msgSend_nint(nint receiver, nint selector);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern nint objc_msgSend_CGRect(nint receiver, nint selector, CGRect rect);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_bool(nint receiver, nint selector, byte value);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_nint_arg(nint receiver, nint selector, nint arg);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern nint objc_msgSend_CGRect_uint_nint_byte(
            nint receiver,
            nint selector,
            CGRect rect,
            uint styleMask,
            nint backing,
            byte defer_);

        [DllImport("/usr/lib/libSystem.B.dylib")]
        private static extern int pthread_main_np();

        public static bool IsMainThread()
        {
            return pthread_main_np() == 1;
        }

        public static nint CreateNSView()
        {
            nint cls = objc_getClass("NSView");
            nint allocSel = sel_registerName("alloc");
            nint initSel = sel_registerName("initWithFrame:");

            nint allocated = objc_msgSend_nint(cls, allocSel);
            CGRect rect = new()
            {
                X = 0,
                Y = 0,
                Width = 800,
                Height = 600,
            };
            return objc_msgSend_CGRect(allocated, initSel, rect);
        }

        public static nint CreateOffscreenWindowForView(nint nsView)
        {
            nint cls = objc_getClass("NSWindow");
            nint allocSel = sel_registerName("alloc");
            nint initSel = sel_registerName("initWithContentRect:styleMask:backing:defer:");
            nint setContentViewSel = sel_registerName("setContentView:");
            nint orderOutSel = sel_registerName("orderOut:");
            nint setReleasedWhenClosedSel = sel_registerName("setReleasedWhenClosed:");

            nint allocated = objc_msgSend_nint(cls, allocSel);
            CGRect rect = new()
            {
                X = -10000,
                Y = -10000,
                Width = 800,
                Height = 600,
            };

            // NSBorderlessWindowMask = 0, NSBackingStoreBuffered = 2
            nint window = objc_msgSend_CGRect_uint_nint_byte(allocated, initSel, rect, 0, 2, 1);
            if (window == nint.Zero)
            {
                return nint.Zero;
            }

            objc_msgSend_bool(window, setReleasedWhenClosedSel, 0);
            objc_msgSend_nint_arg(window, setContentViewSel, nsView);
            objc_msgSend_nint_arg(window, orderOutSel, nint.Zero);
            return window;
        }

        public static void ReleaseNSWindow(nint window)
        {
            if (window == nint.Zero)
            {
                return;
            }

            nint closeSel = sel_registerName("close");
            nint releaseSel = sel_registerName("release");
            objc_msgSend_nint(window, closeSel);
            objc_msgSend_nint(window, releaseSel);
        }

        public static void ReleaseNSView(nint view)
        {
            if (view == nint.Zero)
            {
                return;
            }

            nint releaseSel = sel_registerName("release");
            objc_msgSend_nint(view, releaseSel);
        }
    }
}
