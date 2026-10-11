// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class OctoberGraphicsRegressionTests
{
    [Fact]
    public void UpstreamZlibPngFixtureDecodesIdenticallyWithWuffsAndManagedZlib()
    {
        byte[] compressed = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Kitty", "compressed-png.data"));
        byte[] command = Encoding.ASCII.GetBytes($"f=100,o=z,i=31;{Convert.ToBase64String(compressed)}");
        Assert.True(ManagedKittyGraphicsCommand.TryParse(command, 1024, out var parsed));
        Assert.True(ManagedKittyImageLoader.TryCreate(parsed, new SkiaKittyGraphicsPngDecoder(), 1024 * 1024, out var loader, out string error), error);
        Assert.True(loader.TryComplete(out var actual, out error), error);
        Assert.Equal((50, 76), (actual.Width, actual.Height));
        if (!GhosttyVtProcessor.IsAvailable()) return;
        GhosttySys.EnsureSkiaPngDecoderInstalled();
        using GhosttyTerminal native = new(80, 24);
        native.SetKittyImageStorageLimit(1024 * 1024);
        native.Write("\u001b_G"u8); native.Write(command); native.Write("\u001b\\"u8);
        Assert.True(native.TryGetKittyGraphics(out GhosttyKittyGraphics? graphics));
        Assert.True(graphics!.TryGetImage(31, out GhosttyKittyGraphicsImage image));
        Assert.Equal(image.CopyRgbaData(), actual.GetRgbaImage().Rgba);
    }

    [Theory]
    [InlineData(false, "\u001b[2K")]
    [InlineData(true, "\u001b[2K")]
    [InlineData(false, "\u001b[2J")]
    [InlineData(true, "\u001b[2J")]
    public void FullRowEraseRemovesVirtualPlaceholderProjection(bool native, string erase)
    {
        if (native && !GhosttyVtProcessor.IsAvailable()) return;
        TerminalScreen screen = new(8, 4);
        using IVtProcessor processor = native ? new GhosttyVtProcessor(screen) : new BasicVtProcessor(screen);
        processor.NotifyResize(8, 4, 80, 40);
        processor.Process("\u001b_Ga=T,f=32,i=42,U=1,s=1,v=1,c=1,r=1;/wAA/w==\u001b\\"u8);
        processor.Process(Encoding.UTF8.GetBytes("\u001b[38;5;42m\U0010eeee"));
        Assert.Single(screen.GetKittyPlacements().ToArray());
        processor.Process(Encoding.ASCII.GetBytes(erase));
        Assert.Empty(screen.GetKittyPlacements().ToArray());
        processor.Process("plain"u8);
        Assert.Empty(screen.GetKittyPlacements().ToArray());
        // Virtual storage survives erasure and can be projected by new placeholders.
        processor.Process(Encoding.UTF8.GetBytes("\r\U0010eeee"));
        Assert.Single(screen.GetKittyPlacements().ToArray());
    }
}
