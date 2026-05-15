// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Native RoyalTerminal capture session format.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Reads and writes the native RoyalTerminal JSON capture format.
/// </summary>
public sealed class RoyalTerminalCaptureSessionFormat : ITerminalCaptureSessionFormat
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter(),
        },
    };

    /// <inheritdoc />
    public TerminalCaptureFileFormatDescriptor Descriptor { get; } = new(
        TerminalCaptureSessionFormats.RoyalTerminalJsonId,
        "RoyalTerminal JSON",
        ".rtcap.json",
        [".rtcap.json", ".json"],
        ["application/json"]);

    /// <inheritdoc />
    public ValueTask SaveAsync(
        TerminalCaptureSession session,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();

        return new ValueTask(
            JsonSerializer.SerializeAsync(stream, session, JsonOptions, cancellationToken));
    }

    /// <inheritdoc />
    public async ValueTask<TerminalCaptureSession> LoadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();

        using JsonDocument document = await JsonDocument
            .ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        JsonElement root = document.RootElement;
        if (LooksLikeAsciicastHeader(root))
        {
            throw new InvalidDataException("Capture file is an asciicast recording, not a RoyalTerminal JSON capture.");
        }

        TerminalCaptureSession? session = root.Deserialize<TerminalCaptureSession>(JsonOptions);
        if (session is null)
        {
            throw new InvalidDataException("Capture file is empty or malformed.");
        }

        return TerminalCaptureSessionValidator.NormalizeAndValidate(session);
    }

    private static bool LooksLikeAsciicastHeader(JsonElement root)
    {
        return root.ValueKind == JsonValueKind.Object &&
               !root.TryGetProperty("formatVersion", out _) &&
               root.TryGetProperty("version", out JsonElement version) &&
               version.ValueKind == JsonValueKind.Number &&
               version.TryGetInt32(out int versionValue) &&
               versionValue == 3 &&
               root.TryGetProperty("term", out JsonElement term) &&
               term.ValueKind == JsonValueKind.Object;
    }
}
