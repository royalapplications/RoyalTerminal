// Licensed under the MIT License.
// GhosttySharp.Tests - Skia interop bridge tests.

using GhosttySharp.Rendering.Contracts;
using GhosttySharp.Rendering.Interop.Skia;
using SkiaSharp;
using Xunit;

namespace GhosttySharp.Tests;

public sealed class RenderingSkiaInteropTests
{
    [Fact]
    public void SkiaInteropRenderer_DirectMetalPath_SkipsFallback()
    {
        FakeRenderSurface surface = new(RenderFrameResult.Success());
        FakeRgbaFallbackRenderer fallback = new(RenderFrameResult.Success());
        SkiaInteropRenderer renderer = new(surface, fallback);

        using SKBitmap bitmap = new(8, 8);
        using SKCanvas canvas = new(bitmap);

        RenderTargetDescriptor descriptor = CreateMetalDescriptor(8, 8);
        SkiaInteropRenderRequest request = new() { TargetDescriptor = descriptor, AllowCpuFallback = true };

        SkiaInteropRenderResult result = renderer.Render(canvas, request);

        Assert.True(result.FrameResult.Succeeded, result.FrameResult.ErrorMessage);
        Assert.False(result.UsedCpuFallback);
        Assert.Equal(1, surface.ValidateTargetCallCount);
        Assert.Equal(1, surface.RenderCallCount);
        Assert.Equal(0, fallback.CallCount);
    }

    [Fact]
    public void SkiaInteropRenderer_DirectFailure_UsesCpuFallbackAndDraws()
    {
        FakeRenderSurface surface = new(RenderFrameResult.Failure("texture wrapping failed"));
        FakeRgbaFallbackRenderer fallback = new(
            RenderFrameResult.Success(),
            fillColor: new byte[] { 255, 0, 0, 255 });
        SkiaInteropRenderer renderer = new(surface, fallback);

        using SKBitmap bitmap = new(4, 4, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using SKCanvas canvas = new(bitmap);
        canvas.Clear(SKColors.Black);

        RenderTargetDescriptor descriptor = CreateMetalDescriptor(4, 4);
        SkiaInteropRenderRequest request = new()
        {
            TargetDescriptor = descriptor,
            AllowCpuFallback = true,
            ClearCanvasBeforeFallbackDraw = true,
            ClearColor = SKColors.Transparent,
        };

        SkiaInteropRenderResult result = renderer.Render(canvas, request);

        Assert.True(result.FrameResult.Succeeded, result.FrameResult.ErrorMessage);
        Assert.True(result.UsedCpuFallback);
        Assert.Equal(1, surface.ValidateTargetCallCount);
        Assert.Equal(1, surface.RenderCallCount);
        Assert.Equal(1, fallback.CallCount);

        SKColor pixel = bitmap.GetPixel(0, 0);
        Assert.Equal((byte)255, pixel.Red);
        Assert.Equal((byte)0, pixel.Green);
        Assert.Equal((byte)0, pixel.Blue);
        Assert.Equal((byte)255, pixel.Alpha);
    }

    [Fact]
    public void SkiaInteropRenderer_UnsupportedBackendWithoutFallback_ReturnsFailure()
    {
        FakeRenderSurface surface = new(RenderFrameResult.Success());
        SkiaInteropRenderer renderer = new(surface);

        using SKBitmap bitmap = new(4, 4);
        using SKCanvas canvas = new(bitmap);

        RenderTargetDescriptor descriptor = new()
        {
            BackendKind = RenderBackendKind.Vulkan,
            TargetKind = RenderTargetKind.Texture2D,
            PixelFormat = RenderPixelFormat.Bgra8Unorm,
            Width = 4,
            Height = 4,
            SampleCount = 1,
            DeviceHandle = (nint)1,
            TargetHandle = (nint)2,
        };

        SkiaInteropRenderRequest request = new()
        {
            TargetDescriptor = descriptor,
            AllowCpuFallback = false,
        };

        SkiaInteropRenderResult result = renderer.Render(canvas, request);

        Assert.False(result.FrameResult.Succeeded);
        Assert.False(result.UsedCpuFallback);
        Assert.Equal(0, surface.ValidateTargetCallCount);
        Assert.Equal(0, surface.RenderCallCount);
    }

    [Fact]
    public void SkiaInteropRenderer_VulkanDirectPath_UsesSurfaceRender()
    {
        FakeRenderSurface surface = new(
            RenderFrameResult.Success(),
            backendKind: RenderBackendKind.Vulkan);
        FakeRgbaFallbackRenderer fallback = new(RenderFrameResult.Success());
        SkiaInteropRenderer renderer = new(surface, fallback);

        using SKBitmap bitmap = new(4, 4);
        using SKCanvas canvas = new(bitmap);

        RenderTargetDescriptor descriptor = new()
        {
            BackendKind = RenderBackendKind.Vulkan,
            TargetKind = RenderTargetKind.Texture2D,
            PixelFormat = RenderPixelFormat.Bgra8Unorm,
            Width = 4,
            Height = 4,
            SampleCount = 1,
            DeviceHandle = (nint)1,
            CommandQueueHandle = (nint)2,
            TargetHandle = (nint)3,
            TargetViewHandle = (nint)4,
        };

        SkiaInteropRenderRequest request = new()
        {
            TargetDescriptor = descriptor,
            AllowCpuFallback = true,
        };

        SkiaInteropRenderResult result = renderer.Render(canvas, request);

        Assert.True(result.FrameResult.Succeeded, result.FrameResult.ErrorMessage);
        Assert.False(result.UsedCpuFallback);
        Assert.Equal(1, surface.ValidateTargetCallCount);
        Assert.Equal(1, surface.RenderCallCount);
        Assert.Equal(0, fallback.CallCount);
    }

    [Fact]
    public void SkiaInteropRenderer_WhenSurfaceCapabilitiesDoNotAdvertiseExternalTargets_UsesFallback()
    {
        FakeRenderSurface surface = new(
            RenderFrameResult.Success(),
            backendKind: RenderBackendKind.Vulkan,
            featureFlags: RenderFeatureFlags.CpuRgbaFallback);
        FakeRgbaFallbackRenderer fallback = new(RenderFrameResult.Success());
        SkiaInteropRenderer renderer = new(surface, fallback);

        using SKBitmap bitmap = new(4, 4, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using SKCanvas canvas = new(bitmap);

        RenderTargetDescriptor descriptor = new()
        {
            BackendKind = RenderBackendKind.Vulkan,
            TargetKind = RenderTargetKind.Texture2D,
            PixelFormat = RenderPixelFormat.Bgra8Unorm,
            Width = 4,
            Height = 4,
            SampleCount = 1,
            DeviceHandle = (nint)1,
            CommandQueueHandle = (nint)2,
            TargetHandle = (nint)3,
            TargetViewHandle = (nint)4,
        };

        SkiaInteropRenderRequest request = new()
        {
            TargetDescriptor = descriptor,
            AllowCpuFallback = true,
        };

        SkiaInteropRenderResult result = renderer.Render(canvas, request);

        Assert.True(result.FrameResult.Succeeded, result.FrameResult.ErrorMessage);
        Assert.True(result.UsedCpuFallback);
        Assert.Equal(0, surface.ValidateTargetCallCount);
        Assert.Equal(0, surface.RenderCallCount);
        Assert.Equal(1, fallback.CallCount);
    }

    [Fact]
    public void SkiaInteropRenderer_NonMetalWithFallback_UsesCpuFallback()
    {
        FakeRenderSurface surface = new(RenderFrameResult.Success());
        FakeRgbaFallbackRenderer fallback = new(RenderFrameResult.Success());
        SkiaInteropRenderer renderer = new(surface, fallback);

        using SKBitmap bitmap = new(4, 4, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using SKCanvas canvas = new(bitmap);

        RenderTargetDescriptor descriptor = new()
        {
            BackendKind = RenderBackendKind.Software,
            TargetKind = RenderTargetKind.Framebuffer,
            PixelFormat = RenderPixelFormat.Unknown,
            Width = 4,
            Height = 4,
            SampleCount = 1,
            TargetHandle = (nint)1,
        };

        SkiaInteropRenderRequest request = new() { TargetDescriptor = descriptor, AllowCpuFallback = true };

        SkiaInteropRenderResult result = renderer.Render(canvas, request);

        Assert.True(result.FrameResult.Succeeded, result.FrameResult.ErrorMessage);
        Assert.True(result.UsedCpuFallback);
        Assert.Equal(0, surface.ValidateTargetCallCount);
        Assert.Equal(0, surface.RenderCallCount);
        Assert.Equal(1, fallback.CallCount);
    }

    [Fact]
    public void SkiaInteropRenderer_InvalidDescriptor_DoesNotCallSurfaceOrFallback()
    {
        FakeRenderSurface surface = new(RenderFrameResult.Success());
        FakeRgbaFallbackRenderer fallback = new(RenderFrameResult.Success());
        SkiaInteropRenderer renderer = new(surface, fallback);

        using SKBitmap bitmap = new(4, 4);
        using SKCanvas canvas = new(bitmap);

        RenderTargetDescriptor descriptor = CreateMetalDescriptor(0, 4);
        SkiaInteropRenderRequest request = new()
        {
            TargetDescriptor = descriptor,
            AllowCpuFallback = true,
        };

        SkiaInteropRenderResult result = renderer.Render(canvas, request);

        Assert.False(result.FrameResult.Succeeded);
        Assert.False(result.UsedCpuFallback);
        Assert.Equal(0, surface.ValidateTargetCallCount);
        Assert.Equal(0, surface.RenderCallCount);
        Assert.Equal(0, fallback.CallCount);
    }

    [Fact]
    public void SkiaInteropRenderer_DirectValidationFailure_UsesFallbackWithoutRenderCall()
    {
        FakeRenderSurface surface = new(
            RenderFrameResult.Success(),
            validationResult: RenderValidationResult.Invalid("Direct texture interop unsupported on this host."));
        FakeRgbaFallbackRenderer fallback = new(RenderFrameResult.Success());
        SkiaInteropRenderer renderer = new(surface, fallback);

        using SKBitmap bitmap = new(4, 4, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using SKCanvas canvas = new(bitmap);

        RenderTargetDescriptor descriptor = CreateMetalDescriptor(4, 4);
        SkiaInteropRenderRequest request = new() { TargetDescriptor = descriptor, AllowCpuFallback = true };

        SkiaInteropRenderResult result = renderer.Render(canvas, request);

        Assert.True(result.FrameResult.Succeeded, result.FrameResult.ErrorMessage);
        Assert.True(result.UsedCpuFallback);
        Assert.Equal(1, surface.ValidateTargetCallCount);
        Assert.Equal(0, surface.RenderCallCount);
        Assert.Equal(1, fallback.CallCount);
    }

    [Fact]
    public void SkiaInteropRenderer_DirectAndFallbackFailure_CombinesErrors()
    {
        FakeRenderSurface surface = new(RenderFrameResult.Failure("Metal interop failed."));
        FakeRgbaFallbackRenderer fallback = new(RenderFrameResult.Failure("RGBA fallback failed."));
        SkiaInteropRenderer renderer = new(surface, fallback);

        using SKBitmap bitmap = new(4, 4);
        using SKCanvas canvas = new(bitmap);

        RenderTargetDescriptor descriptor = CreateMetalDescriptor(4, 4);
        SkiaInteropRenderRequest request = new()
        {
            TargetDescriptor = descriptor,
            AllowCpuFallback = true,
        };

        SkiaInteropRenderResult result = renderer.Render(canvas, request);

        Assert.False(result.FrameResult.Succeeded);
        Assert.True(result.UsedCpuFallback);
        Assert.NotNull(result.FrameResult.ErrorMessage);
        Assert.Contains("Metal interop failed.", result.FrameResult.ErrorMessage!, StringComparison.Ordinal);
        Assert.Contains("RGBA fallback failed.", result.FrameResult.ErrorMessage!, StringComparison.Ordinal);
    }

    private static RenderTargetDescriptor CreateMetalDescriptor(int width, int height)
    {
        return new RenderTargetDescriptor
        {
            BackendKind = RenderBackendKind.Metal,
            TargetKind = RenderTargetKind.Texture2D,
            PixelFormat = RenderPixelFormat.Bgra8Unorm,
            Width = width,
            Height = height,
            SampleCount = 1,
            DeviceHandle = (nint)1,
            TargetHandle = (nint)2,
        };
    }

    private sealed class FakeRenderSurface : IRenderSurface
    {
        private readonly RenderFrameResult _renderResult;
        private readonly RenderValidationResult? _validationResult;
        private readonly RenderBackendKind _backendKind;

        public FakeRenderSurface(
            RenderFrameResult renderResult,
            RenderValidationResult? validationResult = null,
            RenderBackendKind backendKind = RenderBackendKind.Metal,
            RenderFeatureFlags featureFlags = RenderFeatureFlags.ExternalTextureTargets | RenderFeatureFlags.CpuRgbaFallback)
        {
            _renderResult = renderResult;
            _validationResult = validationResult;
            _backendKind = backendKind;
            Capabilities = new RenderBackendCapabilities(
                backendKind,
                featureFlags,
                minSampleCount: 1,
                maxSampleCount: 1,
                supportedPixelFormats: new[] { RenderPixelFormat.Bgra8Unorm });
        }

        public RenderBackendKind BackendKind => _backendKind;

        public RenderBackendCapabilities Capabilities { get; }

        public int RenderCallCount { get; private set; }

        public int ValidateTargetCallCount { get; private set; }

        public void SetSize(int width, int height)
        {
        }

        public void SetScale(double scaleX, double scaleY)
        {
        }

        public RenderValidationResult ValidateTarget(in RenderTargetDescriptor descriptor)
        {
            ValidateTargetCallCount++;
            if (_validationResult.HasValue)
            {
                return _validationResult.Value;
            }

            return RenderTargetDescriptorValidator.Validate(descriptor);
        }

        public RenderFrameResult Render(in RenderTargetDescriptor descriptor)
        {
            RenderCallCount++;
            return _renderResult;
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeRgbaFallbackRenderer : ISkiaRgbaFallbackRenderer
    {
        private readonly RenderFrameResult _result;
        private readonly byte[] _fillColor;

        public FakeRgbaFallbackRenderer(RenderFrameResult result, byte[]? fillColor = null)
        {
            _result = result;
            _fillColor = fillColor ?? new byte[] { 0, 255, 0, 255 };
        }

        public int CallCount { get; private set; }

        public RenderFrameResult RenderToRgba(Span<byte> destination, int width, int height, int stride)
        {
            CallCount++;
            for (int y = 0; y < height; y++)
            {
                Span<byte> row = destination.Slice(y * stride, stride);
                for (int x = 0; x < width; x++)
                {
                    int offset = x * 4;
                    row[offset + 0] = _fillColor[0];
                    row[offset + 1] = _fillColor[1];
                    row[offset + 2] = _fillColor[2];
                    row[offset + 3] = _fillColor[3];
                }
            }

            return _result;
        }
    }
}
