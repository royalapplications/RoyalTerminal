// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Tests - Avalonia render-target adapter tests for texture interop mode.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.OpenGL;
using Avalonia.Platform;
using Avalonia.Skia;
using RoyalTerminal.Avalonia.Rendering.GhosttyInterop.Interop;
using RoyalTerminal.Rendering.Contracts;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class RenderingAvaloniaAdapterTests
{
    [Fact]
    public void RenderTargetProvider_WithoutPlatformLease_UsesSoftwareFallbackDescriptor()
    {
        AvaloniaSkiaRenderTargetProvider provider = new();
        using FakeSkiaLease lease = new(platformLease: null);

        var request = provider.CreateRenderRequest(lease, new PixelSize(320, 200));

        Assert.Equal(RenderBackendKind.Software, request.TargetDescriptor.BackendKind);
        Assert.Equal(RenderTargetKind.Framebuffer, request.TargetDescriptor.TargetKind);
        Assert.Equal(RenderPixelFormat.Unknown, request.TargetDescriptor.PixelFormat);
        Assert.Equal(320, request.TargetDescriptor.Width);
        Assert.Equal(200, request.TargetDescriptor.Height);
        Assert.Equal((nint)1, request.TargetDescriptor.TargetHandle);
    }

    [Fact]
    public void RenderTargetProvider_WithMetalContextAndTextureHandle_UsesMetalDescriptor()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return;
        }

        FakeMetalTextureHandleProvider textureProvider = new(
            success: true,
            deviceHandle: (nint)123,
            commandQueueHandle: (nint)456,
            textureHandle: (nint)99);
        AvaloniaSkiaRenderTargetProvider provider = new(
            metalTextureHandleProvider: textureProvider,
            backendPreference: AvaloniaRenderBackendPreference.Metal);

        FakePlatformContext context = new();
        using FakePlatformLease platformLease = new(context);
        using FakeSkiaLease lease = new(platformLease);

        var request = provider.CreateRenderRequest(lease, new PixelSize(640, 480));

        Assert.Equal(RenderBackendKind.Metal, request.TargetDescriptor.BackendKind);
        Assert.Equal(RenderTargetKind.Texture2D, request.TargetDescriptor.TargetKind);
        Assert.Equal(RenderPixelFormat.Bgra8Unorm, request.TargetDescriptor.PixelFormat);
        Assert.Equal((nint)123, request.TargetDescriptor.DeviceHandle);
        Assert.Equal((nint)456, request.TargetDescriptor.CommandQueueHandle);
        Assert.Equal((nint)99, request.TargetDescriptor.TargetHandle);
    }

    [Fact]
    public void RenderTargetProvider_MetalContextWithoutTextureHandle_FallsBackToSoftware()
    {
        FakeMetalTextureHandleProvider textureProvider = new(
            success: false,
            deviceHandle: nint.Zero,
            commandQueueHandle: nint.Zero,
            textureHandle: nint.Zero);
        AvaloniaSkiaRenderTargetProvider provider = new(
            metalTextureHandleProvider: textureProvider,
            backendPreference: AvaloniaRenderBackendPreference.Metal);

        FakePlatformContext context = new();
        using FakePlatformLease platformLease = new(context);
        using FakeSkiaLease lease = new(platformLease);

        var request = provider.CreateRenderRequest(lease, new PixelSize(500, 300));

        Assert.Equal(RenderBackendKind.Software, request.TargetDescriptor.BackendKind);
        Assert.Equal(RenderTargetKind.Framebuffer, request.TargetDescriptor.TargetKind);
        Assert.Equal((nint)1, request.TargetDescriptor.TargetHandle);
    }

    [Fact]
    public void RenderTargetProvider_BackendPreferenceSoftware_UsesSoftwareFallbackAndReportsDiagnostic()
    {
        AvaloniaSkiaRenderTargetProvider provider = new(
            backendPreference: AvaloniaRenderBackendPreference.Software);
        using FakeSkiaLease lease = new(platformLease: null);

        var request = provider.CreateRenderRequest(lease, new PixelSize(120, 80));

        Assert.Equal(RenderBackendKind.Software, request.TargetDescriptor.BackendKind);
        Assert.NotNull(provider.LastDiagnostic);
        Assert.Contains("software", provider.LastDiagnostic!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenderTargetProvider_UnsupportedBackendPreference_FallsBackWithDiagnostic()
    {
        AvaloniaRenderBackendPreference unsupportedPreference = GetUnsupportedBackendPreferenceForHost();
        AvaloniaSkiaRenderTargetProvider provider = new(backendPreference: unsupportedPreference);

        FakePlatformContext context = new();
        using FakePlatformLease platformLease = new(context);
        using FakeSkiaLease lease = new(platformLease);

        var request = provider.CreateRenderRequest(lease, new PixelSize(64, 64));

        Assert.Equal(RenderBackendKind.Software, request.TargetDescriptor.BackendKind);
        Assert.NotNull(provider.LastDiagnostic);
        Assert.Contains("not compatible", provider.LastDiagnostic!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenderTargetProvider_DiagnosticEvent_IsDeduplicated()
    {
        AvaloniaSkiaRenderTargetProvider provider = new(
            backendPreference: AvaloniaRenderBackendPreference.Software);
        int eventCount = 0;
        provider.DiagnosticReported += (_, _) => eventCount++;

        using FakeSkiaLease lease = new(platformLease: null);
        _ = provider.CreateRenderRequest(lease, new PixelSize(16, 16));
        _ = provider.CreateRenderRequest(lease, new PixelSize(16, 16));

        Assert.Equal(1, eventCount);
    }

    [Fact]
    public void RenderTargetProvider_UnsupportedBackend_DiagnosticEvent_IsSinglePerRequest()
    {
        AvaloniaRenderBackendPreference unsupportedPreference = GetUnsupportedBackendPreferenceForHost();
        AvaloniaSkiaRenderTargetProvider provider = new(backendPreference: unsupportedPreference);

        int eventCount = 0;
        provider.DiagnosticReported += (_, _) => eventCount++;

        FakePlatformContext context = new();
        using FakePlatformLease platformLease = new(context);
        using FakeSkiaLease lease = new(platformLease);

        _ = provider.CreateRenderRequest(lease, new PixelSize(64, 64));

        Assert.Equal(1, eventCount);
    }

    [Fact]
    public void RenderTargetProvider_ZeroPixelSize_IsClampedToOneByOne()
    {
        AvaloniaSkiaRenderTargetProvider provider = new();
        using FakeSkiaLease lease = new(platformLease: null);

        var request = provider.CreateRenderRequest(lease, new PixelSize(0, 0));

        Assert.Equal(1, request.TargetDescriptor.Width);
        Assert.Equal(1, request.TargetDescriptor.Height);
    }

    [Fact]
    public void RenderTargetProvider_AutoWithOpenGlContext_FallsBackWithOpenGlDiagnostic()
    {
        AvaloniaSkiaRenderTargetProvider provider = new();

        using FakePlatformLease platformLease = new(new FakeOpenGlContext());
        using FakeSkiaLease lease = new(platformLease);

        var request = provider.CreateRenderRequest(lease, new PixelSize(48, 24));

        Assert.Equal(RenderBackendKind.Software, request.TargetDescriptor.BackendKind);
        Assert.NotNull(provider.LastDiagnostic);
        Assert.Contains("opengl", provider.LastDiagnostic!, StringComparison.OrdinalIgnoreCase);
    }

    private static AvaloniaRenderBackendPreference GetUnsupportedBackendPreferenceForHost()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return AvaloniaRenderBackendPreference.D3D11;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return AvaloniaRenderBackendPreference.Metal;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return AvaloniaRenderBackendPreference.Metal;
        }

        return AvaloniaRenderBackendPreference.Metal;
    }

    private sealed class FakeMetalTextureHandleProvider : IAvaloniaMetalTextureHandleProvider
    {
        private readonly bool _success;
        private readonly nint _deviceHandle;
        private readonly nint _commandQueueHandle;
        private readonly nint _textureHandle;

        public FakeMetalTextureHandleProvider(
            bool success,
            nint deviceHandle,
            nint commandQueueHandle,
            nint textureHandle)
        {
            _success = success;
            _deviceHandle = deviceHandle;
            _commandQueueHandle = commandQueueHandle;
            _textureHandle = textureHandle;
        }

        public bool TryGetHandles(
            ISkiaSharpApiLease lease,
            IPlatformGraphicsContext context,
            out nint deviceHandle,
            out nint commandQueueHandle,
            out nint textureHandle)
        {
            deviceHandle = _deviceHandle;
            commandQueueHandle = _commandQueueHandle;
            textureHandle = _textureHandle;
            return _success;
        }
    }

    private sealed class FakeSkiaLease : ISkiaSharpApiLease
    {
        private readonly SKSurface _surface;
        private readonly ISkiaSharpPlatformGraphicsApiLease? _platformLease;

        public FakeSkiaLease(ISkiaSharpPlatformGraphicsApiLease? platformLease)
        {
            _platformLease = platformLease;
            _surface = SKSurface.Create(new SKImageInfo(1, 1)) ?? throw new InvalidOperationException("Failed to create test surface.");
        }

        public SKCanvas SkCanvas => _surface.Canvas;

        public GRContext? GrContext => null;

        public SKSurface? SkSurface => _surface;

        public double CurrentOpacity => 1.0;

        public ISkiaSharpPlatformGraphicsApiLease? TryLeasePlatformGraphicsApi() => _platformLease;

        public void Dispose()
        {
            _surface.Dispose();
        }
    }

    private sealed class FakePlatformLease : ISkiaSharpPlatformGraphicsApiLease
    {
        public FakePlatformLease(IPlatformGraphicsContext context)
        {
            Context = context;
        }

        public IPlatformGraphicsContext Context { get; }

        public void Dispose()
        {
            Context.Dispose();
        }
    }

    private sealed class FakePlatformContext : IPlatformGraphicsContext
    {
        public bool IsLost => false;

        public IDisposable EnsureCurrent() => DummyDisposable.Instance;

        public object? TryGetFeature(Type featureType) => null;

        public void Dispose()
        {
        }
    }

    private sealed class FakeOpenGlContext : IGlContext
    {
        public GlVersion Version => default;

        public GlInterface GlInterface => throw new NotSupportedException();

        public int SampleCount => 1;

        public int StencilSize => 8;

        public IDisposable MakeCurrent() => DummyDisposable.Instance;

        public bool IsSharedWith(IGlContext context) => false;

        public bool CanCreateSharedContext => false;

        public IGlContext? CreateSharedContext(IEnumerable<GlVersion>? preferredVersions = null) => null;

        public bool IsLost => false;

        public IDisposable EnsureCurrent() => DummyDisposable.Instance;

        public object? TryGetFeature(Type featureType) => null;

        public void Dispose()
        {
        }
    }

    private sealed class DummyDisposable : IDisposable
    {
        public static DummyDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
