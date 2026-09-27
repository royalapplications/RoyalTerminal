// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json.Serialization;

namespace RoyalTerminal.Terminal;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(TerminalSessionProfilesDocument))]
internal sealed partial class TerminalSessionProfileJsonContext : JsonSerializerContext { }
