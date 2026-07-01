// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Workspace serialization and persistence helpers.

using System.Text.Json;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Serialization helpers for <see cref="TerminalWorkspaceDocument"/>.
/// </summary>
public static class TerminalWorkspaceSerializer
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>
    /// Saves a workspace document to a stream.
    /// </summary>
    public static ValueTask SaveAsync(
        TerminalWorkspaceDocument document,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();

        TerminalWorkspaceDocument normalized = NormalizeAndValidate(document);
        return new ValueTask(JsonSerializer.SerializeAsync(stream, normalized, s_jsonOptions, cancellationToken));
    }

    /// <summary>
    /// Loads a workspace document from a stream.
    /// </summary>
    public static async ValueTask<TerminalWorkspaceDocument> LoadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();

        TerminalWorkspaceDocument? document = await JsonSerializer
            .DeserializeAsync<TerminalWorkspaceDocument>(stream, s_jsonOptions, cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
        {
            throw new InvalidDataException("Workspace document is empty or malformed.");
        }

        return NormalizeAndValidate(document);
    }

    /// <summary>
    /// Saves a workspace document to a file using an atomic replace.
    /// </summary>
    public static ValueTask SaveToFileAsync(
        TerminalWorkspaceDocument document,
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
    /// Loads a workspace document from a file.
    /// </summary>
    public static async ValueTask<TerminalWorkspaceDocument> LoadFromFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        cancellationToken.ThrowIfCancellationRequested();

        await using FileStream stream = File.OpenRead(filePath);
        return await LoadAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Serializes a workspace document to JSON text.
    /// </summary>
    public static string ToJson(TerminalWorkspaceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        TerminalWorkspaceDocument normalized = NormalizeAndValidate(document);
        return JsonSerializer.Serialize(normalized, s_jsonOptions);
    }

    /// <summary>
    /// Deserializes a workspace document from JSON text.
    /// </summary>
    public static TerminalWorkspaceDocument FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        TerminalWorkspaceDocument? document = JsonSerializer.Deserialize<TerminalWorkspaceDocument>(json, s_jsonOptions);
        if (document is null)
        {
            throw new InvalidDataException("Workspace JSON is empty or malformed.");
        }

        return NormalizeAndValidate(document);
    }

    private static TerminalWorkspaceDocument NormalizeAndValidate(TerminalWorkspaceDocument document)
    {
        if (document.FormatVersion <= 0 ||
            document.FormatVersion > TerminalWorkspaceDocument.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Unsupported workspace format version '{document.FormatVersion}'.");
        }

        List<TerminalWorkspaceWindow>? sourceWindows = document.Windows;
        List<TerminalWorkspaceWindow> normalizedWindows = sourceWindows is null
            ? []
            : new List<TerminalWorkspaceWindow>(sourceWindows.Count);
        HashSet<string> windowIds = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> tabIds = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; sourceWindows is not null && i < sourceWindows.Count; i++)
        {
            TerminalWorkspaceWindow? source = sourceWindows[i];
            if (source is null)
            {
                continue;
            }

            TerminalWorkspaceWindow window = NormalizeWindow(source, i, windowIds, tabIds);
            normalizedWindows.Add(window);
        }

        string? selectedWindowId = NormalizeOptional(document.SelectedWindowId);
        if (selectedWindowId is not null && !windowIds.Contains(selectedWindowId))
        {
            throw new InvalidDataException(
                $"Selected window '{selectedWindowId}' does not exist.");
        }

        if (selectedWindowId is null && normalizedWindows.Count > 0)
        {
            selectedWindowId = normalizedWindows[0].Id;
        }

        return document with
        {
            SelectedWindowId = selectedWindowId,
            Windows = normalizedWindows,
        };
    }

    private static TerminalWorkspaceWindow NormalizeWindow(
        TerminalWorkspaceWindow source,
        int windowIndex,
        HashSet<string> windowIds,
        HashSet<string> tabIds)
    {
        string id = NormalizeRequired(
            source.Id,
            $"Workspace window at index {windowIndex} is missing a valid id.");
        if (!windowIds.Add(id))
        {
            throw new InvalidDataException($"Workspace window id '{id}' is duplicated.");
        }

        List<TerminalWorkspaceTab>? sourceTabs = source.Tabs;
        List<TerminalWorkspaceTab> normalizedTabs = sourceTabs is null
            ? []
            : new List<TerminalWorkspaceTab>(sourceTabs.Count);
        HashSet<string> windowTabIds = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; sourceTabs is not null && i < sourceTabs.Count; i++)
        {
            TerminalWorkspaceTab? sourceTab = sourceTabs[i];
            if (sourceTab is null)
            {
                continue;
            }

            TerminalWorkspaceTab tab = NormalizeTab(sourceTab, id, i, tabIds, windowTabIds);
            normalizedTabs.Add(tab);
        }

        string? selectedTabId = NormalizeOptional(source.SelectedTabId);
        if (selectedTabId is not null && !windowTabIds.Contains(selectedTabId))
        {
            throw new InvalidDataException(
                $"Selected tab '{selectedTabId}' does not exist in workspace window '{id}'.");
        }

        if (selectedTabId is null && normalizedTabs.Count > 0)
        {
            selectedTabId = normalizedTabs[0].Id;
        }

        return source with
        {
            Id = id,
            Title = NormalizeOptional(source.Title),
            SelectedTabId = selectedTabId,
            WidthPixels = Math.Max(1, source.WidthPixels),
            HeightPixels = Math.Max(1, source.HeightPixels),
            Tabs = normalizedTabs,
        };
    }

    private static TerminalWorkspaceTab NormalizeTab(
        TerminalWorkspaceTab source,
        string windowId,
        int tabIndex,
        HashSet<string> tabIds,
        HashSet<string> windowTabIds)
    {
        string id = NormalizeRequired(
            source.Id,
            $"Workspace tab at index {tabIndex} in window '{windowId}' is missing a valid id.");
        if (!tabIds.Add(id))
        {
            throw new InvalidDataException($"Workspace tab id '{id}' is duplicated.");
        }

        if (!windowTabIds.Add(id))
        {
            throw new InvalidDataException(
                $"Workspace tab id '{id}' is duplicated in window '{windowId}'.");
        }

        string profileId = NormalizeRequired(
            source.ProfileId,
            $"Workspace tab '{id}' is missing a valid profile id.");
        string transportId = NormalizeTransportId(source.TransportId);
        HashSet<string> paneIds = new(StringComparer.OrdinalIgnoreCase);
        TerminalWorkspacePane rootPane = NormalizePane(
            source.RootPane ?? new TerminalWorkspacePane(),
            $"tab '{id}' root",
            paneIds);

        return source with
        {
            Id = id,
            ProfileId = profileId,
            Title = NormalizeOptional(source.Title) ?? profileId,
            WorkingDirectory = NormalizeOptional(source.WorkingDirectory),
            TransportId = transportId,
            TransportProfileId = NormalizeOptional(source.TransportProfileId),
            RenderMode = NormalizeRenderMode(source.RenderMode),
            RootPane = rootPane,
        };
    }

    private static TerminalWorkspacePane NormalizePane(
        TerminalWorkspacePane source,
        string context,
        HashSet<string> paneIds)
    {
        string id = NormalizeRequired(source.Id, $"Workspace pane in {context} is missing a valid id.");
        if (!paneIds.Add(id))
        {
            throw new InvalidDataException($"Workspace pane id '{id}' is duplicated in {context}.");
        }

        TerminalWorkspacePaneSplit? split = source.Split is null
            ? null
            : NormalizePaneSplit(source.Split, $"pane '{id}'", paneIds);

        return source with
        {
            Id = id,
            Title = NormalizeOptional(source.Title),
            ProfileId = NormalizeOptional(source.ProfileId),
            WorkingDirectory = NormalizeOptional(source.WorkingDirectory),
            TransportId = NormalizeOptionalTransportId(source.TransportId),
            TransportProfileId = NormalizeOptional(source.TransportProfileId),
            Split = split,
        };
    }

    private static TerminalWorkspacePaneSplit NormalizePaneSplit(
        TerminalWorkspacePaneSplit source,
        string context,
        HashSet<string> paneIds)
    {
        TerminalWorkspacePane firstPane = NormalizePane(
            source.FirstPane ?? new TerminalWorkspacePane { Id = "first" },
            $"{context} first split child",
            paneIds);
        TerminalWorkspacePane secondPane = NormalizePane(
            source.SecondPane ?? new TerminalWorkspacePane { Id = "second" },
            $"{context} second split child",
            paneIds);

        return source with
        {
            Orientation = NormalizeSplitOrientation(source.Orientation),
            Ratio = NormalizeSplitRatio(source.Ratio),
            FirstPane = firstPane,
            SecondPane = secondPane,
        };
    }

    private static double NormalizeSplitRatio(double value)
    {
        if (!double.IsFinite(value))
        {
            return 0.5;
        }

        return Math.Clamp(value, 0.05, 0.95);
    }

    private static string NormalizeSplitOrientation(string? orientation)
    {
        if (string.Equals(orientation, TerminalWorkspacePaneSplitOrientations.Horizontal, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalWorkspacePaneSplitOrientations.Horizontal;
        }

        if (string.Equals(orientation, TerminalWorkspacePaneSplitOrientations.Vertical, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalWorkspacePaneSplitOrientations.Vertical;
        }

        return TerminalWorkspacePaneSplitOrientations.Horizontal;
    }

    private static string NormalizeRenderMode(string? renderMode)
    {
        if (string.Equals(renderMode, TerminalWorkspaceRenderModes.Default, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalWorkspaceRenderModes.Default;
        }

        if (string.Equals(renderMode, TerminalWorkspaceRenderModes.Text, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalWorkspaceRenderModes.Text;
        }

        if (string.Equals(renderMode, TerminalWorkspaceRenderModes.Skia, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalWorkspaceRenderModes.Skia;
        }

        if (string.Equals(renderMode, TerminalWorkspaceRenderModes.Ghostty, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalWorkspaceRenderModes.Ghostty;
        }

        return TerminalWorkspaceRenderModes.Default;
    }

    private static string NormalizeTransportId(string? transportId)
    {
        if (string.Equals(transportId, TerminalTransportIds.Pty, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalTransportIds.Pty;
        }

        if (string.Equals(transportId, TerminalTransportIds.Pipe, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalTransportIds.Pipe;
        }

        if (string.Equals(transportId, TerminalTransportIds.Ssh, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalTransportIds.Ssh;
        }

        if (string.Equals(transportId, TerminalTransportIds.RawTcp, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalTransportIds.RawTcp;
        }

        if (string.Equals(transportId, TerminalTransportIds.Telnet, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalTransportIds.Telnet;
        }

        if (string.Equals(transportId, TerminalTransportIds.Serial, StringComparison.OrdinalIgnoreCase))
        {
            return TerminalTransportIds.Serial;
        }

        throw new InvalidDataException($"Unsupported transport id '{transportId}'.");
    }

    private static string? NormalizeOptionalTransportId(string? transportId)
    {
        return NormalizeOptional(transportId) is null
            ? null
            : NormalizeTransportId(transportId);
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
/// Persistence abstraction for workspace documents.
/// </summary>
public interface ITerminalWorkspaceStore
{
    /// <summary>
    /// Loads a workspace document.
    /// </summary>
    ValueTask<TerminalWorkspaceDocument> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a workspace document.
    /// </summary>
    ValueTask SaveAsync(TerminalWorkspaceDocument document, CancellationToken cancellationToken = default);
}

/// <summary>
/// JSON file-backed workspace store.
/// </summary>
public sealed class JsonFileTerminalWorkspaceStore : ITerminalWorkspaceStore
{
    /// <summary>
    /// Creates a JSON file workspace store.
    /// </summary>
    public JsonFileTerminalWorkspaceStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = filePath;
    }

    /// <summary>
    /// Gets the backing file path.
    /// </summary>
    public string FilePath { get; }

    /// <inheritdoc />
    public async ValueTask<TerminalWorkspaceDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(FilePath))
        {
            return new TerminalWorkspaceDocument();
        }

        await using FileStream stream = File.OpenRead(FilePath);
        return await TerminalWorkspaceSerializer.LoadAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask SaveAsync(TerminalWorkspaceDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(document);

        string json = TerminalWorkspaceSerializer.ToJson(document);
        SshSecretFileIo.WriteJsonAtomically(FilePath, json);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Factory and path helpers for workspace stores.
/// </summary>
public static class TerminalWorkspaceStoreFactory
{
    /// <summary>
    /// Creates a default JSON file workspace store.
    /// </summary>
    public static ITerminalWorkspaceStore CreateDefault(string? filePath = null)
    {
        string path = string.IsNullOrWhiteSpace(filePath)
            ? GetDefaultFilePath()
            : filePath;
        return new JsonFileTerminalWorkspaceStore(path);
    }

    /// <summary>
    /// Gets the default workspace file path.
    /// </summary>
    public static string GetDefaultFilePath()
    {
        string profilePath = TerminalSessionProfileStoreFactory.GetDefaultFilePath();
        string? directoryPath = Path.GetDirectoryName(profilePath);
        string baseDirectory = string.IsNullOrWhiteSpace(directoryPath)
            ? Directory.GetCurrentDirectory()
            : directoryPath;

        return Path.Combine(baseDirectory, "workspace.json");
    }
}
