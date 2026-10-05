// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;

namespace RoyalTerminal.Avalonia.App.Services.Notifications;

internal interface IDesktopThemeFiles
{
    bool Exists(string path);
    string? ReadText(string path);
}

// Used only by the owned notification worker. Neither icon nor sound contents
// are loaded here: the notification daemon loads the selected local resource.
internal sealed class DesktopThemeFiles : IDesktopThemeFiles
{
    private const int MaximumDocumentBytes = 256 * 1024;

    public bool Exists(string path) => File.Exists(path);

    public string? ReadText(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            if (stream.Length > MaximumDocumentBytes) return null;
            using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            int maximumCharacters = (int)stream.Length;
            char[] buffer = new char[maximumCharacters + 1];
            int count = reader.ReadBlock(buffer, 0, buffer.Length);
            return count <= maximumCharacters ? new string(buffer, 0, count) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return null; }
    }
}

internal sealed class DesktopThemeDocument
{
    private readonly Dictionary<(string Section, string Key), string> _values = new();

    internal DesktopThemeDocument(string? text)
    {
        if (text is null || text.Length > 256 * 1024) return;
        string section = string.Empty;
        using StringReader reader = new(text);
        while (_values.Count < 4096 && reader.ReadLine() is { } line)
        {
            ReadOnlySpan<char> value = line.AsSpan().Trim();
            if (value.IsEmpty || value[0] == '#') continue;
            if (value[0] == '[' && value[^1] == ']') { section = value[1..^1].ToString(); continue; }
            int separator = value.IndexOf('=');
            if (separator <= 0) continue;
            _values.TryAdd((section, value[..separator].Trim().ToString()), value[(separator + 1)..].Trim().ToString());
        }
    }

    internal string Get(string section, string key) => _values.GetValueOrDefault((section, key), string.Empty);

    internal string[] List(string section, string key)
        => Get(section, key).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    internal int Number(string section, string key, int fallback)
        => int.TryParse(Get(section, key), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out int value) && value <= 65536 ? value : fallback;
}
