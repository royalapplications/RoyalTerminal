// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Runtime.InteropServices;
using Xunit;
using RoyalTerminal.Rendering.Contracts;
using RoyalTerminal.Rendering.Interop.Swift;
using RoyalTerminal.Avalonia.Rendering;

namespace RoyalTerminal.Tests;

public class SwiftRendererInteropTests
{
    [Fact]
    public void SwiftRenderer_CanCreateAndDisposeSurface()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Swift renderer requires Metal and is macOS only
            return;
        }

        using SwiftRenderSurface surface = new(RenderBackendKind.Metal);

        Assert.Equal(RenderBackendKind.Metal, surface.BackendKind);
        Assert.True(surface.Capabilities.SupportsFeatures(RenderFeatureFlags.ExternalTextureTargets));
        Assert.True(surface.Capabilities.SupportsFeatures(RenderFeatureFlags.CpuRgbaFallback));

        surface.SetSize(800, 600);
        surface.SetScale(2.0, 2.0);
        surface.SetFocus(true);
        surface.SetTheme(0xFFFFFFFF, 0xFF1E1E1E, 0xFFFFFFFF);
    }

    [Fact]
    public void SwiftRenderer_CanRenderToRgbaFallback()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return;
        }

        using SwiftRenderSurface surface = new(RenderBackendKind.Metal);
        surface.SetSize(120, 40);
        surface.SetScale(1.0, 1.0);

        TerminalScreen screen = new(columns: 12, viewportRows: 4, scrollbackLimit: 100);
        
        lock (screen.SyncRoot)
        {
            TerminalRow row0 = screen.GetViewportRow(0);
            row0[0] = new TerminalCell { Codepoint = 'H', Foreground = 0xFFFFFFFF, Background = 0xFF000000 };
            row0[1] = new TerminalCell { Codepoint = 'e', Foreground = 0xFFFFFFFF, Background = 0xFF000000 };
            row0[2] = new TerminalCell { Codepoint = 'l', Foreground = 0xFFFFFFFF, Background = 0xFF000000 };
            row0[3] = new TerminalCell { Codepoint = 'l', Foreground = 0xFFFFFFFF, Background = 0xFF000000 };
            row0[4] = new TerminalCell { Codepoint = 'o', Foreground = 0xFFFFFFFF, Background = 0xFF000000 };
        }

        surface.UpdateScreenState(
            screen,
            cursorCol: 5,
            cursorRow: 0,
            cursorVisible: true,
            cursorStyle: 0
        );

        int width = 120;
        int height = 40;
        int stride = width * 4;
        byte[] buffer = new byte[stride * height];

        RenderFrameResult result = surface.RenderToRgba(buffer, width, height, stride);

        Assert.True(result.Succeeded, result.ErrorMessage);
    }
}
