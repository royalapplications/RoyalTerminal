// Licensed under the MIT License.
// GhosttySharp.Tests — Tests for Ghostty rendered/native controls.

using Avalonia.Headless.XUnit;
using GhosttySharp;
using GhosttySharp.Avalonia.Controls;
using GhosttySharp.Avalonia.Diagnostics;
using GhosttySharp.Native;
using System.Runtime.Versioning;
using Xunit;

namespace GhosttySharp.Tests;

 [SupportedOSPlatform("macos")]
public class NativeTerminalControlTests
{
    [AvaloniaFact]
    public void RenderedControl_Logger_DefaultAndNullFallback()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        GhosttyRenderedTerminalControl control = new();
        Assert.NotNull(control.Logger);

        control.Logger = new TestLogger();
        Assert.IsType<TestLogger>(control.Logger);

        control.Logger = null!;
        Assert.NotNull(control.Logger);
    }

    [AvaloniaFact]
    public void NativeControl_Logger_DefaultAndNullFallback()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        GhosttyNativeTerminalControl control = new();
        Assert.NotNull(control.Logger);

        control.Logger = new TestLogger();
        Assert.IsType<TestLogger>(control.Logger);

        control.Logger = null!;
        Assert.NotNull(control.Logger);
    }

    [AvaloniaFact]
    public async Task RenderedControl_PublicApi_WithoutSurface_DoesNotThrow()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        GhosttyRenderedTerminalControl control = new();

        control.SetColorScheme(GhosttyColorScheme.Dark);
        control.SendInput("hello");
        control.RequestClose();
        await control.CopySelectionAsync();
        await control.PasteAsync();
        control.Dispose();
    }

    [AvaloniaFact]
    public async Task NativeControl_PublicApi_WithoutSurface_DoesNotThrow()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        GhosttyNativeTerminalControl control = new();

        control.SetColorScheme(GhosttyColorScheme.Light);
        control.SendInput("hello");
        control.RequestClose();
        await control.CopySelectionAsync();
        await control.PasteAsync();
        control.Dispose();
    }

    [AvaloniaFact]
    public void RenderedControl_Initialize_WithRealApp_OnMac_Smoke()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        if (!Ghostty.Initialize())
        {
            return;
        }

        using GhosttyConfig config = new();
        config.LoadDefaultFiles();
        config.Finalize_();

        using GhosttyApp app = new(config);

        GhosttyRenderedTerminalControl control = new();
        control.Initialize(app);
        control.Dispose();
    }

    [AvaloniaFact]
    public void NativeControl_Initialize_WithRealApp_OnMac_Smoke()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        if (!Ghostty.Initialize())
        {
            return;
        }

        using GhosttyConfig config = new();
        config.LoadDefaultFiles();
        config.Finalize_();

        using GhosttyApp app = new(config);

        GhosttyNativeTerminalControl control = new();
        control.Initialize(app);
        control.Dispose();
    }

    private sealed class TestLogger : IGhosttyLogger
    {
        public bool IsEnabled(GhosttyLogLevel level)
        {
            _ = level;
            return true;
        }

        public void Log(GhosttyLogLevel level, string message, Exception? exception = null)
        {
            _ = level;
            _ = message;
            _ = exception;
        }
    }
}
