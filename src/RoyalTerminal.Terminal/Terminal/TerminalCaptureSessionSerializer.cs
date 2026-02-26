// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Capture session persistence helpers.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Serialization helpers for <see cref="TerminalCaptureSession"/>.
/// </summary>
public static class TerminalCaptureSessionSerializer
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    /// <summary>
    /// Saves a capture session to a stream in JSON format.
    /// </summary>
    public static ValueTask SaveAsync(
        TerminalCaptureSession session,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();

        return new ValueTask(
            JsonSerializer.SerializeAsync(stream, session, s_jsonOptions, cancellationToken));
    }

    /// <summary>
    /// Loads a capture session from a JSON stream.
    /// </summary>
    public static async ValueTask<TerminalCaptureSession> LoadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();

        TerminalCaptureSession? session = await JsonSerializer
            .DeserializeAsync<TerminalCaptureSession>(stream, s_jsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (session is null)
        {
            throw new InvalidDataException("Capture file is empty or malformed.");
        }

        return NormalizeAndValidate(session);
    }

    /// <summary>
    /// Saves a capture session to a file in JSON format.
    /// </summary>
    public static async ValueTask SaveToFileAsync(
        TerminalCaptureSession session,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        string directoryPath = Path.GetDirectoryName(filePath) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        await using FileStream stream = File.Create(filePath);
        await SaveAsync(session, stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads a capture session from a JSON file.
    /// </summary>
    public static async ValueTask<TerminalCaptureSession> LoadFromFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        await using FileStream stream = File.OpenRead(filePath);
        return await LoadAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private static TerminalCaptureSession NormalizeAndValidate(TerminalCaptureSession session)
    {
        if (session.FormatVersion <= 0 || session.FormatVersion > TerminalCaptureSession.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Unsupported capture format version '{session.FormatVersion}'.");
        }

        List<TerminalCaptureEvent> sourceEvents = session.Events ?? [];
        List<OrderedCaptureEvent> normalizedEvents = new(sourceEvents.Count);
        for (int i = 0; i < sourceEvents.Count; i++)
        {
            TerminalCaptureEvent sourceEvent = sourceEvents[i];
            long offset = sourceEvent.OffsetMilliseconds < 0 ? 0 : sourceEvent.OffsetMilliseconds;
            TerminalCaptureEventKind kind = sourceEvent.Kind;

            switch (kind)
            {
                case TerminalCaptureEventKind.Output:
                case TerminalCaptureEventKind.Input:
                    if (sourceEvent.Data is null || sourceEvent.Data.Length == 0)
                    {
                        throw new InvalidDataException(
                            $"Capture event at index {i} requires a non-empty data payload.");
                    }

                    normalizedEvents.Add(new OrderedCaptureEvent(
                        sourceEvent with
                        {
                            OffsetMilliseconds = offset,
                            Data = sourceEvent.Data.ToArray(),
                            Columns = 0,
                            Rows = 0,
                        },
                        i));
                    break;

                case TerminalCaptureEventKind.Resize:
                    if (sourceEvent.Columns <= 0 || sourceEvent.Rows <= 0)
                    {
                        throw new InvalidDataException(
                            $"Resize event at index {i} has invalid dimensions ({sourceEvent.Columns}x{sourceEvent.Rows}).");
                    }

                    normalizedEvents.Add(new OrderedCaptureEvent(
                        sourceEvent with
                        {
                            OffsetMilliseconds = offset,
                            Data = null,
                            Columns = sourceEvent.Columns,
                            Rows = sourceEvent.Rows,
                        },
                        i));
                    break;

                default:
                    throw new InvalidDataException(
                        $"Capture event at index {i} uses unsupported kind '{kind}'.");
            }
        }

        normalizedEvents.Sort(static (left, right) =>
        {
            int offsetComparison = left.Event.OffsetMilliseconds.CompareTo(right.Event.OffsetMilliseconds);
            return offsetComparison != 0
                ? offsetComparison
                : left.OriginalIndex.CompareTo(right.OriginalIndex);
        });

        List<TerminalCaptureEvent> orderedEvents = new(normalizedEvents.Count);
        for (int i = 0; i < normalizedEvents.Count; i++)
        {
            orderedEvents.Add(normalizedEvents[i].Event);
        }

        return session with
        {
            InitialColumns = Math.Max(1, session.InitialColumns),
            InitialRows = Math.Max(1, session.InitialRows),
            Events = orderedEvents,
        };
    }

    private readonly record struct OrderedCaptureEvent(TerminalCaptureEvent Event, int OriginalIndex);
}
