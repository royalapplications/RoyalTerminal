// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text.Json;
using RoyalTerminal.Multiplexing.Automation;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Cli;

/// <summary>Resolves a single CLI invocation against bounded, authorized host listings.</summary>
internal sealed class CliTargetResolver(MultiplexingApiClient client)
{
    private const int MaximumCandidates = 4096;
    private readonly Dictionary<Guid, JsonElement> _sessions = [];

    public async ValueTask<Guid> SessionAsync(string selector, CancellationToken token)
    {
        if (TryIdentity(selector, out Guid identity)) return identity;
        JsonElement listing = await client.SendAsync(HttpMethod.Get, "api/sessions", cancellationToken: token);
        return Resolve(selector, ReadTargets(listing, "sessions"));
    }

    public async ValueTask<Guid> BlockAsync(Guid session, string selector, CancellationToken token)
    {
        if (TryIdentity(selector, out Guid identity)) return identity;
        JsonElement descriptor = await DescribeAsync(session, token);
        return Resolve(selector, ReadTargets(descriptor, "blocks"));
    }

    public async ValueTask<Guid> WindowAsync(Guid session, string selector, CancellationToken token)
    {
        if (TryIdentity(selector, out Guid identity)) return identity;
        JsonElement descriptor = await DescribeAsync(session, token);
        return Resolve(selector, ReadTargets(descriptor, "windows"));
    }

    private async ValueTask<JsonElement> DescribeAsync(Guid session, CancellationToken token)
    {
        if (_sessions.TryGetValue(session, out JsonElement known)) return known;
        JsonElement descriptor = await client.SendAsync(HttpMethod.Get, $"api/sessions/{session:D}", cancellationToken: token);
        _sessions.Add(session, descriptor);
        return descriptor;
    }

    private static bool TryIdentity(string selector, out Guid identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        if (selector.Length > 256) throw new ArgumentException("Target selectors cannot exceed 256 characters.");
        if (!Guid.TryParse(selector, out identity)) return false;
        if (identity == Guid.Empty) throw new ArgumentException("Target identities cannot be empty GUIDs.");
        return true;
    }

    private static IReadOnlyList<MultiplexerTarget> ReadTargets(JsonElement descriptor, string property)
    {
        if (descriptor.ValueKind != JsonValueKind.Object || !descriptor.TryGetProperty(property, out JsonElement items)
            || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > MaximumCandidates)
            throw new IOException("The host returned an invalid or oversized target listing.");

        List<MultiplexerTarget> targets = new(items.GetArrayLength());
        foreach (JsonElement item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out JsonElement id)
                || id.ValueKind != JsonValueKind.String || !id.TryGetGuid(out Guid identity) || identity == Guid.Empty
                || !item.TryGetProperty("label", out JsonElement label) || label.ValueKind != JsonValueKind.String)
                throw new IOException("The host returned an invalid target descriptor.");
            targets.Add(new(identity.ToString("D"), label.GetString()!));
        }
        return targets;
    }

    private static Guid Resolve(string selector, IReadOnlyList<MultiplexerTarget> targets)
        => Guid.Parse(MultiplexerTargetResolver.Resolve(selector, targets, MaximumCandidates).Id);
}
