// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;
using System.Text.Json.Serialization;
using RoyalTerminal.Multiplexing;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Multiplexing.Server;

internal sealed record ApiError(string Code, string Message);
internal sealed record ApiSuccess(bool Success = true);
internal sealed record CapabilityDescription(bool Administrator, Guid? SessionId, bool CanControl);
internal sealed record ServerVersion(string Product, string Version, int Protocol, string Durability);
internal sealed record SessionList(IReadOnlyList<MultiplexerSessionDescriptor> Sessions);
internal sealed record MethodList(IReadOnlyList<MethodDescription> Methods);
internal sealed record MethodDescription(string Name, string Description, JsonElement InputSchema,
    JsonElement OutputSchema, JsonElement? RequestExample = null, string? ValidationNotes = null);
internal sealed record ApiCall(string Method, JsonElement Arguments);
internal sealed record NewSession(string Label);
internal sealed record NewBlock(string Label = "Shell", string Transport = "pty",
    TerminalCommandSpec? Command = null, string? WorkingDirectory = null,
    TerminalSessionDimensions? Dimensions = null, Guid? WindowId = null,
    Guid? SplitBlockId = null, string Orientation = "horizontal", double Ratio = 0.5,
    long? ExpectedRevision = null);
internal sealed record InputRequest(string Data);
internal sealed record ShareRequest(string Role = "observer", int LifetimeMinutes = 60);
internal sealed record ShareDescriptor(Guid Id, Guid SessionId, string Role, DateTimeOffset ExpiresUtc);
internal sealed record ShareCreated(Guid Id, Guid SessionId, string Role, DateTimeOffset ExpiresUtc, string Token);
internal sealed record ShareList(IReadOnlyList<ShareDescriptor> Shares);
internal sealed record ConnectedClient(Guid Id, Guid SessionId, Guid BlockId, string Role, DateTimeOffset ConnectedUtc);
internal sealed record ClientList(IReadOnlyList<ConnectedClient> Clients);
internal sealed record ScreenCapture(string Format, string Content, string Title, string? WorkingDirectory);

internal sealed record ServerFrame
{
    public string? Type { get; init; }
    public string? Kind { get; init; }
    public long Sequence { get; init; }
    public byte[]? Data { get; init; }
    public TerminalSessionDimensions? Dimensions { get; init; }
    public int? ExitCode { get; init; }
    public string? Code { get; init; }
    public string? Message { get; init; }
    public Guid? AttachmentId { get; init; }
    public bool HasControl { get; init; }
}

internal sealed record ClientFrame(string Type, string? Token = null, string? Data = null,
    int? Columns = null, int? Rows = null, int? WidthPixels = null, int? HeightPixels = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, UseStringEnumConverter = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ApiError))]
[JsonSerializable(typeof(ApiSuccess))]
[JsonSerializable(typeof(CapabilityDescription))]
[JsonSerializable(typeof(ServerVersion))]
[JsonSerializable(typeof(SessionList))]
[JsonSerializable(typeof(MultiplexerSessionDescriptor))]
[JsonSerializable(typeof(MultiplexerBlockDescriptor))]
[JsonSerializable(typeof(MultiplexerWindowDescriptor))]
[JsonSerializable(typeof(MultiplexerLayoutNode))]
[JsonSerializable(typeof(MethodList))]
[JsonSerializable(typeof(MethodDescription))]
[JsonSerializable(typeof(ApiCall))]
[JsonSerializable(typeof(NewSession))]
[JsonSerializable(typeof(NewBlock))]
[JsonSerializable(typeof(InputRequest))]
[JsonSerializable(typeof(ShareRequest))]
[JsonSerializable(typeof(ShareCreated))]
[JsonSerializable(typeof(ShareList))]
[JsonSerializable(typeof(ClientList))]
[JsonSerializable(typeof(ScreenCapture))]
[JsonSerializable(typeof(ServerFrame))]
[JsonSerializable(typeof(ClientFrame))]
[JsonSerializable(typeof(TerminalProgramStatus[]))]
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class ServerJsonContext : JsonSerializerContext;
