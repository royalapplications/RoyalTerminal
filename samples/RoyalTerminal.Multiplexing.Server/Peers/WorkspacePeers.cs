// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoyalTerminal.Multiplexing.Server;

internal sealed record WorkspacePeer(string Id, string Label, string[] Endpoints, string? Icon);
internal sealed record WorkspacePeers(WorkspacePeer[] Hosts, string Hash)
{
    internal static WorkspacePeers Empty { get; } = Create([]);
    private static WorkspacePeers Create(WorkspacePeer[] hosts) => new(hosts,
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(hosts, PeerWire.Default.WorkspacePeerArray))));

    internal static WorkspacePeers Parse(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 64) throw Invalid();
        var hosts = new List<WorkspacePeer>(); var labels = new HashSet<string>(StringComparer.Ordinal);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement row in value.EnumerateArray())
        {
            var args = new ApiArguments(row); args.Validate("id", "label", "icon", "endpoints");
            string label = Name(args.String("label")), id = Name(args.String("id", label));
            if (!labels.Add(label) || !identities.Add(id) || !row.TryGetProperty("endpoints", out var endpoints) ||
                endpoints.ValueKind != JsonValueKind.Array || endpoints.GetArrayLength() is < 1 or > 8) throw Invalid();
            string[] addresses = endpoints.EnumerateArray().Select(x => Normalize(x.ValueKind == JsonValueKind.String ? x.GetString()! : throw Invalid())).ToArray();
            if (addresses.Distinct(StringComparer.Ordinal).Count() != addresses.Length) throw Invalid();
            hosts.Add(new(id, label, addresses, row.TryGetProperty("icon", out _) ? Name(args.String("icon")) : null));
        }
        return Create(hosts.ToArray());
    }

    internal static string Normalize(string address)
    {
        if (address.Length is 0 or > 2048 || address.Any(char.IsControl) || address.Contains('\\')) throw Invalid();
        if (address.StartsWith("unix:/", StringComparison.Ordinal))
        {
            string path = address[5..];
            if (path == "/" || path.EndsWith('/') || path.Contains("//",StringComparison.Ordinal) || path.Split('/').Any(p=>p is "." or "..")) throw Invalid();
            return address;
        }
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 ||
            uri.Fragment.Length != 0 || string.IsNullOrEmpty(uri.Host) || uri.AbsolutePath != "/") throw Invalid();
        return uri.GetLeftPart(UriPartial.Authority) + "/";
    }
    internal static string Name(string text) => text.Length is > 0 and <= 128 && text == text.Trim() && !text.Any(char.IsControl) ? text : throw Invalid();
    private static ApiException Invalid() => new(422,"invalid_host_declaration","Invalid or duplicate host identity, label or endpoint; use an HTTPS origin or an absolute unix: socket.");
}

[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition=JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(WorkspacePeers))]
[JsonSerializable(typeof(WorkspacePeer[]))]
[JsonSerializable(typeof(PeerCredentialPin[]))]
internal sealed partial class PeerWire : JsonSerializerContext;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record PeerCredentialPin(string Id,string Endpoint,string TokenFile);
