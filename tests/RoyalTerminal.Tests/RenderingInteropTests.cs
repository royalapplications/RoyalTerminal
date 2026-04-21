// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests - Interop tests for renderer C API bindings.

using System.Runtime.InteropServices;
using RoyalTerminal.Rendering.Contracts;
using RoyalTerminal.Rendering.Interop.Ghostty;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class RenderingInteropTests
{
    [Fact]
    public void GhosttyRenderInteropResultMapper_MapsKnownCodes()
    {
        Assert.Equal(GhosttyRenderInteropResult.Ok, GhosttyRenderInteropResultMapper.FromNativeCode(0));
        Assert.Equal(GhosttyRenderInteropResult.InvalidArgument, GhosttyRenderInteropResultMapper.FromNativeCode(1));
        Assert.Equal(GhosttyRenderInteropResult.UnsupportedBackend, GhosttyRenderInteropResultMapper.FromNativeCode(2));
        Assert.Equal(GhosttyRenderInteropResult.UnsupportedPlatform, GhosttyRenderInteropResultMapper.FromNativeCode(3));
        Assert.Equal(GhosttyRenderInteropResult.InvalidTarget, GhosttyRenderInteropResultMapper.FromNativeCode(4));
        Assert.Equal(GhosttyRenderInteropResult.RenderFailed, GhosttyRenderInteropResultMapper.FromNativeCode(5));
        Assert.Equal(GhosttyRenderInteropResult.OutOfMemory, GhosttyRenderInteropResultMapper.FromNativeCode(6));
    }

    [Fact]
    public void GhosttyRenderInteropResultMapper_MapsUnknownCode()
    {
        Assert.Equal(GhosttyRenderInteropResult.Unknown, GhosttyRenderInteropResultMapper.FromNativeCode(999));
    }

    [Fact]
    public void GhosttyRenderInteropResultMapper_GetMessage_DoesNotThrow()
    {
        string message = GhosttyRenderInteropResultMapper.GetMessage(12345);
        Assert.False(string.IsNullOrWhiteSpace(message));
    }

    [Fact(
        Skip = "Windows and Linux CI currently crash inside the native Ghostty software RGBA fixture path; interop validation and non-software target coverage remain exercised by companion rendering tests.",
        SkipType = typeof(TestPlatformConditions),
        SkipWhen = nameof(TestPlatformConditions.IsWindowsOrLinux))]
    public void GhosttyRenderSurface_WithFixtureLibrary_CanRenderRgba()
    {
        string? fixtureLibraryPath = ResolveFixtureLibraryPath();
        if (fixtureLibraryPath is null)
        {
            // Fixture native library is optional in local/unit-only runs.
            return;
        }

        string? originalPath = Environment.GetEnvironmentVariable("GHOSTTY_RENDERER_CAPI_LIBRARY_PATH");
        try
        {
            Environment.SetEnvironmentVariable("GHOSTTY_RENDERER_CAPI_LIBRARY_PATH", fixtureLibraryPath);

            using GhosttyRenderContext context = new();
            using GhosttyRenderSurface surface = context.CreateSurface(RenderBackendKind.Software);

            Assert.True(surface.Capabilities.SupportsFeatures(RenderFeatureFlags.ExplicitFrameLifecycle));
            Assert.True(surface.Capabilities.SupportsFeatures(RenderFeatureFlags.CpuRgbaFallback));
            Assert.False(surface.Capabilities.SupportsFeatures(RenderFeatureFlags.ExternalTextureTargets));

            Assert.Throws<ArgumentOutOfRangeException>(() => surface.SetSize(0, 32));
            Assert.Throws<ArgumentOutOfRangeException>(() => surface.SetScale(double.NaN, 1.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => surface.SetScale(1.0, double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => surface.RenderToRgba(new byte[32], 4, 4, 0));
            Assert.Throws<ArgumentException>(() => surface.RenderToRgba(new byte[16], 4, 4, 16));

            surface.SetSize(64, 32);
            surface.SetScale(1.0, 1.0);
            surface.SetFocus(true);
            surface.SetColorScheme(0);
            surface.SetTheme(new RenderTheme(
                defaultForegroundArgb: 0xFFFFFFFFu,
                defaultBackgroundArgb: 0xFF112233u,
                cursorArgb: 0xFFFFFFFFu));

            ulong frameToken = surface.BeginFrame();
            surface.EndFrame(frameToken);

            RenderTargetDescriptor descriptor = new()
            {
                BackendKind = RenderBackendKind.Software,
                TargetKind = RenderTargetKind.Framebuffer,
                PixelFormat = RenderPixelFormat.Unknown,
                Width = 64,
                Height = 32,
                SampleCount = 1,
                TargetHandle = (nint)1,
            };

            RenderValidationResult validation = surface.ValidateTarget(descriptor);
            Assert.True(validation.IsValid, validation.ErrorMessage);

            RenderTargetDescriptor invalidDescriptor = descriptor with { Width = 0 };
            RenderValidationResult invalidValidation = surface.ValidateTarget(invalidDescriptor);
            Assert.False(invalidValidation.IsValid);
            Assert.Contains("width", invalidValidation.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);

            RenderFrameResult invalidRenderResult = surface.Render(invalidDescriptor);
            Assert.False(invalidRenderResult.Succeeded);
            Assert.Contains("width", invalidRenderResult.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);

            byte[] rgbaBuffer = new byte[64 * 32 * 4];
            RenderFrameResult rgbaResult = surface.RenderToRgba(rgbaBuffer, 64, 32, 64 * 4);
            Assert.True(rgbaResult.Succeeded, rgbaResult.ErrorMessage);
            Assert.Contains(rgbaBuffer, static value => value != 0);
            Assert.Equal(0x11, rgbaBuffer[0]);
            Assert.Equal(0x22, rgbaBuffer[1]);
            Assert.Equal(0x33, rgbaBuffer[2]);
            Assert.Equal(0xFF, rgbaBuffer[3]);
            Assert.Equal(0xFF112233u, surface.Theme.DefaultBackgroundArgb);

            RenderFrameResult targetResult = surface.Render(descriptor);
            Assert.True(targetResult.Succeeded, targetResult.ErrorMessage);
            Assert.NotEqual(0UL, targetResult.SynchronizationToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GHOSTTY_RENDERER_CAPI_LIBRARY_PATH", originalPath);
        }
    }

    [Fact]
    public void GhosttyRenderSurface_WithFixtureLibrary_NonMetalBackendsValidateAndRenderTargets()
    {
        string? fixtureLibraryPath = ResolveFixtureLibraryPath();
        if (fixtureLibraryPath is null)
        {
            return;
        }

        string? originalPath = Environment.GetEnvironmentVariable("GHOSTTY_RENDERER_CAPI_LIBRARY_PATH");
        try
        {
            Environment.SetEnvironmentVariable("GHOSTTY_RENDERER_CAPI_LIBRARY_PATH", fixtureLibraryPath);

            using GhosttyRenderContext context = new();

            ValidateAndRenderDescriptor(
                context,
                RenderBackendKind.Vulkan,
                new RenderTargetDescriptor
                {
                    BackendKind = RenderBackendKind.Vulkan,
                    TargetKind = RenderTargetKind.Texture2D,
                    PixelFormat = RenderPixelFormat.Bgra8Unorm,
                    Width = 96,
                    Height = 54,
                    SampleCount = 1,
                    DeviceHandle = (nint)1,
                    CommandQueueHandle = (nint)2,
                    TargetHandle = (nint)3,
                    TargetViewHandle = (nint)4,
                },
                RenderFeatureFlags.ExternalTextureTargets);

            ValidateAndRenderDescriptor(
                context,
                RenderBackendKind.D3D11,
                new RenderTargetDescriptor
                {
                    BackendKind = RenderBackendKind.D3D11,
                    TargetKind = RenderTargetKind.Texture2D,
                    PixelFormat = RenderPixelFormat.Bgra8Unorm,
                    Width = 96,
                    Height = 54,
                    SampleCount = 1,
                    DeviceHandle = (nint)10,
                    TargetHandle = (nint)11,
                    TargetViewHandle = (nint)12,
                },
                RenderFeatureFlags.ExternalTextureTargets);

            ValidateAndRenderDescriptor(
                context,
                RenderBackendKind.D3D12,
                new RenderTargetDescriptor
                {
                    BackendKind = RenderBackendKind.D3D12,
                    TargetKind = RenderTargetKind.Texture2D,
                    PixelFormat = RenderPixelFormat.Bgra8Unorm,
                    Width = 96,
                    Height = 54,
                    SampleCount = 1,
                    DeviceHandle = (nint)20,
                    CommandQueueHandle = (nint)21,
                    CommandBufferHandle = (nint)22,
                    TargetHandle = (nint)23,
                    TargetViewHandle = (nint)24,
                },
                RenderFeatureFlags.ExternalTextureTargets);

            ValidateAndRenderDescriptor(
                context,
                RenderBackendKind.OpenGL,
                new RenderTargetDescriptor
                {
                    BackendKind = RenderBackendKind.OpenGL,
                    TargetKind = RenderTargetKind.Framebuffer,
                    PixelFormat = RenderPixelFormat.Unknown,
                    Width = 96,
                    Height = 54,
                    SampleCount = 1,
                    ContextHandle = (nint)30,
                    TargetHandle = nint.Zero,
                },
                RenderFeatureFlags.ExternalFramebufferTargets);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GHOSTTY_RENDERER_CAPI_LIBRARY_PATH", originalPath);
        }
    }

    private static void ValidateAndRenderDescriptor(
        GhosttyRenderContext context,
        RenderBackendKind backendKind,
        RenderTargetDescriptor descriptor,
        RenderFeatureFlags requiredDirectFeature)
    {
        using GhosttyRenderSurface surface = context.CreateSurface(backendKind);
        Assert.True(surface.Capabilities.SupportsFeatures(requiredDirectFeature));
        Assert.True(surface.Capabilities.SupportsFeatures(RenderFeatureFlags.ExplicitFrameLifecycle));
        Assert.True(surface.Capabilities.SupportsFeatures(RenderFeatureFlags.CpuRgbaFallback));

        RenderValidationResult validation = surface.ValidateTarget(descriptor);
        Assert.True(validation.IsValid, validation.ErrorMessage);

        RenderFrameResult frame = surface.Render(descriptor);
        Assert.True(frame.Succeeded, frame.ErrorMessage);
        Assert.NotEqual(0UL, frame.SynchronizationToken);
    }

    private static string? ResolveFixtureLibraryPath()
    {
        string? configuredPath = Environment.GetEnvironmentVariable("GHOSTTY_RENDERER_CAPI_LIBRARY_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        string? repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot is null)
        {
            return null;
        }

        string platformLibraryName = GetPlatformLibraryName();
        string sourceBuildCandidatePath = Path.Combine(
            repositoryRoot,
            "native",
            "ghostty-renderer-capi",
            "zig-out",
            "lib",
            platformLibraryName);

        if (File.Exists(sourceBuildCandidatePath))
        {
            return sourceBuildCandidatePath;
        }

        string runtimeRid = GetRuntimeIdentifier();
        string nativePackageDirectory = GetNativePackageDirectory();
        string packageRuntimeCandidatePath = Path.Combine(
            repositoryRoot,
            "src",
            nativePackageDirectory,
            "runtimes",
            runtimeRid,
            "native",
            platformLibraryName);

        if (File.Exists(packageRuntimeCandidatePath))
        {
            return packageRuntimeCandidatePath;
        }

        string scriptOutputCandidatePath = Path.Combine(
            repositoryRoot,
            "native",
            runtimeRid,
            platformLibraryName);

        return File.Exists(scriptOutputCandidatePath) ? scriptOutputCandidatePath : null;
    }

    private static string? FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string solutionPath = Path.Combine(current.FullName, "RoyalTerminal.sln");
            if (File.Exists(solutionPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string GetPlatformLibraryName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "ghostty-renderer-capi.dll";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "libghostty-renderer-capi.dylib";
        }

        return "libghostty-renderer-capi.so";
    }

    private static string GetRuntimeIdentifier()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "win-arm64",
                _ => "win-x64",
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "osx-x64",
                _ => "osx-arm64",
            };
        }

        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "linux-arm64",
            _ => "linux-x64",
        };
    }

    private static string GetNativePackageDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "RoyalTerminal.GhosttySharp.Native.Win64";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "RoyalTerminal.GhosttySharp.Native.OSX";
        }

        return "RoyalTerminal.GhosttySharp.Native.Linux64";
    }
}
