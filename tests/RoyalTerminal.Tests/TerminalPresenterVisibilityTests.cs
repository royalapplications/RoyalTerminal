// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Shaders;
using RoyalTerminal.Terminal;
using SkiaSharp;
using Xunit;

namespace RoyalTerminal.Tests;

// Ghostty c4e16970a: release hidden-surface resources, lazily rebuild on draw.
// xterm RenderService pauses hidden refreshes; WT Renderer.EnablePainting forces
// viewport resynchronization and a full redraw. No shared GPU/font cache purge.
public sealed class TerminalPresenterVisibilityTests
{
    [AvaloniaTheory]
    [InlineData("self")]
    [InlineData("ancestor")]
    [InlineData("opacity")]
    [InlineData("minimize")]
    [InlineData("window")]
    public void HiddenPresenterReleasesResourcesAndResumesWithLatestState(string kind)
    {
        using SkiaTerminalRenderer renderer = new("monospace", 14f) { CursorVisible = false };
        TerminalScreen screen = new(20, 4);
        using BasicVtProcessor processor = new(screen);
        processor.Process("\u001b[41m\u001b[2J"u8);
        TerminalPresenter presenter = new();
        presenter.SetRenderState(renderer, screen);
        presenter.SetShaderState([Shader("half4(1, 0, 0, 1)")], true);
        Border parent = new() { Child = presenter };
        Window window = new() { Width = 240, Height = 100, Content = parent };
        try
        {
            window.Show();
            Assert.Equal(SKColors.Red, Pixel(window));
            TerminalDrawHandler handler = Assert.IsType<TerminalDrawHandler>(presenter.DrawHandler);
            Assert.True(handler.RetainedFrameBytes > 0);
            Assert.True(handler.HasCompiledShaders);
            SetHidden(true);
            Pump();
            Assert.Equal(0, handler.RetainedFrameBytes);
            Assert.False(handler.HasCompiledShaders);
            Assert.False(handler.IsRenderPending);

            processor.Process("\u001b[42m\u001b[2J"u8);
            presenter.NotifyResize(new Size(300, 120));
            presenter.Invalidate(fullRedraw: true);
            presenter.SetShaderState([Shader("half4(0, 1, 0, 1)")], true);
            presenter.SendUpdate();
            Pump();
            Assert.Equal(0, handler.RetainedFrameBytes);
            Assert.False(handler.HasCompiledShaders);
            Assert.False(handler.IsRenderPending);

            SetHidden(false);
            Assert.Equal(SKColors.Lime, Pixel(window));
            Assert.True(handler.RetainedFrameBytes > 0);
            Assert.True(handler.HasCompiledShaders);
            Assert.True(handler.IsRenderPending);
        }
        finally
        {
            window.Close();
            Pump();
        }

        void SetHidden(bool hidden)
        {
            switch (kind)
            {
                case "self": presenter.IsVisible = !hidden; break;
                case "ancestor": parent.IsVisible = !hidden; break;
                case "opacity": parent.Opacity = hidden ? 0 : 1; break;
                case "minimize": window.WindowState = hidden ? WindowState.Minimized : WindowState.Normal; break;
                case "window": if (hidden) window.Hide(); else window.Show(); break;
            }
        }
    }

    [AvaloniaFact]
    public void InitiallyHiddenPresenterDoesNotCompileShadersOrAllocateFrame()
    {
        using SkiaTerminalRenderer renderer = new("monospace", 14f) { CursorVisible = false };
        TerminalPresenter presenter = new();
        presenter.SetRenderState(renderer, new TerminalScreen(20, 4));
        List<TerminalShaderSource> sources = [Shader("half4(0, 1, 0, 1)")];
        presenter.SetShaderState(sources, true);
        sources.Clear(); // The posted configuration owns the immutable sources.
        Border parent = new() { Child = presenter, IsVisible = false };
        Window window = new() { Width = 240, Height = 100, Content = parent };
        try
        {
            window.Show();
            Pump();
            TerminalDrawHandler handler = Assert.IsType<TerminalDrawHandler>(presenter.DrawHandler);
            Assert.Equal(0, handler.RetainedFrameBytes);
            Assert.False(handler.HasCompiledShaders);
            Assert.False(handler.IsRenderPending);
            parent.IsVisible = true;
            Assert.Equal(SKColors.Lime, Pixel(window));
            Assert.True(handler.HasCompiledShaders);
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaFact]
    public void ResumeRedrawsCleanRowsAndNewSizeWithoutShaders()
    {
        using SkiaTerminalRenderer renderer = new("monospace", 14f) { CursorVisible = false };
        TerminalScreen screen = new(20, 4);
        using BasicVtProcessor processor = new(screen);
        processor.Process("\u001b[41m\u001b[2J"u8);
        TerminalPresenter presenter = new();
        presenter.SetRenderState(renderer, screen);
        Window window = new() { Width = 240, Height = 100, Content = presenter };
        try
        {
            window.Show();
            SKColor before = Pixel(window);
            presenter.IsVisible = false;
            Pump();
            processor.Process("\u001b[44m\u001b[2J"u8);
            window.Width = 320;
            for (int row = 0; row < screen.ViewportRows; row++) screen.GetViewportRow(row).IsDirty = false;
            presenter.IsVisible = true;
            SKColor after = Pixel(window);
            Assert.NotEqual(before, after);
            Assert.True(after.Blue > after.Red);
            Assert.Equal((long)Math.Ceiling(presenter.Bounds.Width) * (long)Math.Ceiling(presenter.Bounds.Height) * 4,
                presenter.DrawHandler!.RetainedFrameBytes);
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaFact]
    public void DetachAndReparentDropOldObserversAndIgnoreLateMessages()
    {
        using SkiaTerminalRenderer renderer = new("monospace", 14f) { CursorVisible = false };
        TerminalScreen screen = new(20, 4);
        TerminalPresenter presenter = new();
        presenter.SetRenderState(renderer, screen);
        presenter.SetShaderState([Shader("half4(0, 1, 0, 1)")], true);
        Border oldParent = new() { Child = presenter };
        Border nextParent = new();
        Grid root = new();
        root.Children.Add(oldParent);
        root.Children.Add(nextParent);
        Window window = new() { Width = 240, Height = 100, Content = root };
        try
        {
            window.Show();
            _ = Pixel(window);
            TerminalDrawHandler oldHandler = presenter.DrawHandler!;
            oldParent.Child = null;
            Pump();
            Assert.Equal(0, oldHandler.RetainedFrameBytes);
            Assert.False(oldHandler.HasCompiledShaders);
            oldHandler.OnMessage(new TerminalDrawHandler.UpdateMessage(renderer, screen));
            oldHandler.OnMessage(new TerminalDrawHandler.VisibilityMessage(true));
            Assert.False(oldHandler.IsRenderPending);
            nextParent.Child = presenter;
            Assert.Equal(SKColors.Lime, Pixel(window));
            Assert.NotSame(oldHandler, presenter.DrawHandler);
            oldParent.IsVisible = false;
            Pump();
            Assert.True(presenter.DrawHandler!.RetainedFrameBytes > 0);
            nextParent.IsVisible = false;
            Pump();
            Assert.Equal(0, presenter.DrawHandler.RetainedFrameBytes);
        }
        finally { window.Close(); Pump(); }
    }

    [AvaloniaFact]
    public void HiddenSiblingFramesAreReleasedWithoutDisturbingVisiblePresenter()
    {
        using SkiaTerminalRenderer renderer = new("monospace", 14f) { CursorVisible = false };
        TerminalScreen screen = new(20, 4);
        Grid hiddenGroup = new(), root = new();
        TerminalPresenter[] hidden = new TerminalPresenter[20];
        for (int index = 0; index < hidden.Length; index++)
        {
            hidden[index] = new TerminalPresenter();
            hidden[index].SetRenderState(renderer, screen);
            hiddenGroup.Children.Add(hidden[index]);
        }
        TerminalPresenter visible = new();
        visible.SetRenderState(renderer, screen);
        root.Children.Add(hiddenGroup);
        root.Children.Add(visible);
        Window window = new() { Width = 960, Height = 600, Content = root };
        try
        {
            window.Show();
            _ = Pixel(window);
            long bytes = 0;
            foreach (TerminalPresenter presenter in hidden) bytes += presenter.DrawHandler!.RetainedFrameBytes;
            Assert.Equal(20L * 960 * 600 * 4, bytes);
            hiddenGroup.IsVisible = false;
            Pump();
            foreach (TerminalPresenter presenter in hidden) Assert.Equal(0, presenter.DrawHandler!.RetainedFrameBytes);
            Assert.Equal(960L * 600 * 4, visible.DrawHandler!.RetainedFrameBytes);
        }
        finally { window.Close(); Pump(); }
    }

    private static TerminalShaderSource Shader(string expression) =>
        new("visibility", $"half4 main(float2 p) {{ return {expression}; }}", requiresContinuousAnimation: true);

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
        Dispatcher.UIThread.RunJobs();
    }

    private static SKColor Pixel(Window window)
    {
        Pump();
        using Bitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        using MemoryStream stream = new();
        frame.Save(stream, PngBitmapEncoderOptions.Default);
        using SKBitmap bitmap = SKBitmap.Decode(stream.ToArray());
        return bitmap.GetPixel(1, 1);
    }
}
