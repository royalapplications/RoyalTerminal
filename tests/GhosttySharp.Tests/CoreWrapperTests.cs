// Licensed under the MIT License.
// GhosttySharp.Tests — Wrapper lifecycle and guard tests.

using GhosttySharp;
using GhosttySharp.Native;
using Xunit;

namespace GhosttySharp.Tests;

public class CoreWrapperTests
{
    [Fact]
    public void NativeLibraryLoader_Initialize_IsIdempotent()
    {
        NativeLibraryLoader.Initialize();
        NativeLibraryLoader.Initialize();
    }

    [Fact]
    public void Ghostty_Initialize_IsIdempotent()
    {
        bool first = Ghostty.Initialize();
        bool second = Ghostty.Initialize();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Ghostty_GetInfo_WhenInitialized_ReturnsVersion()
    {
        if (!Ghostty.Initialize())
        {
            return;
        }

        GhosttyLibraryInfo info = Ghostty.GetInfo();
        Assert.False(string.IsNullOrWhiteSpace(info.Version));
    }

    [Fact]
    public void GhosttyConfig_InternalWrapper_NonOwningLifecycle_Works()
    {
        GhosttyConfig config = new((nint)0x1001, ownsHandle: false);

        Assert.True(config.IsValid);
        Assert.Equal((nint)0x1001, config.Handle);

        config.Dispose();

        Assert.False(config.IsValid);
        Assert.Throws<ObjectDisposedException>(() => _ = config.Handle);
        Assert.Throws<ObjectDisposedException>(() => config.LoadCliArgs());
    }

    [Fact]
    public void GhosttyApp_InternalWrapper_NonOwningLifecycle_Works()
    {
        GhosttyApp app = new((nint)0x1002, ownsHandle: false);

        Assert.True(app.IsValid);
        Assert.Equal((nint)0x1002, app.Handle);

        app.Dispose();

        Assert.False(app.IsValid);
        Assert.Throws<ObjectDisposedException>(() => _ = app.Handle);
        Assert.Throws<ObjectDisposedException>(() => app.Tick());
    }

    [Fact]
    public void GhosttySurface_InternalWrapper_NonOwningLifecycle_Works()
    {
        GhosttySurface surface = new((nint)0x1003, ownsHandle: false);

        Assert.True(surface.IsValid);
        Assert.Equal((nint)0x1003, surface.Handle);

        surface.Dispose();

        Assert.False(surface.IsValid);
        Assert.Throws<ObjectDisposedException>(() => _ = surface.Handle);
        Assert.Throws<ObjectDisposedException>(() => surface.Refresh());
    }

    [Fact]
    public void GhosttyInspector_InternalWrapper_NonOwningLifecycle_Works()
    {
        GhosttyInspector inspector = new((nint)0x1004, (nint)0x1003, ownsHandle: false);

        Assert.True(inspector.IsValid);
        Assert.Equal((nint)0x1004, inspector.Handle);

        inspector.Dispose();

        Assert.False(inspector.IsValid);
        Assert.Throws<ObjectDisposedException>(() => _ = inspector.Handle);
        Assert.Throws<ObjectDisposedException>(() => inspector.SetFocus(true));
    }

    [Fact]
    public void GhosttyConfig_CreateAndClone_Smoke()
    {
        if (!Ghostty.Initialize())
        {
            return;
        }

        using GhosttyConfig config = new();
        using GhosttyConfig clone = config.Clone();

        Assert.True(config.IsValid);
        Assert.True(clone.IsValid);
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
        app.NotifyKeyboardChanged();

        _ = app.NeedsConfirmQuit;
        _ = app.HasGlobalKeybinds;
    }
}
