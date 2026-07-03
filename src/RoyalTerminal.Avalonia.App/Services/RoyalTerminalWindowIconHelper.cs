// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.App - In-memory RoyalTerminal window icon generation.

using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace RoyalTerminal.Avalonia.App.Services;

internal static class RoyalTerminalWindowIconHelper
{
    private const string RoyalTerminalLogoPathData = """
        M5.522 19.586c-1.176 0 -2.128 -0.953 -2.128 -2.128V6.579c0 -1.176 0.953 -2.128 2.128 -2.128h11.824c1.176 0 2.128 0.953 2.128 2.128v8.122c0.534 -0.358 1.013 -0.679 1.419 -0.951v-7.171c0 -1.959 -1.588 -3.547 -3.547 -3.547H5.522c-1.959 0 -3.547 1.588 -3.547 3.547v10.878c0 1.959 1.588 3.547 3.547 3.547h2.49c-0.124 -0.329 -0.291 -0.785 -0.52 -1.419h-1.969Z
        M5.091 11.827c0.139 0.148 0.327 0.222 0.516 0.222 0.175 0 0.35 -0.064 0.487 -0.194l2.306 -2.201c0.142 -0.134 0.222 -0.321 0.222 -0.516s-0.08 -0.382 -0.222 -0.516l-2.306 -2.201c-0.285 -0.27 -0.734 -0.256 -1.003 0.028 -0.27 0.285 -0.256 0.734 0.028 1.004l1.759 1.686 -1.759 1.686c-0.285 0.269 -0.298 0.718 -0.028 1.003Z
        M23.486 14.421c-0.006 -0.01 -0.017 -0.017 -0.026 -0.025 -0.023 -0.021 -0.055 -0.033 -0.092 -0.039 -0.01 -0.001 -0.014 -0.011 -0.026 -0.011 -0.006 0 -0.014 0.006 -0.02 0.007 -0.058 0.004 -0.124 0.02 -0.195 0.067 0 0 -0.922 0.618 -2.234 1.497 -0.433 0.291 -0.908 0.609 -1.407 0.944 -0.004 0.003 -0.008 0.005 -0.012 0.008 -1.302 0.874 -2.761 1.854 -4.043 2.717 -0.797 0.537 -1.525 1.028 -2.101 1.419 -0.556 0.377 -0.973 0.662 -1.175 0.803 -0.071 0.053 -0.094 0.203 0.043 0.203h8.294c0.208 0 0.384 -0.151 0.444 -0.293l2.567 -7.164c0.017 -0.053 0.005 -0.098 -0.018 -0.133Z
        M13.631 19.586c0.492 -0.33 1.02 -0.685 1.53 -1.029 0.372 -0.251 0.733 -0.494 1.06 -0.714s0.618 -0.418 0.852 -0.577c0.263 -0.179 0.452 -0.309 0.535 -0.368 0.028 -0.02 0.043 -0.032 0.046 -0.035 0.046 -0.065 0.04 -0.122 0.039 -0.198l-0.289 -0.572 -1.708 -3.377s-0.006 -0.016 -0.014 -0.033c-0.002 -0.005 -0.007 -0.013 -0.01 -0.019 -0.008 -0.016 -0.02 -0.034 -0.033 -0.052 -0.007 -0.009 -0.015 -0.017 -0.023 -0.025 -0.014 -0.015 -0.031 -0.026 -0.049 -0.036 -0.011 -0.006 -0.023 -0.01 -0.035 -0.012 -0.008 -0.002 -0.014 -0.008 -0.023 -0.008 -0.024 0 -0.049 0.007 -0.077 0.022 -0.027 0.016 -0.045 0.036 -0.059 0.058 -0.014 0.022 -0.026 0.046 -0.041 0.069l-1.781 3.649 -1.589 3.256 -0.129 0.264 -0.452 0.926c-0.022 0.054 -0.014 0.097 0.005 0.132 0.004 0.007 0.007 0.014 0.012 0.02 0.024 0.028 0.058 0.047 0.101 0.049 0.002 0 0.003 0.002 0.005 0.002 0.026 0 0.055 -0.006 0.083 -0.018 0 0 0.351 -0.235 0.877 -0.588 0.333 -0.223 0.736 -0.494 1.172 -0.787Z
        M11.975 17.215c0 -0.118 -0.214 -0.257 -0.214 -0.257l-0.394 -0.265 -3.429 -2.302s-0.125 -0.105 -0.206 -0.105c-0.089 0 -0.162 0.073 -0.162 0.163h0c0 0.036 0.012 0.069 0.027 0.1 0 0 0.009 0.025 0.026 0.071 0.033 0.092 0.097 0.271 0.183 0.511 0.086 0.24 0.194 0.541 0.315 0.88 0.121 0.339 0.256 0.714 0.395 1.102 0.209 0.582 0.429 1.193 0.629 1.749 0.093 0.258 0.179 0.497 0.261 0.724 0.036 0.099 0.073 0.202 0.106 0.292 0.053 0.147 0.102 0.282 0.146 0.403 0.045 0.124 0.085 0.233 0.118 0.322 0.065 0.177 0.105 0.281 0.11 0.287 0.017 0.05 0.066 0.068 0.117 0.073 0.006 0 0.007 0.008 0.013 0.008 0.028 0 0.057 -0.006 0.083 -0.018 0.019 -0.009 0.034 -0.015 0.052 -0.029s0.04 -0.037 0.072 -0.077l0.615 -1.26 1.042 -2.135s0.093 -0.149 0.093 -0.236Z
        M9.211 12.019h4.257c0.071 0 0.135 -0.021 0.2 -0.04 0.292 -0.088 0.51 -0.348 0.51 -0.669 0 -0.014 -0.007 -0.026 -0.008 -0.041 -0.022 -0.372 -0.324 -0.669 -0.701 -0.669h-4.257c-0.392 0 -0.709 0.318 -0.709 0.709s0.318 0.709 0.709 0.709Z
        """;

    private static readonly int[] IconSizes = [16, 24, 32, 48, 64, 128, 256];

    public static WindowIcon CreateWindowIcon()
    {
        return new WindowIcon(new MemoryStream(CreateIcoBytes(), writable: false));
    }

    public static byte[] CreateIcoBytes()
    {
        var frames = new List<byte[]>(IconSizes.Length);

        foreach (int size in IconSizes)
        {
            frames.Add(RenderLogoPngFrame(size));
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)frames.Count);

        int imageOffset = 6 + (frames.Count * 16);
        for (int i = 0; i < frames.Count; i++)
        {
            int size = IconSizes[i];
            byte[] frame = frames[i];

            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)frame.Length);
            writer.Write((uint)imageOffset);

            imageOffset += frame.Length;
        }

        foreach (byte[] frame in frames)
        {
            writer.Write(frame);
        }

        return stream.ToArray();
    }

    public static string CreateLogoSvg()
    {
        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
              <path fill="#ffffff" d="{RoyalTerminalLogoPathData}" />
            </svg>
            """;
    }

    private static byte[] RenderLogoPngFrame(int size)
    {
        StreamGeometry geometry = StreamGeometry.Parse(RoyalTerminalLogoPathData);
        Rect bounds = geometry.Bounds;
        double padding = Math.Max(1, size * 0.1);
        double scale = Math.Min((size - (padding * 2)) / bounds.Width, (size - (padding * 2)) / bounds.Height);
        double offsetX = ((size - (bounds.Width * scale)) / 2) - (bounds.X * scale);
        double offsetY = ((size - (bounds.Height * scale)) / 2) - (bounds.Y * scale);
        Matrix transform =
            Matrix.CreateScale(scale, scale) *
            Matrix.CreateTranslation(offsetX, offsetY);

        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (DrawingContext context = bitmap.CreateDrawingContext())
        using (context.PushTransform(transform))
        {
            context.DrawGeometry(Brushes.White, null, geometry);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return stream.ToArray();
    }
}
