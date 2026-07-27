// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - NativeAOT-safe JSON serializer metadata.

using System.Text.Json.Serialization;

namespace RoyalTerminal.Terminal;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(TerminalCaptureSession))]
[JsonSerializable(typeof(TerminalCommandHistoryDocument))]
[JsonSerializable(typeof(TerminalSessionProfilesDocument))]
[JsonSerializable(typeof(TerminalWorkspaceDocument))]
internal sealed partial class TerminalIndentedJsonSerializerContext : JsonSerializerContext;

[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, ProtectedJsonFileSshSecretStore.ProtectedSecretRecord>))]
internal sealed partial class SshSecretJsonSerializerContext : JsonSerializerContext;
