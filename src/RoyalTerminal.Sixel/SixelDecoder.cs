// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Sixel - Managed sixel decoder.

namespace RoyalTerminal.Sixel;

/// <summary>
/// Decodes DCS sixel payloads into RGBA bitmaps.
/// </summary>
public sealed class SixelDecoder
{
    private const byte SixelFinalByte = (byte)'q';
    private const int SixelDataMin = 0x3F;
    private const int SixelDataMax = 0x7E;
    private readonly SixelDecoderOptions _options;

    /// <summary>
    /// Creates a sixel decoder with default resource limits.
    /// </summary>
    public SixelDecoder()
        : this(SixelDecoderOptions.Default)
    {
    }

    /// <summary>
    /// Creates a sixel decoder with explicit resource limits.
    /// </summary>
    public SixelDecoder(SixelDecoderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);
        _options = options;
    }

    /// <summary>
    /// Decodes a DCS payload, including its sixel introducer and final <c>q</c> byte.
    /// </summary>
    public SixelDecodeResult Decode(ReadOnlySpan<byte> payload)
    {
        try
        {
            return DecodeCore(payload);
        }
        catch (OverflowException)
        {
            return SixelDecodeResult.Failure(
                SixelDecodeStatus.InvalidData,
                "The sixel payload contains numeric values that exceed decoder limits.");
        }
    }

    private SixelDecodeResult DecodeCore(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return SixelDecodeResult.Failure(SixelDecodeStatus.EmptyInput, "The sixel payload is empty.");
        }

        if (payload.Length > _options.MaxInputBytes)
        {
            return SixelDecodeResult.Failure(SixelDecodeStatus.InputTooLarge, "The sixel payload exceeds the input limit.");
        }

        if (!TryParseSixelIntroducer(payload, out int dataIndex, out int backgroundMode))
        {
            return SixelDecodeResult.Failure(SixelDecodeStatus.MissingIntroducer, "The DCS payload is not a sixel payload.");
        }

        SixelColorTable colors = new(_options.MaxColorRegisters);
        bool transparentBackground = backgroundMode == 1 ||
            (backgroundMode < 0 && _options.DefaultTransparentBackground);
        uint background = transparentBackground ? 0x00000000u : colors.GetColor(0);
        SixelCanvas canvas = new(_options, background);
        int x = 0;
        int y = 0;
        uint currentColor = colors.GetColor(0);

        for (int i = dataIndex; i < payload.Length; i++)
        {
            byte command = payload[i];
            switch (command)
            {
                case (byte)'!':
                    i++;
                    int repeatCount = ParseUnsignedInteger(payload, ref i);
                    if (repeatCount <= 0)
                    {
                        repeatCount = 1;
                    }

                    if (i >= payload.Length || !IsSixelDataByte(payload[i]))
                    {
                        return SixelDecodeResult.Failure(SixelDecodeStatus.InvalidData, "The sixel repeat command is missing a data byte.");
                    }

                    if (!DrawSixelColumns(canvas, payload[i], repeatCount, ref x, y, currentColor, out SixelDecodeStatus repeatStatus))
                    {
                        return SixelDecodeResult.Failure(repeatStatus, "The repeated sixel data exceeded configured limits.");
                    }
                    break;

                case (byte)'#':
                    i++;
                    if (!HandleColorCommand(payload, ref i, colors, ref currentColor, out SixelDecodeStatus colorStatus))
                    {
                        return SixelDecodeResult.Failure(colorStatus, "The sixel color command exceeded configured limits.");
                    }
                    i--;
                    break;

                case (byte)'"':
                    i++;
                    if (!HandleRasterAttributes(payload, ref i, canvas, out SixelDecodeStatus rasterStatus))
                    {
                        return SixelDecodeResult.Failure(rasterStatus, "The sixel raster attributes exceeded configured limits.");
                    }
                    i--;
                    break;

                case (byte)'$':
                    x = 0;
                    break;

                case (byte)'-':
                    x = 0;
                    y = checked(y + 6);
                    if (!canvas.TryEnsureSize(Math.Max(1, canvas.Width), y + 1, out SixelDecodeStatus newlineStatus))
                    {
                        return SixelDecodeResult.Failure(newlineStatus, "The sixel newline exceeded configured limits.");
                    }
                    break;

                default:
                    if (IsSixelDataByte(command))
                    {
                        if (!DrawSixelColumns(canvas, command, 1, ref x, y, currentColor, out SixelDecodeStatus drawStatus))
                        {
                            return SixelDecodeResult.Failure(drawStatus, "The sixel data exceeded configured limits.");
                        }
                    }
                    break;
            }
        }

        if (canvas.Width <= 0 || canvas.Height <= 0)
        {
            return SixelDecodeResult.Failure(SixelDecodeStatus.EmptyInput, "The sixel payload did not draw any pixels.");
        }

        return SixelDecodeResult.FromImage(
            new SixelImage(canvas.Width, canvas.Height, canvas.ToRgbaBytes()),
            x,
            y);
    }

    private static void ValidateOptions(SixelDecoderOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxInputBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxHeight, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxPixels, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxColorRegisters, 1);
    }

    private static bool TryParseSixelIntroducer(ReadOnlySpan<byte> payload, out int dataIndex, out int backgroundMode)
    {
        dataIndex = 0;
        backgroundMode = -1;
        int i = 0;
        int parameterIndex = 0;
        int currentParameter = 0;
        bool hasParameter = false;

        while (i < payload.Length && IsDcsParameterByte(payload[i]))
        {
            byte b = payload[i];
            if (b >= (byte)'0' && b <= (byte)'9')
            {
                hasParameter = true;
                currentParameter = checked((currentParameter * 10) + (b - (byte)'0'));
            }
            else if (b == (byte)';')
            {
                if (parameterIndex == 1 && hasParameter)
                {
                    backgroundMode = currentParameter;
                }

                parameterIndex++;
                currentParameter = 0;
                hasParameter = false;
            }

            i++;
        }

        if (parameterIndex == 1 && hasParameter)
        {
            backgroundMode = currentParameter;
        }

        bool hasIntermediate = false;
        while (i < payload.Length && IsDcsIntermediateByte(payload[i]))
        {
            hasIntermediate = true;
            i++;
        }

        if (i >= payload.Length || payload[i] != SixelFinalByte || hasIntermediate)
        {
            return false;
        }

        dataIndex = i + 1;
        return true;
    }

    private static bool HandleColorCommand(
        ReadOnlySpan<byte> payload,
        ref int index,
        SixelColorTable colors,
        ref uint currentColor,
        out SixelDecodeStatus status)
    {
        Span<int> parameters = stackalloc int[5];
        Span<bool> present = stackalloc bool[5];
        int count = ParseParameterList(payload, ref index, parameters, present);
        status = SixelDecodeStatus.Success;

        if (count <= 0 || !present[0])
        {
            return true;
        }

        int register = Math.Max(0, parameters[0]);
        if (!colors.TrySelect(register, out currentColor))
        {
            status = SixelDecodeStatus.ColorRegisterLimitExceeded;
            return false;
        }

        if (count >= 5 && present[1])
        {
            int colorSpace = parameters[1];
            uint color = colorSpace == 1
                ? ConvertHlsToRgba(parameters[2], parameters[3], parameters[4])
                : ConvertRgbPercentToRgba(parameters[2], parameters[3], parameters[4]);

            if (!colors.TrySet(register, color))
            {
                status = SixelDecodeStatus.ColorRegisterLimitExceeded;
                return false;
            }

            currentColor = color;
        }

        return true;
    }

    private static bool HandleRasterAttributes(
        ReadOnlySpan<byte> payload,
        ref int index,
        SixelCanvas canvas,
        out SixelDecodeStatus status)
    {
        Span<int> parameters = stackalloc int[4];
        Span<bool> present = stackalloc bool[4];
        int count = ParseParameterList(payload, ref index, parameters, present);

        int width = count >= 3 && present[2] ? parameters[2] : 0;
        int height = count >= 4 && present[3] ? parameters[3] : 0;
        if (width > 0 || height > 0)
        {
            int requestedWidth = Math.Max(width, Math.Max(1, canvas.Width));
            int requestedHeight = Math.Max(height, Math.Max(1, canvas.Height));
            return canvas.TryEnsureSize(requestedWidth, requestedHeight, out status);
        }

        status = SixelDecodeStatus.Success;
        return true;
    }

    private static bool DrawSixelColumns(
        SixelCanvas canvas,
        byte sixel,
        int repeatCount,
        ref int x,
        int y,
        uint color,
        out SixelDecodeStatus status)
    {
        int bits = sixel - SixelDataMin;
        int requestedWidth = checked(x + repeatCount);
        int requestedHeight = checked(y + 6);
        if (!canvas.TryEnsureSize(requestedWidth, requestedHeight, out status))
        {
            return false;
        }

        for (int repeat = 0; repeat < repeatCount; repeat++)
        {
            for (int bit = 0; bit < 6; bit++)
            {
                if ((bits & (1 << bit)) != 0)
                {
                    canvas.SetPixel(x, y + bit, color);
                }
            }

            x++;
        }

        status = SixelDecodeStatus.Success;
        return true;
    }

    private static int ParseParameterList(
        ReadOnlySpan<byte> payload,
        ref int index,
        Span<int> parameters,
        Span<bool> present)
    {
        int count = 0;
        int value = 0;
        bool hasValue = false;

        while (index < payload.Length)
        {
            byte b = payload[index];
            if (b >= (byte)'0' && b <= (byte)'9')
            {
                hasValue = true;
                value = checked((value * 10) + (b - (byte)'0'));
                index++;
                continue;
            }

            if (b == (byte)';')
            {
                StoreParameter(parameters, present, count, value, hasValue);
                count++;
                value = 0;
                hasValue = false;
                index++;
                continue;
            }

            break;
        }

        StoreParameter(parameters, present, count, value, hasValue);
        return Math.Min(parameters.Length, count + 1);
    }

    private static void StoreParameter(Span<int> parameters, Span<bool> present, int index, int value, bool hasValue)
    {
        if ((uint)index >= (uint)parameters.Length)
        {
            return;
        }

        parameters[index] = value;
        present[index] = hasValue;
    }

    private static int ParseUnsignedInteger(ReadOnlySpan<byte> payload, ref int index)
    {
        int value = 0;
        while (index < payload.Length)
        {
            byte b = payload[index];
            if (b < (byte)'0' || b > (byte)'9')
            {
                break;
            }

            value = checked((value * 10) + (b - (byte)'0'));
            index++;
        }

        return value;
    }

    private static uint ConvertRgbPercentToRgba(int red, int green, int blue)
    {
        byte r = PercentToByte(red);
        byte g = PercentToByte(green);
        byte b = PercentToByte(blue);
        return PackRgba(r, g, b, 0xFF);
    }

    private static uint ConvertHlsToRgba(int hueDegrees, int lightnessPercent, int saturationPercent)
    {
        double h = ((hueDegrees % 360) + 360) % 360 / 360.0;
        double l = Math.Clamp(lightnessPercent, 0, 100) / 100.0;
        double s = Math.Clamp(saturationPercent, 0, 100) / 100.0;

        if (s <= double.Epsilon)
        {
            byte gray = UnitToByte(l);
            return PackRgba(gray, gray, gray, 0xFF);
        }

        double q = l < 0.5 ? l * (1.0 + s) : l + s - (l * s);
        double p = (2.0 * l) - q;
        byte r = UnitToByte(HueToRgb(p, q, h + (1.0 / 3.0)));
        byte g = UnitToByte(HueToRgb(p, q, h));
        byte b = UnitToByte(HueToRgb(p, q, h - (1.0 / 3.0)));
        return PackRgba(r, g, b, 0xFF);
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0.0)
        {
            t += 1.0;
        }
        else if (t > 1.0)
        {
            t -= 1.0;
        }

        if (t < 1.0 / 6.0)
        {
            return p + ((q - p) * 6.0 * t);
        }

        if (t < 1.0 / 2.0)
        {
            return q;
        }

        if (t < 2.0 / 3.0)
        {
            return p + ((q - p) * ((2.0 / 3.0) - t) * 6.0);
        }

        return p;
    }

    private static byte PercentToByte(int value)
        => UnitToByte(Math.Clamp(value, 0, 100) / 100.0);

    private static byte UnitToByte(double value)
        => (byte)Math.Clamp((int)Math.Round(value * 255.0, MidpointRounding.AwayFromZero), 0, 255);

    private static uint PackRgba(byte red, byte green, byte blue, byte alpha)
        => ((uint)red << 24) | ((uint)green << 16) | ((uint)blue << 8) | alpha;

    private static bool IsSixelDataByte(byte value) => value is >= SixelDataMin and <= SixelDataMax;

    private static bool IsDcsParameterByte(byte value) => value is >= 0x30 and <= 0x3F;

    private static bool IsDcsIntermediateByte(byte value) => value is >= 0x20 and <= 0x2F;

    private sealed class SixelColorTable
    {
        private readonly uint[] _colors;

        public SixelColorTable(int maxColorRegisters)
        {
            _colors = new uint[maxColorRegisters];
            InitializeDefaultColors(_colors);
        }

        public uint GetColor(int register)
        {
            if ((uint)register >= (uint)_colors.Length)
            {
                return _colors[0];
            }

            return _colors[register];
        }

        public bool TrySelect(int register, out uint color)
        {
            if ((uint)register >= (uint)_colors.Length)
            {
                color = 0;
                return false;
            }

            color = _colors[register];
            return true;
        }

        public bool TrySet(int register, uint color)
        {
            if ((uint)register >= (uint)_colors.Length)
            {
                return false;
            }

            _colors[register] = color;
            return true;
        }

        private static void InitializeDefaultColors(Span<uint> colors)
        {
            ReadOnlySpan<uint> defaults =
            [
                0x000000FFu, 0xCD0000FFu, 0x00CD00FFu, 0xCDCD00FFu,
                0x0000EEFFu, 0xCD00CDFFu, 0x00CDCDFFu, 0xE5E5E5FFu,
                0x7F7F7FFFu, 0xFF0000FFu, 0x00FF00FFu, 0xFFFF00FFu,
                0x5C5CFFFFu, 0xFF00FFFFu, 0x00FFFFFFu, 0xFFFFFFFFu,
            ];

            int count = Math.Min(colors.Length, defaults.Length);
            for (int i = 0; i < count; i++)
            {
                colors[i] = defaults[i];
            }

            for (int i = count; i < colors.Length; i++)
            {
                byte value = (byte)(i & 0xFF);
                colors[i] = PackRgba(value, value, value, 0xFF);
            }
        }
    }

    private sealed class SixelCanvas
    {
        private readonly SixelDecoderOptions _options;
        private readonly uint _background;
        private uint[] _pixels = [];
        private int _capacityWidth;
        private int _capacityHeight;

        public SixelCanvas(SixelDecoderOptions options, uint background)
        {
            _options = options;
            _background = background;
        }

        public int Width { get; private set; }

        public int Height { get; private set; }

        public bool TryEnsureSize(int width, int height, out SixelDecodeStatus status)
        {
            if (width <= 0 || height <= 0)
            {
                status = SixelDecodeStatus.Success;
                return true;
            }

            if (width > _options.MaxWidth || height > _options.MaxHeight)
            {
                status = SixelDecodeStatus.ImageTooLarge;
                return false;
            }

            if ((long)width * height > _options.MaxPixels)
            {
                status = SixelDecodeStatus.ImageTooLarge;
                return false;
            }

            if (width <= Width && height <= Height)
            {
                status = SixelDecodeStatus.Success;
                return true;
            }

            if (width <= _capacityWidth && height <= _capacityHeight)
            {
                Width = Math.Max(Width, width);
                Height = Math.Max(Height, height);
                status = SixelDecodeStatus.Success;
                return true;
            }

            int nextCapacityWidth = Math.Max(width, _capacityWidth == 0 ? 1 : _capacityWidth);
            int nextCapacityHeight = Math.Max(height, _capacityHeight == 0 ? 1 : _capacityHeight);
            if (_capacityWidth > 0)
            {
                int grownWidth = (long)_capacityWidth * 2 > _options.MaxWidth
                    ? _options.MaxWidth
                    : _capacityWidth * 2;
                nextCapacityWidth = Math.Max(nextCapacityWidth, grownWidth);
            }

            if (_capacityHeight > 0)
            {
                int grownHeight = (long)_capacityHeight * 2 > _options.MaxHeight
                    ? _options.MaxHeight
                    : _capacityHeight * 2;
                nextCapacityHeight = Math.Max(nextCapacityHeight, grownHeight);
            }

            if ((long)nextCapacityWidth * nextCapacityHeight > _options.MaxPixels)
            {
                nextCapacityWidth = width;
                nextCapacityHeight = height;
            }

            uint[] nextPixels = new uint[checked(nextCapacityWidth * nextCapacityHeight)];
            if (_background != 0)
            {
                Array.Fill(nextPixels, _background);
            }

            for (int row = 0; row < Height; row++)
            {
                Array.Copy(_pixels, row * _capacityWidth, nextPixels, row * nextCapacityWidth, Width);
            }

            _pixels = nextPixels;
            _capacityWidth = nextCapacityWidth;
            _capacityHeight = nextCapacityHeight;
            Width = Math.Max(Width, width);
            Height = Math.Max(Height, height);
            status = SixelDecodeStatus.Success;
            return true;
        }

        public void SetPixel(int x, int y, uint color)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            {
                return;
            }

            _pixels[(y * _capacityWidth) + x] = color;
        }

        public byte[] ToRgbaBytes()
        {
            byte[] rgba = new byte[checked(Width * Height * 4)];
            int destination = 0;
            for (int y = 0; y < Height; y++)
            {
                int source = y * _capacityWidth;
                for (int x = 0; x < Width; x++)
                {
                    uint pixel = _pixels[source + x];
                    rgba[destination++] = (byte)(pixel >> 24);
                    rgba[destination++] = (byte)(pixel >> 16);
                    rgba[destination++] = (byte)(pixel >> 8);
                    rgba[destination++] = (byte)pixel;
                }
            }

            return rgba;
        }
    }
}
