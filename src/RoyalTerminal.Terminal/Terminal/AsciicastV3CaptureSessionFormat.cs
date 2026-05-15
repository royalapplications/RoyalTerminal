// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - asciicast v3 capture session format.

using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Reads and writes asciinema asciicast v3 capture files.
/// </summary>
public sealed class AsciicastV3CaptureSessionFormat : ITerminalCaptureSessionFormat
{
    private const int FormatVersion = 3;
    private static readonly byte[] s_newLine = [(byte)'\n'];
    private static readonly UTF8Encoding s_strictUtf8 = new(false, throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions s_headerJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <inheritdoc />
    public TerminalCaptureFileFormatDescriptor Descriptor { get; } = new(
        TerminalCaptureSessionFormats.AsciicastV3Id,
        "Asciicast v3",
        ".cast",
        [".cast"],
        ["application/x-asciicast"]);

    /// <inheritdoc />
    public async ValueTask SaveAsync(
        TerminalCaptureSession session,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();

        TerminalCaptureSession normalizedSession =
            TerminalCaptureSessionValidator.NormalizeAndValidate(session);
        AsciicastHeader header = new()
        {
            Version = FormatVersion,
            Term = new AsciicastTerminal
            {
                Columns = normalizedSession.InitialColumns,
                Rows = normalizedSession.InitialRows,
            },
            Timestamp = normalizedSession.CreatedUtc.ToUnixTimeSeconds(),
        };

        await JsonSerializer
            .SerializeAsync(stream, header, s_headerJsonOptions, cancellationToken)
            .ConfigureAwait(false);
        await stream.WriteAsync(s_newLine, cancellationToken).ConfigureAwait(false);

        Utf8EventDecoder outputDecoder = new("output");
        Utf8EventDecoder inputDecoder = new("input");
        IReadOnlyList<TerminalCaptureEvent> events = normalizedSession.Events;
        long previousWrittenOffsetMilliseconds = 0;
        for (int i = 0; i < events.Count; i++)
        {
            TerminalCaptureEvent captureEvent = events[i];
            long offsetMilliseconds = Math.Max(previousWrittenOffsetMilliseconds, captureEvent.OffsetMilliseconds);
            string? data = GetEventData(captureEvent, outputDecoder, inputDecoder);
            if (data is null)
            {
                continue;
            }

            previousWrittenOffsetMilliseconds = await WriteEventLineAsync(
                stream,
                offsetMilliseconds,
                previousWrittenOffsetMilliseconds,
                GetEventCode(captureEvent.Kind),
                data,
                cancellationToken).ConfigureAwait(false);
        }

        outputDecoder.Complete();
        inputDecoder.Complete();
    }

    /// <inheritdoc />
    public async ValueTask<TerminalCaptureSession> LoadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();

        using StreamReader reader = new(
            stream,
            s_strictUtf8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: true);

        string? headerLine;
        try
        {
            headerLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException("Asciicast file is not valid UTF-8.", ex);
        }

        if (string.IsNullOrWhiteSpace(headerLine))
        {
            throw new InvalidDataException("Asciicast file is empty or missing a header.");
        }

        AsciicastHeaderData header = ParseHeader(headerLine);
        List<TerminalCaptureEvent> events = [];
        decimal elapsedMilliseconds = 0m;
        int lineNumber = 1;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? line;
            try
            {
                line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidDataException("Asciicast file is not valid UTF-8.", ex);
            }

            if (line is null)
            {
                break;
            }

            lineNumber++;
            string trimmedLine = line.Trim();
            if (trimmedLine.Length == 0 || trimmedLine[0] == '#')
            {
                continue;
            }

            ParseEventLine(trimmedLine, lineNumber, events, ref elapsedMilliseconds);
        }

        TerminalCaptureSession session = new()
        {
            FormatVersion = TerminalCaptureSession.CurrentFormatVersion,
            CreatedUtc = header.CreatedUtc,
            InitialColumns = header.Columns,
            InitialRows = header.Rows,
            DurationMilliseconds = (long)Math.Round(elapsedMilliseconds, MidpointRounding.AwayFromZero),
            Events = events,
        };

        return TerminalCaptureSessionValidator.NormalizeAndValidate(session);
    }

    private static async ValueTask<long> WriteEventLineAsync(
        Stream stream,
        long offsetMilliseconds,
        long previousOffsetMilliseconds,
        string code,
        string data,
        CancellationToken cancellationToken)
    {
        using MemoryStream lineStream = new();
        using (Utf8JsonWriter writer = new(lineStream))
        {
            long intervalMilliseconds = offsetMilliseconds - previousOffsetMilliseconds;
            decimal intervalSeconds = intervalMilliseconds / 1000m;
            writer.WriteStartArray();
            writer.WriteNumberValue(intervalSeconds);
            writer.WriteStringValue(code);
            writer.WriteStringValue(data);
            writer.WriteEndArray();
        }

        await stream
            .WriteAsync(lineStream.GetBuffer().AsMemory(0, checked((int)lineStream.Length)), cancellationToken)
            .ConfigureAwait(false);
        await stream.WriteAsync(s_newLine, cancellationToken).ConfigureAwait(false);
        return offsetMilliseconds;
    }

    private static string GetEventCode(TerminalCaptureEventKind kind)
    {
        return kind switch
        {
            TerminalCaptureEventKind.Output => "o",
            TerminalCaptureEventKind.Input => "i",
            TerminalCaptureEventKind.Resize => "r",
            TerminalCaptureEventKind.Marker => "m",
            TerminalCaptureEventKind.Exit => "x",
            _ => throw new InvalidDataException($"Unsupported capture event kind '{kind}'."),
        };
    }

    private static string? GetEventData(
        TerminalCaptureEvent captureEvent,
        Utf8EventDecoder outputDecoder,
        Utf8EventDecoder inputDecoder)
    {
        switch (captureEvent.Kind)
        {
            case TerminalCaptureEventKind.Output:
                return outputDecoder.Decode(captureEvent.Data ?? []);

            case TerminalCaptureEventKind.Input:
                return inputDecoder.Decode(captureEvent.Data ?? []);

            case TerminalCaptureEventKind.Resize:
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"{captureEvent.Columns}x{captureEvent.Rows}");

            case TerminalCaptureEventKind.Marker:
                return captureEvent.Label ?? string.Empty;

            case TerminalCaptureEventKind.Exit:
                return captureEvent.ExitCode.ToString(CultureInfo.InvariantCulture);

            default:
                throw new InvalidDataException($"Unsupported capture event kind '{captureEvent.Kind}'.");
        }
    }

    private static AsciicastHeaderData ParseHeader(string headerLine)
    {
        if (headerLine.TrimStart().StartsWith('#'))
        {
            throw new InvalidDataException("Asciicast header must be the first line and cannot be a comment.");
        }

        using JsonDocument document = JsonDocument.Parse(headerLine);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Asciicast header must be a JSON object.");
        }

        int version = ReadRequiredInt32(root, "version", "Asciicast header");
        if (version != FormatVersion)
        {
            throw new InvalidDataException($"Unsupported asciicast format version '{version}'.");
        }

        if (!root.TryGetProperty("term", out JsonElement term) || term.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Asciicast header is missing the required term object.");
        }

        int columns = ReadRequiredInt32(term, "cols", "Asciicast term header");
        int rows = ReadRequiredInt32(term, "rows", "Asciicast term header");
        if (columns <= 0 || rows <= 0)
        {
            throw new InvalidDataException($"Asciicast terminal dimensions are invalid ({columns}x{rows}).");
        }

        DateTimeOffset createdUtc = DateTimeOffset.UtcNow;
        if (root.TryGetProperty("timestamp", out JsonElement timestamp) &&
            timestamp.ValueKind == JsonValueKind.Number &&
            timestamp.TryGetInt64(out long unixTimestamp))
        {
            createdUtc = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
        }

        return new AsciicastHeaderData(columns, rows, createdUtc);
    }

    private static void ParseEventLine(
        string line,
        int lineNumber,
        List<TerminalCaptureEvent> events,
        ref decimal elapsedMilliseconds)
    {
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() != 3)
        {
            throw new InvalidDataException(
                $"Asciicast event at line {lineNumber} must be a 3-element JSON array.");
        }

        decimal intervalSeconds = ReadIntervalSeconds(root[0], lineNumber);
        elapsedMilliseconds += intervalSeconds * 1000m;
        long offsetMilliseconds = (long)Math.Round(elapsedMilliseconds, MidpointRounding.AwayFromZero);
        string code = ReadRequiredString(root[1], lineNumber, "event code");
        string data = ReadRequiredString(root[2], lineNumber, "event data");

        switch (code)
        {
            case "o":
            case "i":
                byte[] bytes = Encoding.UTF8.GetBytes(data);
                if (bytes.Length == 0)
                {
                    return;
                }

                events.Add(new TerminalCaptureEvent
                {
                    OffsetMilliseconds = offsetMilliseconds,
                    Kind = code == "o" ? TerminalCaptureEventKind.Output : TerminalCaptureEventKind.Input,
                    Data = bytes,
                });
                break;

            case "r":
                ParseResize(data, lineNumber, out int columns, out int rows);
                events.Add(new TerminalCaptureEvent
                {
                    OffsetMilliseconds = offsetMilliseconds,
                    Kind = TerminalCaptureEventKind.Resize,
                    Columns = columns,
                    Rows = rows,
                });
                break;

            case "m":
                events.Add(new TerminalCaptureEvent
                {
                    OffsetMilliseconds = offsetMilliseconds,
                    Kind = TerminalCaptureEventKind.Marker,
                    Label = data,
                });
                break;

            case "x":
                events.Add(new TerminalCaptureEvent
                {
                    OffsetMilliseconds = offsetMilliseconds,
                    Kind = TerminalCaptureEventKind.Exit,
                    ExitCode = ParseExitCode(data, lineNumber),
                });
                break;
        }
    }

    private static int ReadRequiredInt32(JsonElement element, string propertyName, string owner)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt32(out int value))
        {
            throw new InvalidDataException($"{owner} is missing integer property '{propertyName}'.");
        }

        return value;
    }

    private static decimal ReadIntervalSeconds(JsonElement element, int lineNumber)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDecimal(out decimal value))
        {
            throw new InvalidDataException($"Asciicast event at line {lineNumber} has an invalid interval.");
        }

        if (value < 0)
        {
            throw new InvalidDataException($"Asciicast event at line {lineNumber} has a negative interval.");
        }

        return value;
    }

    private static string ReadRequiredString(JsonElement element, int lineNumber, string fieldName)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                $"Asciicast event at line {lineNumber} has invalid {fieldName}.");
        }

        return element.GetString() ?? string.Empty;
    }

    private static void ParseResize(string data, int lineNumber, out int columns, out int rows)
    {
        int separatorIndex = data.IndexOf('x', StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex == data.Length - 1)
        {
            throw new InvalidDataException(
                $"Asciicast resize event at line {lineNumber} has invalid size '{data}'.");
        }

        ReadOnlySpan<char> columnsSpan = data.AsSpan(0, separatorIndex);
        ReadOnlySpan<char> rowsSpan = data.AsSpan(separatorIndex + 1);
        if (!int.TryParse(columnsSpan, NumberStyles.None, CultureInfo.InvariantCulture, out columns) ||
            !int.TryParse(rowsSpan, NumberStyles.None, CultureInfo.InvariantCulture, out rows) ||
            columns <= 0 ||
            rows <= 0)
        {
            throw new InvalidDataException(
                $"Asciicast resize event at line {lineNumber} has invalid size '{data}'.");
        }
    }

    private static int ParseExitCode(string data, int lineNumber)
    {
        if (!int.TryParse(data, NumberStyles.Integer, CultureInfo.InvariantCulture, out int exitCode))
        {
            throw new InvalidDataException(
                $"Asciicast exit event at line {lineNumber} has invalid status '{data}'.");
        }

        return exitCode;
    }

    private sealed class Utf8EventDecoder
    {
        private readonly Decoder _decoder = s_strictUtf8.GetDecoder();
        private readonly string _eventKind;

        public Utf8EventDecoder(string eventKind)
        {
            _eventKind = eventKind;
        }

        public string? Decode(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty)
            {
                return null;
            }

            char[] rented = ArrayPool<char>.Shared.Rent(Math.Max(2, data.Length + 2));
            try
            {
                _decoder.Convert(
                    data,
                    rented,
                    flush: false,
                    out int bytesUsed,
                    out int charsUsed,
                    out bool completed);
                if (bytesUsed != data.Length || !completed)
                {
                    throw new InvalidDataException(
                        $"Asciicast v3 could not encode the terminal {_eventKind} payload.");
                }

                return charsUsed == 0 ? null : new string(rented, 0, charsUsed);
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidDataException(
                    $"Asciicast v3 requires terminal {_eventKind} payloads to be valid UTF-8.",
                    ex);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(rented, clearArray: true);
            }
        }

        public void Complete()
        {
            char[] rented = ArrayPool<char>.Shared.Rent(2);
            try
            {
                _decoder.Convert(
                    ReadOnlySpan<byte>.Empty,
                    rented,
                    flush: true,
                    out int bytesUsed,
                    out int charsUsed,
                    out bool completed);
                if (bytesUsed != 0 || charsUsed != 0 || !completed)
                {
                    throw new InvalidDataException(
                        $"Asciicast v3 could not encode the terminal {_eventKind} payload.");
                }
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidDataException(
                    $"Asciicast v3 requires terminal {_eventKind} payloads to end with a complete UTF-8 sequence.",
                    ex);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(rented, clearArray: true);
            }
        }
    }

    private sealed record AsciicastHeader
    {
        [JsonPropertyName("version")]
        public int Version { get; init; }

        [JsonPropertyName("term")]
        public AsciicastTerminal Term { get; init; } = new();

        [JsonPropertyName("timestamp")]
        public long? Timestamp { get; init; }
    }

    private sealed record AsciicastTerminal
    {
        [JsonPropertyName("cols")]
        public int Columns { get; init; }

        [JsonPropertyName("rows")]
        public int Rows { get; init; }
    }

    private readonly record struct AsciicastHeaderData(
        int Columns,
        int Rows,
        DateTimeOffset CreatedUtc);
}
