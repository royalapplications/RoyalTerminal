// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.App - App preference persistence.

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using RoyalTerminal.Avalonia.App.ViewModels;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.App.Services;

/// <summary>
/// Stores app-level preferences that are independent from terminal profiles and workspaces.
/// </summary>
public sealed record AppPreferencesDocument
{
    /// <summary>
    /// Gets or initializes the selected application chrome theme.
    /// </summary>
    public AppThemeMode AppThemeMode { get; init; } = AppThemeMode.System;

    /// <summary>
    /// Gets or initializes the last main window placement.
    /// </summary>
    public AppWindowPlacement? WindowPlacement { get; init; }
}

/// <summary>
/// Describes the persisted main window state.
/// </summary>
public enum AppWindowState
{
    /// <summary>
    /// Normal restored window state.
    /// </summary>
    Normal,

    /// <summary>
    /// Maximized window state.
    /// </summary>
    Maximized,

    /// <summary>
    /// Full-screen window state.
    /// </summary>
    FullScreen,
}

/// <summary>
/// Stores the last known main window placement.
/// </summary>
/// <param name="X">Window X coordinate in screen pixels.</param>
/// <param name="Y">Window Y coordinate in screen pixels.</param>
/// <param name="Width">Restored window width in DIPs.</param>
/// <param name="Height">Restored window height in DIPs.</param>
/// <param name="State">Window state to restore.</param>
public sealed record AppWindowPlacement(
    int X,
    int Y,
    double Width,
    double Height,
    AppWindowState State);

/// <summary>
/// Persistence abstraction for app-level preferences.
/// </summary>
public interface IAppPreferencesStore
{
    /// <summary>
    /// Loads app-level preferences.
    /// </summary>
    ValueTask<AppPreferencesDocument> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves app-level preferences.
    /// </summary>
    ValueTask SaveAsync(AppPreferencesDocument document, CancellationToken cancellationToken = default);
}

/// <summary>
/// JSON serializer for app-level preferences.
/// </summary>
public static class AppPreferencesSerializer
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Serializes app-level preferences to JSON.
    /// </summary>
    public static string ToJson(AppPreferencesDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return JsonSerializer.Serialize(document, s_options);
    }

    /// <summary>
    /// Deserializes app-level preferences from JSON.
    /// </summary>
    public static AppPreferencesDocument FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new AppPreferencesDocument();
        }

        return JsonSerializer.Deserialize<AppPreferencesDocument>(json, s_options)
            ?? new AppPreferencesDocument();
    }
}

/// <summary>
/// JSON file-backed app preference store.
/// </summary>
public sealed class JsonFileAppPreferencesStore : IAppPreferencesStore
{
    /// <summary>
    /// Creates a JSON file app preference store.
    /// </summary>
    public JsonFileAppPreferencesStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = filePath;
    }

    /// <summary>
    /// Gets the backing file path.
    /// </summary>
    public string FilePath { get; }

    /// <inheritdoc />
    public async ValueTask<AppPreferencesDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(FilePath))
        {
            return new AppPreferencesDocument();
        }

        string json = await File.ReadAllTextAsync(FilePath, cancellationToken).ConfigureAwait(false);
        return AppPreferencesSerializer.FromJson(json);
    }

    /// <inheritdoc />
    public ValueTask SaveAsync(AppPreferencesDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(document);

        string json = AppPreferencesSerializer.ToJson(document);
        WriteJsonAtomically(FilePath, json);
        return ValueTask.CompletedTask;
    }

    private static void WriteJsonAtomically(string filePath, string json)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = filePath + ".tmp";
        File.WriteAllText(temporaryPath, json);
        if (File.Exists(filePath))
        {
            File.Replace(temporaryPath, filePath, null);
            return;
        }

        File.Move(temporaryPath, filePath);
    }
}

/// <summary>
/// Factory and path helpers for app preference stores.
/// </summary>
public static class AppPreferencesStoreFactory
{
    /// <summary>
    /// Creates the default app preference store.
    /// </summary>
    public static IAppPreferencesStore CreateDefault(string? filePath = null)
    {
        string path = string.IsNullOrWhiteSpace(filePath)
            ? GetDefaultFilePath()
            : filePath;
        return new JsonFileAppPreferencesStore(path);
    }

    /// <summary>
    /// Gets the default app preference file path.
    /// </summary>
    public static string GetDefaultFilePath()
    {
        string profilePath = TerminalSessionProfileStoreFactory.GetDefaultFilePath();
        string directory = Path.GetDirectoryName(profilePath) ?? Directory.GetCurrentDirectory();
        return Path.Combine(directory, "app-preferences.json");
    }
}
