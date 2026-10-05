// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Buffers.Text;
using System.Text;

namespace RoyalTerminal.Terminal;

internal static class ManagedKittyResponseFormatter
{
    internal const int MaximumHeaderBytes = 64;

    internal static byte[] Format(uint imageId, uint imageNumber, uint placementId, uint frame, string message)
    {
        Span<byte> header = stackalloc byte[MaximumHeaderBytes];
        int length = WriteHeader(header, imageId, imageNumber, placementId, frame);
        if (length == 0) return [];
        // Host medium readers may supply arbitrary errors. Keep their previous
        // ASCII replacement policy without constraining messages to stack size.
        int messageLength = Encoding.ASCII.GetByteCount(message);
        byte[] result = new byte[checked(length + messageLength + 2)];
        header[..length].CopyTo(result);
        Encoding.ASCII.GetBytes(message, result.AsSpan(length, messageLength));
        result[^2] = 0x1B;
        result[^1] = (byte)'\\';
        return result;
    }

    internal static int WriteHeader(Span<byte> buffer, uint imageId, uint imageNumber, uint placementId, uint frame)
    {
        if (imageId == 0 && imageNumber == 0) return 0;
        if (buffer.Length < MaximumHeaderBytes) throw new ArgumentException("Kitty reply header scratch is too small.", nameof(buffer));
        "\u001b_G"u8.CopyTo(buffer);
        int count = 3;
        if (imageId != 0) Field(buffer, ref count, (byte)'i', imageId, comma: false);
        if (imageNumber != 0) Field(buffer, ref count, (byte)'I', imageNumber, comma: imageId != 0);
        if (placementId != 0) Field(buffer, ref count, (byte)'p', placementId, comma: true);
        if (frame != 0) Field(buffer, ref count, (byte)'r', frame, comma: true);
        buffer[count++] = (byte)';';
        return count;
    }

    private static void Field(Span<byte> buffer, ref int count, byte key, uint value, bool comma)
    {
        if (comma) buffer[count++] = (byte)',';
        buffer[count++] = key;
        buffer[count++] = (byte)'=';
        if (!Utf8Formatter.TryFormat(value, buffer[count..], out int written))
            throw new InvalidOperationException("Bounded Kitty reply field did not fit.");
        count += written;
    }
}
