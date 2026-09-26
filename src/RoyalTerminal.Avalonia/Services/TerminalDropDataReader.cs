// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.Services;

// Reads only representations explicitly offered by the OS drag object. A file
// drop exposes URI references, never opens file contents or follows remote URLs.
internal static class TerminalDropDataReader
{
    internal readonly record struct Format(string Mime, DataFormat Platform);
    private const int MaxBytes = 64 * 1024 * 1024;

    internal static Format[] GetFormats(IDataTransfer data)
    {
        List<Format> formats = new(4);
        if (data.Contains(DataFormat.File)) formats.Add(new("text/uri-list", DataFormat.File));
        if (data.Contains(DataFormat.Text)) formats.Add(new("text/plain", DataFormat.Text));
        foreach (DataFormat format in data.Formats)
        {
            if (formats.Count == 16) break;
            if (format.Kind != DataFormatKind.Platform || format is not (DataFormat<byte[]> or DataFormat<string>)) continue;
            string mime = format.Identifier;
            if (mime.Length is 0 or > 1024 || !mime.Contains('/')) continue;
            bool valid = true;
            foreach (char c in mime) if (c < 33 || c > 126) { valid = false; break; }
            foreach (Format existing in formats) if (existing.Mime == mime) { valid = false; break; }
            if (valid) formats.Add(new(mime, format));
        }
        return formats.ToArray();
    }

    internal static TerminalDropItem[] Read(IDataTransfer data, IReadOnlyList<Format> formats)
    {
        TerminalDropItem[] items = new TerminalDropItem[formats.Count];
        int remaining = MaxBytes;
        for (int i = 0; i < formats.Count; i++)
        {
            Format format = formats[i];
            ReadOnlyMemory<byte> bytes;
            if (format.Platform == DataFormat.File)
            {
                StringBuilder uris = new();
                IStorageItem[] files = data.TryGetFiles() ?? throw new InvalidDataException("Drop files are unavailable.");
                foreach (IStorageItem file in files)
                {
                    string uri = file.Path.AbsoluteUri;
                    if (uri.Length > remaining - uris.Length - 2) throw new InvalidDataException("Drop exceeds its byte quota.");
                    uris.Append(uri).Append("\r\n");
                }
                bytes = Encode(uris.ToString(), remaining);
            }
            else if (format.Platform is DataFormat<string> text)
                bytes = Encode(data.TryGetValue(text) ?? throw new InvalidDataException("Drop text is unavailable."), remaining);
            else if (format.Platform is DataFormat<byte[]> binary)
                bytes = data.TryGetValue(binary) ?? throw new InvalidDataException("Drop data is unavailable.");
            else throw new InvalidDataException("Unsupported drop representation.");
            if (bytes.Length > remaining) throw new InvalidDataException("Drop exceeds its byte quota.");
            remaining -= bytes.Length;
            items[i] = new(format.Mime, bytes);
        }
        return items;
    }

    private static byte[] Encode(string text, int remaining)
    {
        if (text.Length > remaining || Encoding.UTF8.GetByteCount(text) > remaining)
            throw new InvalidDataException("Drop exceeds its byte quota.");
        return Encoding.UTF8.GetBytes(text);
    }
}
