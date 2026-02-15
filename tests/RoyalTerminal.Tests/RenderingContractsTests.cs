// Licensed under the MIT License.
// RoyalTerminal.Tests - Contract and invariant tests for rendering abstractions.

using RoyalTerminal.Rendering.Contracts;
using Xunit;

namespace RoyalTerminal.Tests;

public class RenderingContractsTests
{
    [Fact]
    public void RenderTargetDescriptorValidator_ValidMetalTexture_ReturnsValid()
    {
        RenderTargetDescriptor descriptor = new()
        {
            BackendKind = RenderBackendKind.Metal,
            TargetKind = RenderTargetKind.Texture2D,
            PixelFormat = RenderPixelFormat.Bgra8Unorm,
            Width = 1920,
            Height = 1080,
            SampleCount = 1,
            DeviceHandle = (nint)1,
            CommandQueueHandle = (nint)2,
            CommandBufferHandle = (nint)3,
            TargetHandle = (nint)4,
            FrameId = 42,
            DebugName = "main-swapchain",
        };

        RenderValidationResult result = RenderTargetDescriptorValidator.Validate(descriptor);

        Assert.True(result.IsValid, result.ErrorMessage);
    }

    [Fact]
    public void RenderTargetDescriptorValidator_ZeroDimensions_ReturnsInvalid()
    {
        RenderTargetDescriptor descriptor = new()
        {
            BackendKind = RenderBackendKind.Software,
            TargetKind = RenderTargetKind.Framebuffer,
            PixelFormat = RenderPixelFormat.Unknown,
            Width = 0,
            Height = 1080,
            SampleCount = 1,
            TargetHandle = (nint)123,
        };

        RenderValidationResult result = RenderTargetDescriptorValidator.Validate(descriptor);

        Assert.False(result.IsValid);
        Assert.Contains("width", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenderTargetDescriptorValidator_TextureWithoutFormat_ReturnsInvalid()
    {
        RenderTargetDescriptor descriptor = new()
        {
            BackendKind = RenderBackendKind.Software,
            TargetKind = RenderTargetKind.Texture2D,
            PixelFormat = RenderPixelFormat.Unknown,
            Width = 256,
            Height = 256,
            SampleCount = 1,
            TargetHandle = (nint)123,
        };

        RenderValidationResult result = RenderTargetDescriptorValidator.Validate(descriptor);

        Assert.False(result.IsValid);
        Assert.Contains("pixel format", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenderTargetDescriptorValidator_MetalWithoutDeviceHandle_ReturnsInvalid()
    {
        RenderTargetDescriptor descriptor = new()
        {
            BackendKind = RenderBackendKind.Metal,
            TargetKind = RenderTargetKind.Texture2D,
            PixelFormat = RenderPixelFormat.Bgra8Unorm,
            Width = 800,
            Height = 600,
            SampleCount = 1,
            TargetHandle = (nint)444,
        };

        RenderValidationResult result = RenderTargetDescriptorValidator.Validate(descriptor);

        Assert.False(result.IsValid);
        Assert.Contains("device", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenderTargetDescriptorValidator_VulkanWithoutQueueHandle_ReturnsInvalid()
    {
        RenderTargetDescriptor descriptor = new()
        {
            BackendKind = RenderBackendKind.Vulkan,
            TargetKind = RenderTargetKind.Texture2D,
            PixelFormat = RenderPixelFormat.Bgra8Unorm,
            Width = 800,
            Height = 600,
            SampleCount = 1,
            DeviceHandle = (nint)10,
            TargetHandle = (nint)11,
            TargetViewHandle = (nint)12,
        };

        RenderValidationResult result = RenderTargetDescriptorValidator.Validate(descriptor);

        Assert.False(result.IsValid);
        Assert.Contains("queue", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenderTargetDescriptorValidator_VulkanTextureWithoutViewHandle_ReturnsInvalid()
    {
        RenderTargetDescriptor descriptor = new()
        {
            BackendKind = RenderBackendKind.Vulkan,
            TargetKind = RenderTargetKind.Texture2D,
            PixelFormat = RenderPixelFormat.Bgra8Unorm,
            Width = 800,
            Height = 600,
            SampleCount = 1,
            DeviceHandle = (nint)10,
            CommandQueueHandle = (nint)11,
            TargetHandle = (nint)12,
        };

        RenderValidationResult result = RenderTargetDescriptorValidator.Validate(descriptor);

        Assert.False(result.IsValid);
        Assert.Contains("view", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenderTargetDescriptorValidator_D3D11TextureWithoutTargetView_ReturnsInvalid()
    {
        RenderTargetDescriptor descriptor = new()
        {
            BackendKind = RenderBackendKind.D3D11,
            TargetKind = RenderTargetKind.Texture2D,
            PixelFormat = RenderPixelFormat.Bgra8Unorm,
            Width = 800,
            Height = 600,
            SampleCount = 1,
            DeviceHandle = (nint)10,
            TargetHandle = (nint)11,
        };

        RenderValidationResult result = RenderTargetDescriptorValidator.Validate(descriptor);

        Assert.False(result.IsValid);
        Assert.Contains("view", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenderTargetDescriptorValidator_D3D12WithoutQueueOrCommandList_ReturnsInvalid()
    {
        RenderTargetDescriptor descriptor = new()
        {
            BackendKind = RenderBackendKind.D3D12,
            TargetKind = RenderTargetKind.Texture2D,
            PixelFormat = RenderPixelFormat.Bgra8Unorm,
            Width = 800,
            Height = 600,
            SampleCount = 1,
            DeviceHandle = (nint)10,
            TargetHandle = (nint)11,
            TargetViewHandle = (nint)12,
        };

        RenderValidationResult result = RenderTargetDescriptorValidator.Validate(descriptor);

        Assert.False(result.IsValid);
        Assert.Contains("queue", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenderTargetDescriptorValidator_D3D12WithoutCommandList_ReturnsInvalid()
    {
        RenderTargetDescriptor descriptor = new()
        {
            BackendKind = RenderBackendKind.D3D12,
            TargetKind = RenderTargetKind.Texture2D,
            PixelFormat = RenderPixelFormat.Bgra8Unorm,
            Width = 800,
            Height = 600,
            SampleCount = 1,
            DeviceHandle = (nint)10,
            CommandQueueHandle = (nint)11,
            TargetHandle = (nint)12,
            TargetViewHandle = (nint)13,
        };

        RenderValidationResult result = RenderTargetDescriptorValidator.Validate(descriptor);

        Assert.False(result.IsValid);
        Assert.Contains("command-list", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenderTargetDescriptorValidator_OpenGlWithoutContextHandle_ReturnsInvalid()
    {
        RenderTargetDescriptor descriptor = new()
        {
            BackendKind = RenderBackendKind.OpenGL,
            TargetKind = RenderTargetKind.Framebuffer,
            PixelFormat = RenderPixelFormat.Unknown,
            Width = 800,
            Height = 600,
            SampleCount = 1,
            TargetHandle = (nint)55,
        };

        RenderValidationResult result = RenderTargetDescriptorValidator.Validate(descriptor);

        Assert.False(result.IsValid);
        Assert.Contains("context", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenderTargetDescriptorValidator_OpenGlDefaultFramebuffer_AllowsZeroHandle()
    {
        RenderTargetDescriptor descriptor = new()
        {
            BackendKind = RenderBackendKind.OpenGL,
            TargetKind = RenderTargetKind.Framebuffer,
            PixelFormat = RenderPixelFormat.Unknown,
            Width = 640,
            Height = 480,
            SampleCount = 1,
            ContextHandle = (nint)88,
            TargetHandle = nint.Zero,
        };

        RenderValidationResult result = RenderTargetDescriptorValidator.Validate(descriptor);

        Assert.True(result.IsValid, result.ErrorMessage);
    }

    [Fact]
    public void RenderTargetDescriptorValidator_ThrowIfInvalid_ThrowsArgumentException()
    {
        RenderTargetDescriptor descriptor = new()
        {
            BackendKind = RenderBackendKind.Unknown,
            TargetKind = RenderTargetKind.Framebuffer,
            Width = 32,
            Height = 32,
            SampleCount = 1,
            TargetHandle = (nint)9,
        };

        Assert.Throws<ArgumentException>(() => RenderTargetDescriptorValidator.ThrowIfInvalid(descriptor));
    }

    [Fact]
    public void RenderBackendCapabilities_FeatureAndFormatQueries_Work()
    {
        RenderBackendCapabilities capabilities = new(
            backendKind: RenderBackendKind.Vulkan,
            featureFlags: RenderFeatureFlags.ExternalTextureTargets | RenderFeatureFlags.ExplicitFrameLifecycle,
            minSampleCount: 1,
            maxSampleCount: 4,
            supportedPixelFormats: new[] { RenderPixelFormat.Bgra8Unorm, RenderPixelFormat.Rgba16Float });

        Assert.True(capabilities.SupportsFeatures(RenderFeatureFlags.ExternalTextureTargets));
        Assert.False(capabilities.SupportsFeatures(RenderFeatureFlags.CpuRgbaFallback));
        Assert.True(capabilities.SupportsPixelFormat(RenderPixelFormat.Bgra8Unorm));
        Assert.False(capabilities.SupportsPixelFormat(RenderPixelFormat.Unknown));
    }

    [Fact]
    public void RenderBackendCapabilities_CopiesAndDeduplicatesFormats()
    {
        List<RenderPixelFormat> source = new()
        {
            RenderPixelFormat.Bgra8Unorm,
            RenderPixelFormat.Bgra8Unorm,
            RenderPixelFormat.Rgba16Float,
        };

        RenderBackendCapabilities capabilities = new(
            backendKind: RenderBackendKind.Metal,
            featureFlags: RenderFeatureFlags.ExternalTextureTargets,
            minSampleCount: 1,
            maxSampleCount: 4,
            supportedPixelFormats: source);

        source.Clear();

        Assert.Equal(2, capabilities.SupportedPixelFormats.Count);
        Assert.Contains(RenderPixelFormat.Bgra8Unorm, capabilities.SupportedPixelFormats);
        Assert.Contains(RenderPixelFormat.Rgba16Float, capabilities.SupportedPixelFormats);
    }

    [Fact]
    public void RenderBackendCapabilities_UnknownFormat_Throws()
    {
        List<RenderPixelFormat> source = new()
        {
            RenderPixelFormat.Unknown,
        };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = new RenderBackendCapabilities(
                backendKind: RenderBackendKind.Metal,
                featureFlags: RenderFeatureFlags.None,
                minSampleCount: 1,
                maxSampleCount: 1,
                supportedPixelFormats: source));
    }

    [Fact]
    public void RenderValidationResult_FactoryMethods_SetExpectedValues()
    {
        RenderValidationResult valid = RenderValidationResult.Valid();
        RenderValidationResult invalid = RenderValidationResult.Invalid("bad descriptor");

        Assert.True(valid.IsValid);
        Assert.Null(valid.ErrorMessage);
        Assert.False(invalid.IsValid);
        Assert.Equal("bad descriptor", invalid.ErrorMessage);
    }

    [Fact]
    public void RenderFrameResult_FactoryMethods_SetExpectedValues()
    {
        RenderFrameResult success = RenderFrameResult.Success(
            requiresRedraw: true,
            synchronizationToken: 123,
            gpuTimeNanoseconds: 456);
        RenderFrameResult failure = RenderFrameResult.Failure("gpu lost");

        Assert.True(success.Succeeded);
        Assert.True(success.RequiresRedraw);
        Assert.Equal((ulong)123, success.SynchronizationToken);
        Assert.Equal(456, success.GpuTimeNanoseconds);
        Assert.Null(success.ErrorMessage);

        Assert.False(failure.Succeeded);
        Assert.False(failure.RequiresRedraw);
        Assert.Equal((ulong)0, failure.SynchronizationToken);
        Assert.Equal(0, failure.GpuTimeNanoseconds);
        Assert.Equal("gpu lost", failure.ErrorMessage);
    }

    [Fact]
    public void FakeRenderBackend_UsesCommonDescriptorValidator()
    {
        FakeRenderBackend backend = new();

        RenderTargetDescriptor valid = new()
        {
            BackendKind = RenderBackendKind.Software,
            TargetKind = RenderTargetKind.Framebuffer,
            Width = 1280,
            Height = 720,
            SampleCount = 1,
            TargetHandle = (nint)1,
        };

        RenderTargetDescriptor invalid = valid with { Width = -1 };

        Assert.True(backend.ValidateTarget(valid).IsValid);
        Assert.False(backend.ValidateTarget(invalid).IsValid);
    }

    [Fact]
    public void FakeRenderSurface_RenderInvalidTarget_ReturnsFailure()
    {
        FakeRenderSurface surface = new();

        RenderTargetDescriptor descriptor = new()
        {
            BackendKind = RenderBackendKind.Metal,
            TargetKind = RenderTargetKind.Texture2D,
            PixelFormat = RenderPixelFormat.Bgra8Unorm,
            Width = 800,
            Height = 600,
            SampleCount = 1,
            TargetHandle = (nint)1,
            // Missing required device handle for Metal.
            DeviceHandle = nint.Zero,
        };

        RenderFrameResult result = surface.Render(descriptor);

        Assert.False(result.Succeeded);
        Assert.Contains("device", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeRenderBackend : IRenderBackend
    {
        public RenderBackendKind BackendKind => RenderBackendKind.Software;

        public RenderBackendCapabilities Capabilities { get; } = new(
            RenderBackendKind.Software,
            RenderFeatureFlags.CpuRgbaFallback,
            minSampleCount: 1,
            maxSampleCount: 1,
            supportedPixelFormats: new[] { RenderPixelFormat.Bgra8Unorm });

        public RenderValidationResult ValidateTarget(in RenderTargetDescriptor descriptor) =>
            RenderTargetDescriptorValidator.Validate(descriptor);
    }

    private sealed class FakeRenderSurface : IRenderSurface
    {
        private bool _disposed;

        public RenderBackendKind BackendKind => RenderBackendKind.Metal;

        public RenderBackendCapabilities Capabilities { get; } = new(
            RenderBackendKind.Metal,
            RenderFeatureFlags.ExternalTextureTargets | RenderFeatureFlags.ExplicitFrameLifecycle,
            minSampleCount: 1,
            maxSampleCount: 4,
            supportedPixelFormats: new[] { RenderPixelFormat.Bgra8Unorm, RenderPixelFormat.Bgra8Srgb });

        public int Width { get; private set; } = 1;
        public int Height { get; private set; } = 1;
        public double ScaleX { get; private set; } = 1;
        public double ScaleY { get; private set; } = 1;

        public void SetSize(int width, int height)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

            Width = width;
            Height = height;
        }

        public void SetScale(double scaleX, double scaleY)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (scaleX <= 0) throw new ArgumentOutOfRangeException(nameof(scaleX));
            if (scaleY <= 0) throw new ArgumentOutOfRangeException(nameof(scaleY));

            ScaleX = scaleX;
            ScaleY = scaleY;
        }

        public RenderValidationResult ValidateTarget(in RenderTargetDescriptor descriptor)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return RenderTargetDescriptorValidator.Validate(descriptor);
        }

        public RenderFrameResult Render(in RenderTargetDescriptor descriptor)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            RenderValidationResult validation = ValidateTarget(descriptor);
            return validation.IsValid
                ? RenderFrameResult.Success(requiresRedraw: false, synchronizationToken: descriptor.FrameId)
                : RenderFrameResult.Failure(validation.ErrorMessage ?? "Validation failed.");
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
