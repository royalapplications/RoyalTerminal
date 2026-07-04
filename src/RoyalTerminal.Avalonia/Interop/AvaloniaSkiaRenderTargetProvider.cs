// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.Rendering.GhosttyInterop - Avalonia to render-target descriptor adapter.

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Metal;
using Avalonia.OpenGL;
using Avalonia.Platform;
using Avalonia.Skia;
using Avalonia.Vulkan;
using RoyalTerminal.Rendering.Contracts;
using RoyalTerminal.Avalonia.Interop;
using SkiaSharp;

namespace RoyalTerminal.Avalonia.Interop;

/// <summary>
/// Creates <see cref="RenderTargetDescriptor"/> values from Avalonia Skia render leases.
/// </summary>
public sealed class AvaloniaSkiaRenderTargetProvider : IAvaloniaSkiaRenderTargetProvider
{
    private readonly IAvaloniaMetalTextureHandleProvider _metalTextureHandleProvider;
    private readonly IAvaloniaVulkanTextureHandleProvider _vulkanTextureHandleProvider;
    private readonly IAvaloniaD3D11TextureHandleProvider _d3d11TextureHandleProvider;
    private readonly IAvaloniaD3D12TextureHandleProvider _d3d12TextureHandleProvider;
    private readonly IAvaloniaOpenGlRenderTargetHandleProvider _openGlRenderTargetHandleProvider;
    private string? _lastDiagnostic;

    private nint _sharedTextureHandle = nint.Zero;
    private nint _sharedTextureDevice = nint.Zero;
    private int _sharedTextureWidth = 0;
    private int _sharedTextureHeight = 0;

    [DllImport("libswift_terminal_renderer", EntryPoint = "swift_render_create_texture")]
    private static extern nint CreateSharedTextureNative(nint deviceHandle, int width, int height);

    [DllImport("libswift_terminal_renderer", EntryPoint = "swift_render_free_texture")]
    private static extern void FreeSharedTextureNative(nint textureHandle);

    private nint GetOrCreateSharedTexture(nint deviceHandle, int width, int height)
    {
        if (_sharedTextureHandle != nint.Zero &&
            _sharedTextureDevice == deviceHandle &&
            _sharedTextureWidth == width &&
            _sharedTextureHeight == height)
        {
            return _sharedTextureHandle;
        }

        CleanupSharedTexture();

        try
        {
            nint newTex = CreateSharedTextureNative(deviceHandle, width, height);
            if (newTex != nint.Zero)
            {
                _sharedTextureHandle = newTex;
                _sharedTextureDevice = deviceHandle;
                _sharedTextureWidth = width;
                _sharedTextureHeight = height;
            }
            return newTex;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Renderer Error] Failed to allocate shared Metal texture: {ex.Message}");
            return nint.Zero;
        }
    }

    private void CleanupSharedTexture()
    {
        if (_sharedTextureHandle != nint.Zero)
        {
            try
            {
                FreeSharedTextureNative(_sharedTextureHandle);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Renderer Error] Failed to free shared Metal texture: {ex.Message}");
            }
            _sharedTextureHandle = nint.Zero;
            _sharedTextureDevice = nint.Zero;
            _sharedTextureWidth = 0;
            _sharedTextureHeight = 0;
        }
    }

    ~AvaloniaSkiaRenderTargetProvider()
    {
        CleanupSharedTexture();
    }

    private static readonly RenderBackendKind[] MacBackendCandidates = [RenderBackendKind.Metal];
    private static readonly RenderBackendKind[] LinuxBackendCandidates = [RenderBackendKind.Vulkan];
    private static readonly RenderBackendKind[] WindowsBackendCandidates = [RenderBackendKind.D3D11, RenderBackendKind.D3D12];
    private static readonly RenderBackendKind[] OpenGlBackendCandidates = [RenderBackendKind.OpenGL];
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
    /// <param name="openGlRenderTargetHandleProvider">
    /// Optional OpenGL framebuffer/context resolver. When unavailable, requests fall back to CPU RGBA rendering.
    /// </param>
    public AvaloniaSkiaRenderTargetProvider(
        IAvaloniaMetalTextureHandleProvider? metalTextureHandleProvider = null,
        IAvaloniaVulkanTextureHandleProvider? vulkanTextureHandleProvider = null,
        IAvaloniaD3D11TextureHandleProvider? d3d11TextureHandleProvider = null,
        IAvaloniaD3D12TextureHandleProvider? d3d12TextureHandleProvider = null,
        AvaloniaRenderBackendPreference backendPreference = AvaloniaRenderBackendPreference.Auto,
        IAvaloniaOpenGlRenderTargetHandleProvider? openGlRenderTargetHandleProvider = null)
    {
        _metalTextureHandleProvider = metalTextureHandleProvider ?? DefaultAvaloniaMetalTextureHandleProvider.Instance;
        _vulkanTextureHandleProvider = vulkanTextureHandleProvider ?? DefaultAvaloniaVulkanTextureHandleProvider.Instance;
        _d3d11TextureHandleProvider = d3d11TextureHandleProvider ?? DefaultAvaloniaD3D11TextureHandleProvider.Instance;
        _d3d12TextureHandleProvider = d3d12TextureHandleProvider ?? DefaultAvaloniaD3D12TextureHandleProvider.Instance;
        _openGlRenderTargetHandleProvider = openGlRenderTargetHandleProvider ?? DefaultAvaloniaOpenGlRenderTargetHandleProvider.Instance;
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

        var scale = RoyalTerminal.Avalonia.Rendering.TerminalDrawHandler.GetCanvasScale(lease.SkCanvas.TotalMatrix);
        float destWidth = width / scale.X;
        float destHeight = height / scale.Y;

        using ISkiaSharpPlatformGraphicsApiLease? platformLease = lease.TryLeasePlatformGraphicsApi();
        if (platformLease?.Context is null)
        {
            ReportDiagnostic(
                "Avalonia platform graphics context is unavailable for texture interop. Falling back to software RGBA rendering.");
            return CreateSoftwareFallbackRequest(width, height, destWidth, destHeight, "avalonia-skiacanvas-cpu-fallback-no-context");
        }

        if (platformLease is not null)
        {
            AvaloniaInteropHandleExtraction.DumpObjectMembers(platformLease, "PlatformLease");
        }

        IPlatformGraphicsContext context = platformLease!.Context;
        RenderBackendKind[] backendCandidates = GetBackendCandidates(BackendPreference, context, out string? noCandidateReason);
        if (backendCandidates.Length == 0)
        {
            ReportDiagnostic(
                $"{noCandidateReason ?? "No GPU interop backend candidate is selected."} Falling back to software RGBA rendering.");
            return CreateSoftwareFallbackRequest(width, height, destWidth, destHeight, "avalonia-skiacanvas-cpu-fallback");
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
                DestinationRect = new SKRect(0, 0, destWidth, destHeight),
            };
        }

        string finalDiagnostic = firstFailureReason is not null
            ? $"{firstFailureReason} Falling back to software RGBA rendering."
            : $"No compatible interop render target resolved for backend preference '{BackendPreference}'. Falling back to software RGBA rendering.";
        ReportDiagnostic(finalDiagnostic);
        return CreateSoftwareFallbackRequest(width, height, destWidth, destHeight, "avalonia-skiacanvas-cpu-fallback-no-compatible-backend");
    }

    private static SkiaInteropRenderRequest CreateSoftwareFallbackRequest(
        int width,
        int height,
        float destWidth,
        float destHeight,
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
            DestinationRect = new SKRect(0, 0, destWidth, destHeight),
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
            RenderBackendKind.OpenGL => TryCreateOpenGlFramebufferDescriptor(lease, context, width, height, out descriptor),
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
        bool hasHandles = _metalTextureHandleProvider.TryGetHandles(
            lease,
            context,
            out nint deviceHandle,
            out nint commandQueueHandle,
            out nint textureHandle);

        if (hasHandles && deviceHandle != nint.Zero && commandQueueHandle != nint.Zero && textureHandle != nint.Zero)
        {
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

        if (deviceHandle != nint.Zero && commandQueueHandle != nint.Zero)
        {
            nint sharedTexture = GetOrCreateSharedTexture(deviceHandle, width, height);
            if (sharedTexture != nint.Zero)
            {
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
                    TargetHandle = sharedTexture,
                    DebugName = "avalonia-skiacanvas-metal-shared",
                };
                return true;
            }
        }

        return false;
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

    private bool TryCreateOpenGlFramebufferDescriptor(
        ISkiaSharpApiLease lease,
        IPlatformGraphicsContext context,
        int width,
        int height,
        out RenderTargetDescriptor descriptor)
    {
        descriptor = default;
        if (!_openGlRenderTargetHandleProvider.TryGetHandles(
                lease,
                context,
                out nint contextHandle,
                out nint framebufferHandle) ||
            contextHandle == nint.Zero)
        {
            return false;
        }

        descriptor = new()
        {
            BackendKind = RenderBackendKind.OpenGL,
            TargetKind = RenderTargetKind.Framebuffer,
            PixelFormat = RenderPixelFormat.Unknown,
            Width = width,
            Height = height,
            SampleCount = 1,
            ContextHandle = contextHandle,
            TargetHandle = framebufferHandle,
            DebugName = "avalonia-skiacanvas-opengl",
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
            return OpenGlBackendCandidates;
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

            RenderBackendKind.OpenGL when ReferenceEquals(
                _openGlRenderTargetHandleProvider,
                NullAvaloniaOpenGlRenderTargetHandleProvider.Instance)
                => "OpenGL interop handle provider is not configured.",

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

            case AvaloniaRenderBackendPreference.OpenGL:
                backend = RenderBackendKind.OpenGL;
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
            RenderBackendKind.OpenGL =>
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
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
            RenderBackendKind.OpenGL => true,
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
