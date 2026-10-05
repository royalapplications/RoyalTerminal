// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using Avalonia.Platform;
using Avalonia.Skia;
using RoyalTerminal.Avalonia.Rendering;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed partial class TerminalRenderSurfaceTests
{
    [Fact]
    public void RasterSurfaceReleaseIsIdempotentAndDoesNotDisposeBorrowedLease()
    {
        using SKSurface target = SKSurface.Create(new SKImageInfo(16, 16));
        using ApiLease lease = new(target);
        using TerminalRenderSurface surface = TerminalRenderSurface.Create(new(32, 24), lease)!;
        Assert.False(surface.IsGpuBacked);
        Assert.Equal(32 * 24 * 4, surface.ByteCount);
        surface.Dispose();
        surface.Dispose();
        Assert.Equal(0, surface.ByteCount);
        Assert.False(lease.Disposed);
        Assert.NotEqual(IntPtr.Zero, target.Handle);
    }

    [Theory]
    [InlineData("normal")]
    [InlineData("lost")]
    [InlineData("loss-on-enter")]
    [InlineData("retry")]
    [InlineData("no-platform-lease")]
    [InlineData("abandoned")]
    [InlineData("disposed")]
    public void GpuReleaseReentersOnlyItsBorrowedContextAndHandlesLoss(string scenario)
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("The native CGL fixture requires macOS.");
        using CglContext platform = new();
        using IDisposable current = platform.EnsureCurrent();
        using GRContext grContext = GRContext.CreateGl();
        Assert.NotNull(grContext);
        using SKSurface target = SKSurface.Create(grContext, false, new SKImageInfo(16, 16));
        using ApiLease lease = new(target, grContext, scenario == "no-platform-lease" ? null : platform);
        using TerminalRenderSurface surface = TerminalRenderSurface.Create(new(32, 24), lease)!;
        Assert.Equal(scenario != "no-platform-lease", surface.IsGpuBacked);
        surface.Surface.Canvas.Clear(SKColors.Red);
        surface.Surface.Canvas.Flush();
        current.Dispose();
        int enters = platform.Enters;
        nint before = CGLGetCurrentContext();

        switch (scenario)
        {
            case "lost": platform.Lost = true; break;
            case "loss-on-enter": platform.LoseOnEnter = true; break;
            case "abandoned": grContext.AbandonContext(); break;
            case "disposed":
                using (platform.EnsureCurrent()) grContext.Dispose();
                enters = platform.Enters;
                break;
            case "retry":
                platform.RefuseEnter = true;
                Assert.Throws<InvalidOperationException>(surface.Dispose);
                Assert.True(surface.ByteCount > 0);
                platform.RefuseEnter = false;
                break;
        }

        surface.Dispose();
        surface.Dispose();
        Assert.Equal(0, surface.ByteCount);
        Assert.Equal(before, CGLGetCurrentContext());
        Assert.False(platform.Disposed);
        Assert.False(lease.Disposed);
        if (scenario is "normal" or "retry")
        {
            Assert.Equal(enters + 1, platform.Enters);
            Assert.False(grContext.IsAbandoned);
            using (platform.EnsureCurrent())
            {
                // Another terminal's target and the shared context remain usable.
                target.Canvas.Clear(SKColors.Blue);
                using SKImage image = target.Snapshot();
                using SKBitmap pixels = SKBitmap.FromImage(image);
                Assert.Equal(SKColors.Blue, pixels.GetPixel(0, 0));
            }
        }
        else
        {
            Assert.Equal(enters, platform.Enters);
            if (scenario is "lost" or "loss-on-enter" or "abandoned") Assert.True(grContext.IsAbandoned);
        }

        // Dispose the fixture's remaining Skia objects under the live native
        // context; a simulated loss doesn't actually destroy that CGL context.
        platform.Lost = platform.LoseOnEnter = false;
        using (platform.EnsureCurrent())
        {
            target.Dispose();
            grContext.Dispose();
        }
    }

    private sealed class ApiLease(SKSurface target, GRContext? grContext = null,
        IPlatformGraphicsContext? platform = null) : ISkiaSharpApiLease
    {
        public bool Disposed { get; private set; }
        public SKCanvas SkCanvas => target.Canvas;
        public GRContext? GrContext => grContext;
        public SKSurface SkSurface => target;
        public double CurrentOpacity => 1;
        public ISkiaSharpPlatformGraphicsApiLease? TryLeasePlatformGraphicsApi() =>
            platform is null ? null : new PlatformLease(platform);
        public void Dispose() => Disposed = true;
    }

    private sealed class PlatformLease(IPlatformGraphicsContext context) : ISkiaSharpPlatformGraphicsApiLease
    {
        public IPlatformGraphicsContext Context => context;
        public void Dispose() { }
    }

    private sealed class CglContext : IPlatformGraphicsContext
    {
        private nint _context;
        public int Enters { get; private set; }
        public bool Lost { get; set; }
        public bool LoseOnEnter { get; set; }
        public bool RefuseEnter { get; set; }
        public bool Disposed => _context == 0;
        public bool IsLost => Lost || Disposed;

        public unsafe CglContext()
        {
            // kCGLPFAOpenGLProfile, kCGLOGLPVersion_3_2_Core, terminator.
            int* attributes = stackalloc int[] { 99, 0x3200, 0 };
            Assert.Equal(0, CGLChoosePixelFormat(attributes, out nint format, out _));
            try { Assert.Equal(0, CGLCreateContext(format, 0, out _context)); }
            finally { CGLDestroyPixelFormat(format); }
        }

        public IDisposable EnsureCurrent()
        {
            if (IsLost || LoseOnEnter) throw new PlatformGraphicsContextLostException();
            if (RefuseEnter) throw new InvalidOperationException("Transient fixture refusal");
            nint previous = CGLGetCurrentContext();
            Assert.Equal(0, CGLSetCurrentContext(_context));
            Enters++;
            return new Current(previous);
        }

        public object? TryGetFeature(Type type) => null;
        public void Dispose()
        {
            if (_context == 0) return;
            CGLDestroyContext(_context);
            _context = 0;
        }

        private sealed class Current(nint previous) : IDisposable
        {
            private bool _disposed;
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Assert.Equal(0, CGLSetCurrentContext(previous));
            }
        }
    }

    private const string OpenGl = "/System/Library/Frameworks/OpenGL.framework/OpenGL";
    [LibraryImport(OpenGl)] private static unsafe partial int CGLChoosePixelFormat(int* attributes, out nint format, out int count);
    [LibraryImport(OpenGl)] private static partial int CGLDestroyPixelFormat(nint format);
    [LibraryImport(OpenGl)] private static partial int CGLCreateContext(nint format, nint share, out nint context);
    [LibraryImport(OpenGl)] private static partial int CGLDestroyContext(nint context);
    [LibraryImport(OpenGl)] private static partial int CGLSetCurrentContext(nint context);
    [LibraryImport(OpenGl)] private static partial nint CGLGetCurrentContext();
}
