// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoyalTerminal.Terminal;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(TerminalFontFamilySettings))]
internal sealed partial class TerminalFontFamilySettingsJsonContext : JsonSerializerContext { }

// Keep the existing profile reader's initializer/default semantics. The new
// immutable font model uses generated metadata, without reflecting its members.
internal sealed class TerminalFontFamilySettingsJsonConverter : JsonConverter<TerminalFontFamilySettings>
{
    public override TerminalFontFamilySettings? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
        => JsonSerializer.Deserialize(ref reader, TerminalFontFamilySettingsJsonContext.Default.TerminalFontFamilySettings)?.Normalize();

    public override void Write(Utf8JsonWriter writer, TerminalFontFamilySettings value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value.Normalize(), TerminalFontFamilySettingsJsonContext.Default.TerminalFontFamilySettings);
}
