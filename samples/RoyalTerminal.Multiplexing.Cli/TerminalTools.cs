// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using RoyalTerminal.Multiplexing.Automation;
using RoyalTerminal.Terminal.Transport.Multiplexing;
namespace RoyalTerminal.Multiplexing.Cli;

internal sealed record KeyBatch(Guid SessionId, Guid BlockId, string[] Keys);
internal sealed record EventRead(Guid SessionId, Guid BlockId, long? After, int TimeoutMs, int Limit = 64);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(KeyBatch))][JsonSerializable(typeof(EventRead))][JsonSerializable(typeof(JsonElement))]
internal partial class ToolsJson : JsonSerializerContext;

internal static class TerminalTools
{
    internal static async Task<int> RunAsync(string verb, string[] args, string? url, string? token, HttpClient http, TextWriter output, TextWriter error, CancellationToken ct)
    {
        try
        {
            if (args.Length < 2 || string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Provide SESSION BLOCK and a token.");
            var api = new MultiplexingApiClient(http, new Uri(url ?? "http://127.0.0.1:7331"), token);
            var resolver = new CliTargetResolver(api);
            Guid session = await resolver.SessionAsync(args[0], ct), block = await resolver.BlockAsync(session, args[1], ct);
            if (verb == "send-key")
            {
                if (args.Length is < 3 or > 66) throw new ArgumentException("send-key SESSION BLOCK KEY [KEY …] (1–64 keys)");
                var batch = JsonSerializer.SerializeToElement(new KeyBatch(session, block, args[2..]), ToolsJson.Default.KeyBatch);
                var result = await api.CallAsync("terminal.send_keys", batch, ct);
                await output.WriteLineAsync(result.GetRawText()); return 0;
            }
            long? after = null; int? count = null;
            for (int i = 2; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length || !long.TryParse(args[i+1], NumberStyles.None, CultureInfo.InvariantCulture, out long n)) throw new ArgumentException("Use events SESSION BLOCK [--after N] [--count N].");
                if (args[i] == "--after" && after is null) after = n;
                else if (args[i] == "--count" && count is null && n is > 0 and <= int.MaxValue) count = (int)n;
                else throw new ArgumentException("Invalid event option or value.");
            }
            while (true)
            {
                var read = JsonSerializer.SerializeToElement(new EventRead(session, block, after, after.HasValue ? 15000 : 0), ToolsJson.Default.EventRead);
                var page = await api.CallAsync("terminal.events", read, ct);
                long next = page.GetProperty("nextSequence").GetInt64(), expected = after ?? next;
                foreach (var item in page.GetProperty("events").EnumerateArray())
                {
                    if (after is null || item.GetProperty("sequence").GetInt64() != checked(++expected)) throw new InvalidDataException("Incomplete metadata history.");
                    await output.WriteLineAsync(JsonSerializer.Serialize(item, ToolsJson.Default.JsonElement)); await output.FlushAsync(ct);
                    if (count.HasValue && --count == 0) return 0;
                }
                if (expected != next) throw new InvalidDataException("Inconsistent metadata cursor.");
                after = next; if (page.GetProperty("ended").GetBoolean()) return 0;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return 130; }
        catch (Exception ex) when (ex is ArgumentException or UriFormatException or MultiplexerAutomationException) { await error.WriteLineAsync(ex.Message); return 2; }
        catch (Exception ex) when (ex is IOException or HttpRequestException or JsonException or OperationCanceledException) { await error.WriteLineAsync(ex.Message); return 1; }
    }
}
