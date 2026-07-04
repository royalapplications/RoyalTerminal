// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Runtime.InteropServices;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Rendering.Contracts;
using RoyalTerminal.Rendering.Interop.Swift.Native;

namespace RoyalTerminal.Rendering.Interop.Swift;

/// <summary>
/// Implements <see cref="IRenderSurface"/> by calling the native Swift/Metal rendering engine.
/// </summary>
public sealed class SwiftRenderSurface : IRenderSurface
{
    private nint _contextHandle = nint.Zero;
    private nint _surfaceHandle = nint.Zero;
    private bool _disposed;

    // Deferred initialization state
    private int? _pendingWidth;
    private int? _pendingHeight;
    private double? _pendingScaleX;
    private double? _pendingScaleY;
    private bool? _pendingFocus;
    private (uint fg, uint bg, uint cursor)? _pendingTheme;
    private (string family, double size)? _pendingFont;
    private (double width, double height, double baseline)? _pendingCellSize;

    // Retained cell buffer to avoid allocation during render passes
    private SwiftTerminalCellNative[] _cellsBuffer = [];
    private int _cols;
    private int _rows;
    private int _cursorCol;
    private int _cursorRow;
    private byte _cursorVisible;
    private int _cursorStyle;

    public SwiftRenderSurface(RenderBackendKind backendKind)
    {
        SwiftRendererNativeLibraryLoader.Initialize();

        BackendKind = backendKind;
        Capabilities = new RenderBackendCapabilities(
            backendKind,
            RenderFeatureFlags.ExternalTextureTargets |
            RenderFeatureFlags.CpuRgbaFallback,
            minSampleCount: 1,
            maxSampleCount: 1,
            supportedPixelFormats: [RenderPixelFormat.Bgra8Unorm, RenderPixelFormat.Bgra8Srgb, RenderPixelFormat.Rgba8Unorm]
        );
    }

    /// <inheritdoc />
    public RenderBackendKind BackendKind { get; }

    /// <inheritdoc />
    public RenderBackendCapabilities Capabilities { get; }

    private void EnsureInitialized(nint deviceHandle)
    {
        if (_surfaceHandle != nint.Zero)
        {
            return;
        }

        _contextHandle = deviceHandle != nint.Zero
            ? SwiftRendererNative.ContextNewWithDevice(deviceHandle)
            : SwiftRendererNative.ContextNew();

        if (_contextHandle == nint.Zero)
        {
            throw new InvalidOperationException("Failed to initialize Swift Metal render context.");
        }

        _surfaceHandle = SwiftRendererNative.SurfaceNew(_contextHandle, (int)BackendKind);
        if (_surfaceHandle == nint.Zero)
        {
            SwiftRendererNative.ContextFree(_contextHandle);
            _contextHandle = nint.Zero;
            throw new InvalidOperationException("Failed to create Swift Metal render surface.");
        }

        // Apply deferred states
        if (_pendingWidth.HasValue && _pendingHeight.HasValue)
        {
            SwiftRendererNative.SurfaceSetSize(_surfaceHandle, _pendingWidth.Value, _pendingHeight.Value);
            _pendingWidth = null;
            _pendingHeight = null;
        }
        if (_pendingScaleX.HasValue && _pendingScaleY.HasValue)
        {
            SwiftRendererNative.SurfaceSetScale(_surfaceHandle, _pendingScaleX.Value, _pendingScaleY.Value);
            _pendingScaleX = null;
            _pendingScaleY = null;
        }
        if (_pendingFocus.HasValue)
        {
            SwiftRendererNative.SurfaceSetFocus(_surfaceHandle, _pendingFocus.Value ? (byte)1 : (byte)0);
            _pendingFocus = null;
        }
        if (_pendingTheme.HasValue)
        {
            var t = _pendingTheme.Value;
            SwiftRenderThemeNative nativeTheme = new()
            {
                DefaultForegroundOriginal = t.fg,
                DefaultBackgroundOriginal = t.bg,
                CursorOriginal = t.cursor
            };
            SwiftRendererNative.SurfaceSetTheme(_surfaceHandle, in nativeTheme);
            _pendingTheme = null;
        }
        if (_pendingFont.HasValue)
        {
            var f = _pendingFont.Value;
            SwiftRendererNative.SurfaceSetFont(_surfaceHandle, f.family, f.size);
            _pendingFont = null;
        }
        if (_pendingCellSize.HasValue)
        {
            var c = _pendingCellSize.Value;
            SwiftRendererNative.SurfaceSetCellSize(_surfaceHandle, c.width, c.height, c.baseline);
            _pendingCellSize = null;
        }
    }

    /// <summary>
    /// Captures the terminal screen cells and metrics to be drawn on the next render pass.
    /// </summary>
    public void UpdateScreenState(
        object screenObj,
        int cursorCol,
        int cursorRow,
        bool cursorVisible,
        int cursorStyle)
    {
        ThrowIfDisposed();

        if (screenObj is not TerminalScreen screen)
        {
            throw new ArgumentException("Screen object must be of type TerminalScreen.", nameof(screenObj));
        }

        int viewportRows = screen.ViewportRows;
        int columns = screen.Columns;

        int totalCells = viewportRows * columns;
        if (_cellsBuffer.Length < totalCells)
        {
            _cellsBuffer = new SwiftTerminalCellNative[totalCells];
        }

        _cols = columns;
        _rows = viewportRows;
        _cursorCol = cursorCol;
        _cursorRow = cursorRow;
        _cursorVisible = cursorVisible ? (byte)1 : (byte)0;
        _cursorStyle = cursorStyle;

        for (int r = 0; r < viewportRows; r++)
        {
            TerminalRow row = screen.GetViewportRow(r);
            ReadOnlySpan<TerminalCell> cells = row.ReadOnlyCells;

            for (int c = 0; c < columns && c < cells.Length; c++)
            {
                TerminalCell cell = cells[c];

                int codepoint = cell.Codepoint;
                // Handle grapheme string fallback codepoint
                if (codepoint == 0 && !string.IsNullOrEmpty(cell.Grapheme))
                {
                    codepoint = char.ConvertToUtf32(cell.Grapheme, 0);
                }

                int bufferIndex = r * columns + c;
                _cellsBuffer[bufferIndex] = new SwiftTerminalCellNative
                {
                    Codepoint = codepoint,
                    Foreground = cell.Foreground,
                    Background = cell.Background,
                    Attributes = (byte)cell.Attributes,
                    UnderlineStyle = (byte)cell.UnderlineStyle,
                    Decorations = (byte)cell.Decorations,
                    Width = (byte)cell.Width
                };
            }
        }
    }

    /// <summary>
    /// Renders the cached grid into a CPU RGBA destination buffer.
    /// </summary>
    public unsafe RenderFrameResult RenderToRgba(Span<byte> destination, int width, int height, int stride)
    {
        ThrowIfDisposed();
        EnsureInitialized(nint.Zero);

        if (destination.Length == 0)
        {
            throw new ArgumentException("Destination buffer must be non-empty.", nameof(destination));
        }

        fixed (byte* destinationPtr = destination)
        {
            fixed (SwiftTerminalCellNative* cellsPtr = _cellsBuffer)
            {
                int resultCode = SwiftRendererNative.SurfaceRenderToRgba(
                    _surfaceHandle,
                    (nint)destinationPtr,
                    (uint)destination.Length,
                    width,
                    height,
                    stride,
                    cellsPtr,
                    _cellsBuffer.Length,
                    _cols,
                    _rows,
                    _cursorCol,
                    _cursorRow,
                    _cursorVisible,
                    _cursorStyle
                );

                return resultCode == 0
                    ? RenderFrameResult.Success()
                    : RenderFrameResult.Failure($"Swift render fallback failed with code: {resultCode}");
            }
        }
    }

    /// <inheritdoc />
    public void SetSize(int width, int height)
    {
        ThrowIfDisposed();
        if (_surfaceHandle == nint.Zero)
        {
            _pendingWidth = width;
            _pendingHeight = height;
        }
        else
        {
            SwiftRendererNative.SurfaceSetSize(_surfaceHandle, width, height);
        }
    }

    /// <inheritdoc />
    public void SetScale(double scaleX, double scaleY)
    {
        ThrowIfDisposed();
        if (_surfaceHandle == nint.Zero)
        {
            _pendingScaleX = scaleX;
            _pendingScaleY = scaleY;
        }
        else
        {
            SwiftRendererNative.SurfaceSetScale(_surfaceHandle, scaleX, scaleY);
        }
    }

    /// <summary>
    /// Sets the focused state.
    /// </summary>
    public void SetFocus(bool focused)
    {
        ThrowIfDisposed();
        if (_surfaceHandle == nint.Zero)
        {
            _pendingFocus = focused;
        }
        else
        {
            SwiftRendererNative.SurfaceSetFocus(_surfaceHandle, focused ? (byte)1 : (byte)0);
        }
    }

    /// <summary>
    /// Sets the color theme properties.
    /// </summary>
    public void SetTheme(uint defaultFg, uint defaultBg, uint cursorColor)
    {
        ThrowIfDisposed();
        if (_surfaceHandle == nint.Zero)
        {
            _pendingTheme = (defaultFg, defaultBg, cursorColor);
        }
        else
        {
            SwiftRenderThemeNative nativeTheme = new()
            {
                DefaultForegroundOriginal = defaultFg,
                DefaultBackgroundOriginal = defaultBg,
                CursorOriginal = cursorColor
            };
            SwiftRendererNative.SurfaceSetTheme(_surfaceHandle, in nativeTheme);
        }
    }

    /// <summary>
    /// Sets the active font properties.
    /// </summary>
    public void SetFont(string fontFamily, double fontSize)
    {
        ThrowIfDisposed();
        if (_surfaceHandle == nint.Zero)
        {
            _pendingFont = (fontFamily, fontSize);
        }
        else
        {
            SwiftRendererNative.SurfaceSetFont(_surfaceHandle, fontFamily, fontSize);
        }
    }

    /// <summary>
    /// Sets the layout cell size and baseline.
    /// </summary>
    public void SetCellSize(double cellWidth, double cellHeight, double baseline)
    {
        ThrowIfDisposed();
        if (_surfaceHandle == nint.Zero)
        {
            _pendingCellSize = (cellWidth, cellHeight, baseline);
        }
        else
        {
            SwiftRendererNative.SurfaceSetCellSize(_surfaceHandle, cellWidth, cellHeight, baseline);
        }
    }

    /// <inheritdoc />
    public RenderValidationResult ValidateTarget(in RenderTargetDescriptor descriptor)
    {
        ThrowIfDisposed();
        return RenderValidationResult.Valid(); // Core targets validated dynamically by native pipeline
    }

    /// <inheritdoc />
    public unsafe RenderFrameResult Render(in RenderTargetDescriptor descriptor)
    {
        ThrowIfDisposed();
        EnsureInitialized(descriptor.DeviceHandle);

        SwiftRenderTargetDescriptorNative nativeDesc = new()
        {
            Backend = descriptor.BackendKind,
            TargetKind = descriptor.TargetKind,
            PixelFormat = descriptor.PixelFormat,
            Width = descriptor.Width,
            Height = descriptor.Height,
            SampleCount = descriptor.SampleCount,
            DeviceHandle = descriptor.DeviceHandle,
            ContextHandle = descriptor.ContextHandle,
            CommandQueueHandle = descriptor.CommandQueueHandle,
            CommandBufferHandle = descriptor.CommandBufferHandle,
            TargetHandle = descriptor.TargetHandle,
            TargetViewHandle = descriptor.TargetViewHandle,
            FrameId = descriptor.FrameId,
            DebugNameUtf8 = nint.Zero
        };

        fixed (SwiftTerminalCellNative* cellsPtr = _cellsBuffer)
        {
            int resultCode = SwiftRendererNative.SurfaceRenderToTarget(
                _surfaceHandle,
                in nativeDesc,
                cellsPtr,
                _cellsBuffer.Length,
                _cols,
                _rows,
                _cursorCol,
                _cursorRow,
                _cursorVisible,
                _cursorStyle
            );

            return resultCode == 0
                ? RenderFrameResult.Success(synchronizationToken: descriptor.FrameId)
                : RenderFrameResult.Failure($"Swift direct render failed with code: {resultCode}");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_surfaceHandle != nint.Zero)
        {
            SwiftRendererNative.SurfaceFree(_surfaceHandle);
        }
        if (_contextHandle != nint.Zero)
        {
            SwiftRendererNative.ContextFree(_contextHandle);
        }
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SwiftRenderSurface));
        }
    }
}
