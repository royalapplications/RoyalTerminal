// Licensed under the MIT License.
// RoyalTerminal.Avalonia.Rendering - Avalonia to render-target descriptor adapter.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Metal;
using Avalonia.OpenGL;
using Avalonia.Platform;
using Avalonia.Skia;
using Avalonia.Vulkan;
using RoyalTerminal.Rendering.Contracts;
using RoyalTerminal.Rendering.Interop.Skia;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Rendering.Interop;

/// <summary>
/// Creates <see cref="RenderTargetDescriptor"/> values from Avalonia Skia render leases.
/// </summary>
public sealed class AvaloniaSkiaRenderTargetProvider : IAvaloniaSkiaRenderTargetProvider
{
    private readonly IAvaloniaMetalTextureHandleProvider _metalTextureHandleProvider;
    private readonly IAvaloniaVulkanTextureHandleProvider _vulkanTextureHandleProvider;
    private readonly IAvaloniaD3D11TextureHandleProvider _d3d11TextureHandleProvider;
    private readonly IAvaloniaD3D12TextureHandleProvider _d3d12TextureHandleProvider;
    private string? _lastDiagnostic;

    private static readonly RenderBackendKind[] MacBackendCandidates = [RenderBackendKind.Metal];
    private static readonly RenderBackendKind[] LinuxBackendCandidates = [RenderBackendKind.Vulkan];
    private static readonly RenderBackendKind[] WindowsBackendCandidates = [RenderBackendKind.D3D11, RenderBackendKind.D3D12];
    private static readonly RenderBackendKind[] EmptyBackendCandidates = [];

    /// <summary>
    /// Initializes a new provider.
    /// </summary>
    /// <param name="metalTextureHandleProvider">
    /// Optional Metal texture resolver. When unavailable, requests fall back to CPU RGBA rendering.
    /// </param>
    /// <param name="vulkanTextureHandleProvider">
    /// Optional Vulkan texture resolver. When unavailable, requests fall back to CPU RGBA rendering.
    /// </param>
    /// <param name="d3d11TextureHandleProvider">
    /// Optional D3D11 texture resolver. When unavailable, requests fall back to CPU RGBA rendering.
    /// </param>
    /// <param name="d3d12TextureHandleProvider">
    /// Optional D3D12 texture resolver. When unavailable, requests fall back to CPU RGBA rendering.
    /// </param>
    /// <param name="backendPreference">Preferred backend selection behavior.</param>
    public AvaloniaSkiaRenderTargetProvider(
        IAvaloniaMetalTextureHandleProvider? metalTextureHandleProvider = null,
        IAvaloniaVulkanTextureHandleProvider? vulkanTextureHandleProvider = null,
        IAvaloniaD3D11TextureHandleProvider? d3d11TextureHandleProvider = null,
        IAvaloniaD3D12TextureHandleProvider? d3d12TextureHandleProvider = null,
        AvaloniaRenderBackendPreference backendPreference = AvaloniaRenderBackendPreference.Auto)
    {
        _metalTextureHandleProvider = metalTextureHandleProvider ?? NullAvaloniaMetalTextureHandleProvider.Instance;
        _vulkanTextureHandleProvider = vulkanTextureHandleProvider ?? NullAvaloniaVulkanTextureHandleProvider.Instance;
        _d3d11TextureHandleProvider = d3d11TextureHandleProvider ?? NullAvaloniaD3D11TextureHandleProvider.Instance;
        _d3d12TextureHandleProvider = d3d12TextureHandleProvider ?? NullAvaloniaD3D12TextureHandleProvider.Instance;
        BackendPreference = backendPreference;
    }

    /// <inheritdoc />
    public AvaloniaRenderBackendPreference BackendPreference { get; set; }

    /// <inheritdoc />
    public string? LastDiagnostic => _lastDiagnostic;

    /// <inheritdoc />
    public event EventHandler<string>? DiagnosticReported;

    /// <inheritdoc />
    public SkiaInteropRenderRequest CreateRenderRequest(ISkiaSharpApiLease lease, PixelSize pixelSize)
    {
        ArgumentNullException.ThrowIfNull(lease);

        int width = Math.Max(1, pixelSize.Width);
        int height = Math.Max(1, pixelSize.Height);

        using ISkiaSharpPlatformGraphicsApiLease? platformLease = lease.TryLeasePlatformGraphicsApi();
        if (platformLease?.Context is null)
        {
            ReportDiagnostic(
                "Avalonia platform graphics context is unavailable for texture interop. Falling back to software RGBA rendering.");
            return CreateSoftwareFallbackRequest(width, height, "avalonia-skiacanvas-cpu-fallback-no-context");
        }

        IPlatformGraphicsContext context = platformLease.Context;
        RenderBackendKind[] backendCandidates = GetBackendCandidates(BackendPreference, context, out string? noCandidateReason);
        if (backendCandidates.Length == 0)
        {
            ReportDiagnostic(
                $"{noCandidateReason ?? "No GPU interop backend candidate is selected."} Falling back to software RGBA rendering.");
            return CreateSoftwareFallbackRequest(width, height, "avalonia-skiacanvas-cpu-fallback");
        }

        string? firstFailureReason = null;
        for (int i = 0; i < backendCandidates.Length; i++)
        {
            RenderBackendKind backend = backendCandidates[i];
            if (!IsBackendSupportedOnCurrentHost(backend, context))
            {
                firstFailureReason ??=
                    $"Backend '{backend}' is not compatible with current Avalonia context '{context.GetType().FullName}' on host '{GetHostPlatformName()}'.";
                continue;
            }

            if (TryGetMissingHandleProviderDiagnostic(backend, out string? missingProviderDiagnostic))
            {
                firstFailureReason ??= missingProviderDiagnostic;
                continue;
            }

            if (!TryCreateBackendDescriptor(backend, lease, context, width, height, out RenderTargetDescriptor descriptor))
            {
                firstFailureReason ??=
                    $"Interop handles for backend '{backend}' are unavailable.";
                continue;
            }

            ClearDiagnostic();
            return new SkiaInteropRenderRequest
            {
                TargetDescriptor = descriptor,
                AllowCpuFallback = true,
                DestinationRect = new SKRect(0, 0, width, height),
            };
        }

        string finalDiagnostic = firstFailureReason is not null
            ? $"{firstFailureReason} Falling back to software RGBA rendering."
            : $"No compatible interop render target resolved for backend preference '{BackendPreference}'. Falling back to software RGBA rendering.";
        ReportDiagnostic(finalDiagnostic);
        return CreateSoftwareFallbackRequest(width, height, "avalonia-skiacanvas-cpu-fallback-no-compatible-backend");
    }

    private static SkiaInteropRenderRequest CreateSoftwareFallbackRequest(
        int width,
        int height,
        string debugName)
    {
        RenderTargetDescriptor fallbackDescriptor = new()
        {
            BackendKind = RenderBackendKind.Software,
            TargetKind = RenderTargetKind.Framebuffer,
            PixelFormat = RenderPixelFormat.Unknown,
            Width = width,
            Height = height,
            SampleCount = 1,
            TargetHandle = (nint)1,
            DebugName = debugName,
        };

        return new SkiaInteropRenderRequest
        {
            TargetDescriptor = fallbackDescriptor,
            AllowCpuFallback = true,
            DestinationRect = new SKRect(0, 0, width, height),
        };
    }

    private bool TryCreateBackendDescriptor(
        RenderBackendKind backend,
        ISkiaSharpApiLease lease,
        IPlatformGraphicsContext context,
        int width,
        int height,
        out RenderTargetDescriptor descriptor)
    {
        return backend switch
        {
            RenderBackendKind.Metal => TryCreateMetalTextureDescriptor(lease, context, width, height, out descriptor),
            RenderBackendKind.Vulkan => TryCreateVulkanTextureDescriptor(lease, context, width, height, out descriptor),
            RenderBackendKind.D3D11 => TryCreateD3D11TextureDescriptor(lease, context, width, height, out descriptor),
            RenderBackendKind.D3D12 => TryCreateD3D12TextureDescriptor(lease, context, width, height, out descriptor),
            _ => TryCreateUnsupportedDescriptor(out descriptor),
        };
    }

    private bool TryCreateMetalTextureDescriptor(
        ISkiaSharpApiLease lease,
        IPlatformGraphicsContext context,
        int width,
        int height,
        out RenderTargetDescriptor descriptor)
    {
        descriptor = default;
        if (!_metalTextureHandleProvider.TryGetHandles(
                lease,
                context,
                out nint deviceHandle,
                out nint commandQueueHandle,
                out nint textureHandle) ||
            deviceHandle == nint.Zero ||
            commandQueueHandle == nint.Zero ||
            textureHandle == nint.Zero)
        {
            return false;
        }

        descriptor = new()
        {
            BackendKind = RenderBackendKind.Metal,
            TargetKind = RenderTargetKind.Texture2D,
            PixelFormat = RenderPixelFormat.Bgra8Unorm,
            Width = width,
            Height = height,
            SampleCount = 1,
            DeviceHandle = deviceHandle,
            CommandQueueHandle = commandQueueHandle,
            TargetHandle = textureHandle,
            DebugName = "avalonia-skiacanvas-metal",
        };

        return true;
    }

    private bool TryCreateVulkanTextureDescriptor(
        ISkiaSharpApiLease lease,
        IPlatformGraphicsContext context,
        int width,
        int height,
        out RenderTargetDescriptor descriptor)
    {
        descriptor = default;
        if (!_vulkanTextureHandleProvider.TryGetHandles(
                lease,
                context,
                out nint deviceHandle,
                out nint commandQueueHandle,
                out nint textureHandle,
                out nint textureViewHandle) ||
            deviceHandle == nint.Zero ||
            commandQueueHandle == nint.Zero ||
            textureHandle == nint.Zero ||
            textureViewHandle == nint.Zero)
        {
            return false;
        }

        descriptor = new()
        {
            BackendKind = RenderBackendKind.Vulkan,
            TargetKind = RenderTargetKind.Texture2D,
            PixelFormat = RenderPixelFormat.Bgra8Unorm,
            Width = width,
            Height = height,
            SampleCount = 1,
            DeviceHandle = deviceHandle,
            CommandQueueHandle = commandQueueHandle,
            TargetHandle = textureHandle,
            TargetViewHandle = textureViewHandle,
            DebugName = "avalonia-skiacanvas-vulkan",
        };

        return true;
    }

    private bool TryCreateD3D11TextureDescriptor(
        ISkiaSharpApiLease lease,
        IPlatformGraphicsContext context,
        int width,
        int height,
        out RenderTargetDescriptor descriptor)
    {
        descriptor = default;
        if (!_d3d11TextureHandleProvider.TryGetHandles(
                lease,
                context,
                out nint deviceHandle,
                out nint textureHandle,
                out nint targetViewHandle) ||
            deviceHandle == nint.Zero ||
            textureHandle == nint.Zero ||
            targetViewHandle == nint.Zero)
        {
            return false;
        }

        descriptor = new()
        {
            BackendKind = RenderBackendKind.D3D11,
            TargetKind = RenderTargetKind.Texture2D,
            PixelFormat = RenderPixelFormat.Bgra8Unorm,
            Width = width,
            Height = height,
            SampleCount = 1,
            DeviceHandle = deviceHandle,
            TargetHandle = textureHandle,
            TargetViewHandle = targetViewHandle,
            DebugName = "avalonia-skiacanvas-d3d11",
        };

        return true;
    }

    private bool TryCreateD3D12TextureDescriptor(
        ISkiaSharpApiLease lease,
        IPlatformGraphicsContext context,
        int width,
        int height,
        out RenderTargetDescriptor descriptor)
    {
        descriptor = default;
        if (!_d3d12TextureHandleProvider.TryGetHandles(
                lease,
                context,
                out nint deviceHandle,
                out nint commandQueueHandle,
                out nint commandListHandle,
                out nint textureHandle,
                out nint targetViewHandle) ||
            deviceHandle == nint.Zero ||
            commandQueueHandle == nint.Zero ||
            commandListHandle == nint.Zero ||
            textureHandle == nint.Zero ||
            targetViewHandle == nint.Zero)
        {
            return false;
        }

        descriptor = new()
        {
            BackendKind = RenderBackendKind.D3D12,
            TargetKind = RenderTargetKind.Texture2D,
            PixelFormat = RenderPixelFormat.Bgra8Unorm,
            Width = width,
            Height = height,
            SampleCount = 1,
            DeviceHandle = deviceHandle,
            CommandQueueHandle = commandQueueHandle,
            CommandBufferHandle = commandListHandle,
            TargetHandle = textureHandle,
            TargetViewHandle = targetViewHandle,
            DebugName = "avalonia-skiacanvas-d3d12",
        };

        return true;
    }

    private static bool TryCreateUnsupportedDescriptor(out RenderTargetDescriptor descriptor)
    {
        descriptor = default;
        return false;
    }

    private static RenderBackendKind[] GetBackendCandidates(
        AvaloniaRenderBackendPreference preference,
        IPlatformGraphicsContext context,
        out string? noCandidateReason)
    {
        noCandidateReason = null;

        if (preference == AvaloniaRenderBackendPreference.Software)
        {
            noCandidateReason = "Backend preference is set to software.";
            return EmptyBackendCandidates;
        }

        if (TryMapPreferenceToBackend(preference, out RenderBackendKind preferredBackend))
        {
            return [preferredBackend];
        }

        if (context is IMetalDevice)
        {
            return MacBackendCandidates;
        }

        if (context is IVulkanPlatformGraphicsContext)
        {
            return LinuxBackendCandidates;
        }

        if (context is IGlContext)
        {
            noCandidateReason =
                "Avalonia is using an OpenGL graphics context; direct texture interop provider support is not implemented.";
            return EmptyBackendCandidates;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return MacBackendCandidates;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return LinuxBackendCandidates;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return WindowsBackendCandidates;
        }

        return EmptyBackendCandidates;
    }

    private bool TryGetMissingHandleProviderDiagnostic(RenderBackendKind backend, out string? diagnostic)
    {
        diagnostic = backend switch
        {
            RenderBackendKind.Metal when ReferenceEquals(
                _metalTextureHandleProvider,
                NullAvaloniaMetalTextureHandleProvider.Instance)
                => "Metal interop handle provider is not configured.",

            RenderBackendKind.Vulkan when ReferenceEquals(
                _vulkanTextureHandleProvider,
                NullAvaloniaVulkanTextureHandleProvider.Instance)
                => "Vulkan interop handle provider is not configured.",

            RenderBackendKind.D3D11 when ReferenceEquals(
                _d3d11TextureHandleProvider,
                NullAvaloniaD3D11TextureHandleProvider.Instance)
                => "D3D11 interop handle provider is not configured.",

            RenderBackendKind.D3D12 when ReferenceEquals(
                _d3d12TextureHandleProvider,
                NullAvaloniaD3D12TextureHandleProvider.Instance)
                => "D3D12 interop handle provider is not configured.",

            _ => null,
        };

        return diagnostic is not null;
    }

    private static bool TryMapPreferenceToBackend(
        AvaloniaRenderBackendPreference preference,
        out RenderBackendKind backend)
    {
        switch (preference)
        {
            case AvaloniaRenderBackendPreference.Metal:
                backend = RenderBackendKind.Metal;
                return true;

            case AvaloniaRenderBackendPreference.Vulkan:
                backend = RenderBackendKind.Vulkan;
                return true;

            case AvaloniaRenderBackendPreference.D3D11:
                backend = RenderBackendKind.D3D11;
                return true;

            case AvaloniaRenderBackendPreference.D3D12:
                backend = RenderBackendKind.D3D12;
                return true;

            default:
                backend = RenderBackendKind.Unknown;
                return false;
        }
    }

    private static bool IsBackendSupportedOnCurrentHost(
        RenderBackendKind backend,
        IPlatformGraphicsContext context)
    {
        bool hostSupportsBackend = backend switch
        {
            RenderBackendKind.Metal => RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
            RenderBackendKind.Vulkan => RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
            RenderBackendKind.D3D11 => RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            RenderBackendKind.D3D12 => RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            _ => false,
        };

        if (!hostSupportsBackend)
        {
            return false;
        }

        if (context is IMetalDevice)
        {
            return backend == RenderBackendKind.Metal;
        }

        if (context is IVulkanPlatformGraphicsContext)
        {
            return backend == RenderBackendKind.Vulkan;
        }

        if (context is IGlContext)
        {
            return backend == RenderBackendKind.OpenGL;
        }

        return backend switch
        {
            RenderBackendKind.Metal => true,
            RenderBackendKind.Vulkan => true,
            RenderBackendKind.D3D11 => true,
            RenderBackendKind.D3D12 => true,
            _ => false,
        };
    }

    private static string GetHostPlatformName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "macOS";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "Linux";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "Windows";
        }

        return "Unknown";
    }

    private void ReportDiagnostic(string message)
    {
        if (string.Equals(_lastDiagnostic, message, StringComparison.Ordinal))
        {
            return;
        }

        _lastDiagnostic = message;
        DiagnosticReported?.Invoke(this, message);
    }

    private void ClearDiagnostic()
    {
        _lastDiagnostic = null;
    }
}
