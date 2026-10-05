// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Diagnostics;
using RoyalTerminal.Avalonia.Rendering;
using SkiaSharp;

internal static class ImageRowDamageBenchmark
{
    // Same stationary fractional image scene, one changed row versus forced full
    // repaint. Software Skia only; no claim about GPU or input-to-frame latency.
    internal static void Run()
    {
        foreach (int height in new[] { 24, 240, 2400 })
        {
            TerminalScreen screen = new(20, height, 10);
            screen.AddRow();
            screen.ScrollOffset = 1;
            screen.RenderScrollFraction = 0.5;
            screen.ReplaceKittyGraphics([new(1, 1, 1, [255, 0, 0, 128])],
                [new(1, TerminalKittyImageLayer.AboveText, 0, 0, 0, 0, 200, (height + 1) * 10, 0, 0, 1, 1)]);
            using SkiaTerminalRenderer renderer = new("Consolas", 14) { CursorVisible = false, EnableImageRenderDiagnostics = true };
            renderer.SetCellSize(10, 10);
            using SKBitmap bitmap = new(200, height * 10);
            using SKCanvas canvas = new(bitmap);
            using SKPaint clear = new() { Color = new SKColor(screen.DefaultBackground), BlendMode = SKBlendMode.Src };
            canvas.Clear(clear.Color);
            renderer.RenderFull(canvas, screen);
            foreach (bool full in new[] { false, true })
            {
                void Frame()
                {
                    screen.GetViewportRow(height / 2).IsDirty = true;
                    renderer.PrepareImageDamage(screen);
                    if (full) canvas.Clear(clear.Color);
                    else
                    {
                        canvas.Save();
                        canvas.Translate(0, -5);
                        canvas.DrawRect(0, height / 2 * 10, 200, 10, clear);
                        canvas.Restore();
                    }
                    renderer.Render(canvas, screen, forceFullRedraw: full);
                }
                for (int index = 0; index < 20; index++) Frame();
                int iterations = height == 24 ? 500 : height == 240 ? 100 : 20;
                double[] elapsed = new double[7];
                long[] allocations = new long[7];
                renderer.ResetImageRenderDiagnostics();
                for (int sample = 0; sample < elapsed.Length; sample++)
                {
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    long start = Stopwatch.GetTimestamp();
                    for (int index = 0; index < iterations; index++) Frame();
                    elapsed[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
                }
                Array.Sort(elapsed);
                Array.Sort(allocations);
                Console.WriteLine($"image-row-damage/{height}/full={full}: n={iterations}; median={elapsed[3]:F3} ms; allocated={allocations[3]} bytes; draws={renderer.GetImageRenderDiagnostics().Draws}");
            }
        }
    }
}
