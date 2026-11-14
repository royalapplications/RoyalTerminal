// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json.Serialization;

namespace RoyalTerminal.Multiplexing.Cli;

// Both plain and age-protected archives share this descriptor contract. Do not
// introduce reflection fallback: the archive must validate before making network calls.
[JsonSerializable(typeof(MultiplexerSessionDescriptor))]
internal partial class LayoutRestoreJson : JsonSerializerContext;
