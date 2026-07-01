// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Command history serialization and persistence helpers.

using System.Text.Json;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Serialization helpers for <see cref="TerminalCommandHistoryDocument"/>.
/// </summary>
public static class TerminalCommandHistorySerializer
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>
    /// Saves a command history document to a stream.
    /// </summary>
    public static ValueTask SaveAsync(
        TerminalCommandHistoryDocument document,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();

        TerminalCommandHistoryDocument normalized = NormalizeAndValidate(document);
        return new ValueTask(JsonSerializer.SerializeAsync(stream, normalized, s_jsonOptions, cancellationToken));
    }

    /// <summary>
    /// Loads a command history document from a stream.
    /// </summary>
    public static async ValueTask<TerminalCommandHistoryDocument> LoadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();

        TerminalCommandHistoryDocument? document = await JsonSerializer
            .DeserializeAsync<TerminalCommandHistoryDocument>(stream, s_jsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
        {
            throw new InvalidDataException("Command history document is empty or malformed.");
        }

        return NormalizeAndValidate(document);
    }

    /// <summary>
    /// Saves a command history document to a file using an atomic replace.
    /// </summary>
    public static ValueTask SaveToFileAsync(
        TerminalCommandHistoryDocument document,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        string json = ToJson(document);
        SshSecretFileIo.WriteJsonAtomically(filePath, json);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Loads a command history document from a file.
    /// </summary>
    public static async ValueTask<TerminalCommandHistoryDocument> LoadFromFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        await using FileStream stream = File.OpenRead(filePath);
        return await LoadAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Serializes a command history document to JSON text.
    /// </summary>
    public static string ToJson(TerminalCommandHistoryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        TerminalCommandHistoryDocument normalized = NormalizeAndValidate(document);
        return JsonSerializer.Serialize(normalized, s_jsonOptions);
    }

    /// <summary>
    /// Deserializes a command history document from JSON text.
    /// </summary>
    public static TerminalCommandHistoryDocument FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        TerminalCommandHistoryDocument? document = JsonSerializer
            .Deserialize<TerminalCommandHistoryDocument>(json, s_jsonOptions);
        if (document is null)
        {
            throw new InvalidDataException("Command history JSON is empty or malformed.");
        }

        return NormalizeAndValidate(document);
    }

    private static TerminalCommandHistoryDocument NormalizeAndValidate(TerminalCommandHistoryDocument document)
    {
        if (document.FormatVersion <= 0 ||
            document.FormatVersion > TerminalCommandHistoryDocument.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Unsupported command history format version '{document.FormatVersion}'.");
        }

        int retentionLimit = document.RetentionLimit > 0
            ? document.RetentionLimit
            : TerminalCommandHistoryDocument.DefaultRetentionLimit;
        List<TerminalCommandHistoryEntry>? sourceEntries = document.Entries;
        List<TerminalCommandHistoryEntry> normalizedEntries = sourceEntries is null
            ? []
            : new List<TerminalCommandHistoryEntry>(sourceEntries.Count);
        HashSet<string> entryIds = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; sourceEntries is not null && i < sourceEntries.Count; i++)
        {
            TerminalCommandHistoryEntry? sourceEntry = sourceEntries[i];
            if (sourceEntry is null)
            {
                continue;
            }

            normalizedEntries.Add(NormalizeEntry(sourceEntry, i, entryIds));
        }

        if (normalizedEntries.Count > retentionLimit)
        {
            normalizedEntries.Sort(static (left, right) =>
                left.StartedAtUtc.CompareTo(right.StartedAtUtc));
            normalizedEntries.RemoveRange(0, normalizedEntries.Count - retentionLimit);
        }

        return document with
        {
            RetentionLimit = retentionLimit,
            Entries = normalizedEntries,
        };
    }

    private static TerminalCommandHistoryEntry NormalizeEntry(
        TerminalCommandHistoryEntry source,
        int entryIndex,
        HashSet<string> entryIds)
    {
        string id = NormalizeRequired(
            source.Id,
            $"Command history entry at index {entryIndex} is missing a valid id.");
        if (!entryIds.Add(id))
        {
            throw new InvalidDataException($"Command history entry id '{id}' is duplicated.");
        }

        string commandLine = NormalizeRequired(
            source.CommandLine,
            $"Command history entry '{id}' is missing a valid command line.");
        DateTimeOffset startedAtUtc = source.StartedAtUtc.ToUniversalTime();
        DateTimeOffset? completedAtUtc = source.CompletedAtUtc?.ToUniversalTime();
        if (completedAtUtc is not null && completedAtUtc.Value < startedAtUtc)
        {
            throw new InvalidDataException(
                $"Command history entry '{id}' completed before it started.");
        }

        return source with
        {
            Id = id,
            CommandLine = commandLine,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            WorkingDirectory = NormalizeOptional(source.WorkingDirectory),
            ProfileId = NormalizeOptional(source.ProfileId),
            TransportId = NormalizeOptionalTransportId(source.TransportId),
            Host = NormalizeOptional(source.Host),
            ShellId = NormalizeOptional(source.ShellId),
            ApplicationId = NormalizeOptional(source.ApplicationId),
        };
    }

    private static string? NormalizeOptionalTransportId(string? transportId)
    {
        string? normalized = NormalizeOptional(transportId);
        if (normalized is null)
        {
            return null;
        }

        if (string.Equals(normalized, TerminalTransportIds.Pty, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalTransportIds.Pty;
        }

        if (string.Equals(normalized, TerminalTransportIds.Pipe, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalTransportIds.Pipe;
        }

        if (string.Equals(normalized, TerminalTransportIds.Ssh, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalTransportIds.Ssh;
        }

        if (string.Equals(normalized, TerminalTransportIds.RawTcp, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalTransportIds.RawTcp;
        }

        if (string.Equals(normalized, TerminalTransportIds.Telnet, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalTransportIds.Telnet;
        }

        if (string.Equals(normalized, TerminalTransportIds.Serial, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalTransportIds.Serial;
        }

        throw new InvalidDataException($"Unsupported transport id '{transportId}'.");
    }

    private static string NormalizeRequired(string? value, string error)
    {
        string? normalized = NormalizeOptional(value);
        return normalized ?? throw new InvalidDataException(error);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

/// <summary>
/// Persistence abstraction for command history documents.
/// </summary>
public interface ITerminalCommandHistoryStore
{
    /// <summary>
    /// Loads a command history document.
    /// </summary>
    ValueTask<TerminalCommandHistoryDocument> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a command history document.
    /// </summary>
    ValueTask SaveAsync(TerminalCommandHistoryDocument document, CancellationToken cancellationToken = default);
}

/// <summary>
/// JSON file-backed command history store.
/// </summary>
public sealed class JsonFileTerminalCommandHistoryStore : ITerminalCommandHistoryStore
{
    /// <summary>
    /// Creates a JSON file command history store.
    /// </summary>
    public JsonFileTerminalCommandHistoryStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = filePath;
    }

    /// <summary>
    /// Gets the backing file path.
    /// </summary>
    public string FilePath { get; }

    /// <inheritdoc />
    public async ValueTask<TerminalCommandHistoryDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(FilePath))
        {
            return new TerminalCommandHistoryDocument();
        }

        await using FileStream stream = File.OpenRead(FilePath);
        return await TerminalCommandHistorySerializer.LoadAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask SaveAsync(TerminalCommandHistoryDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(document);

        string json = TerminalCommandHistorySerializer.ToJson(document);
        SshSecretFileIo.WriteJsonAtomically(FilePath, json);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Factory and path helpers for command history stores.
/// </summary>
public static class TerminalCommandHistoryStoreFactory
{
    /// <summary>
    /// Creates a default JSON file command history store.
    /// </summary>
    public static ITerminalCommandHistoryStore CreateDefault(string? filePath = null)
    {
        string path = string.IsNullOrWhiteSpace(filePath)
            ? GetDefaultFilePath()
            : filePath;
        return new JsonFileTerminalCommandHistoryStore(path);
    }

    /// <summary>
    /// Gets the default command history file path.
    /// </summary>
    public static string GetDefaultFilePath()
    {
        string profilePath = TerminalSessionProfileStoreFactory.GetDefaultFilePath();
        string? directoryPath = Path.GetDirectoryName(profilePath);
        string baseDirectory = string.IsNullOrWhiteSpace(directoryPath)
            ? Directory.GetCurrentDirectory()
            : directoryPath;

        return Path.Combine(baseDirectory, "command-history.json");
    }
}
