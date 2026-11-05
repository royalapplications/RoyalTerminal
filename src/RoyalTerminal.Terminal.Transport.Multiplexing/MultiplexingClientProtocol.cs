// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoyalTerminal.Terminal.Transport.Multiplexing;

internal sealed record MultiplexingServerMessage
{
    public string? Type { get; init; }
    public long Sequence { get; init; }
    public string? Kind { get; init; }
    public string? Data { get; init; }
    public TerminalSessionDimensions? Dimensions { get; init; }
    public int? ExitCode { get; init; }
    public string? Message { get; init; }
    public string? Code { get; init; }
    public Guid? AttachmentId { get; init; }
    public bool HasControl { get; init; }
}

internal sealed record MultiplexingClientMessage(string Type, string? Data = null,
    int? Columns = null, int? Rows = null, int? WidthPixels = null, int? HeightPixels = null);

internal sealed record MultiplexingApiCall(string Method, JsonElement Arguments);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MultiplexingServerMessage))]
[JsonSerializable(typeof(MultiplexingClientMessage))]
[JsonSerializable(typeof(MultiplexingApiCall))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class MultiplexingClientJsonContext : JsonSerializerContext;

/// <summary>An error reported by the host or detected while verifying the ordered stream.</summary>
public sealed class MultiplexingProtocolException : IOException
{
    /// <summary>Creates a protocol failure with a stable classification.</summary>
    public MultiplexingProtocolException(string code, string message) : base(message) => Code = code;

    /// <summary>Gets the host or client protocol error classification.</summary>
    public string Code { get; }
}
