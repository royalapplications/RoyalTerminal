// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Globalization;
using System.Text.Json;
using RoyalTerminal.Multiplexing.Automation;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Cli;

/// <summary>Process supervision commands. Observing an exit never acquires the terminal lease.</summary>
internal static class WorkspaceCli
{
    public static async Task<int> RunAsync(string[] args, string? token, string? endpoint, TextReader input,
        TextWriter output, TextWriter error, HttpClient http, CancellationToken cancellation,
        CliCurrentContext? context = null)
    {
        int start = args.Length >= 2 && args[0] == "--endpoint" ? 2 : 0;
        string? verb = args.ElementAtOrDefault(start);
        int flag = verb == "run" ? FindWaitOption(args, start) : -1;
        if (verb != "wait" && flag < 0)
        {
            int result = await MultiplexingCli.RunAsync(args, token, endpoint, input, output, error, http, cancellation, context);
            if (result == 0 && verb is (null or "--help" or "-h" or "help"))
                await output.WriteLineAsync("\n  wait SESSION BLOCK                 Wait without taking control; return the process exit code.\n  run SESSION --wait [OPTIONS] -- EXE ARGS  Create once, then wait; Ctrl+C detaches only.");
            return result;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Set ROYALTERMINAL_MUX_TOKEN to a host credential.");
            string address = start == 2 ? args[1] : endpoint ?? "http://127.0.0.1:7331/";
            MultiplexingApiClient client = new(http, new Uri(address, UriKind.Absolute), token);
            Guid sessionId, blockId;
            if (verb == "wait")
            {
                if (args.Length != start + 3) throw new ArgumentException("Usage: wait SESSION BLOCK");
                CliTargetResolver targets = new(client);
                sessionId = await targets.SessionAsync(args[start + 1], cancellation);
                blockId = await targets.BlockAsync(sessionId, args[start + 2], cancellation);
            }
            else
            {
                // Reuse the normal launch path, including selector and argv handling. Never retry the POST.
                string[] launch = [.. args[..flag], .. args[(flag + 1)..]];
                using StringWriter descriptor = new(CultureInfo.InvariantCulture);
                int status = await MultiplexingCli.RunAsync(launch, token, endpoint, input, descriptor, error, http, cancellation, context);
                if (status != 0) return status;
                using JsonDocument document = JsonDocument.Parse(descriptor.ToString());
                JsonElement block = document.RootElement;
                sessionId = RequiredId(block, "sessionId");
                blockId = RequiredId(block, "id");
                // Print the receipt before waiting, so a script interrupted by a network error can resume by ID.
                await error.WriteLineAsync($"Started {sessionId:D}/{blockId:D}. To resume: wait {sessionId:D} {blockId:D}");
                if (ReadExit(block) is int alreadyExited) return await ReportAsync(output, sessionId, blockId, alreadyExited);
            }

            // Resolve selectors only once. Labels and list order may change while the child runs.
            // No replay cursor, terminal parser, or extra input lease is needed. Exited blocks retain their status.
            TimeSpan delay = TimeSpan.FromMilliseconds(125);
            for (;;)
            {
                cancellation.ThrowIfCancellationRequested();
                JsonElement session = await client.SendAsync(HttpMethod.Get, $"api/sessions/{sessionId:D}", cancellationToken: cancellation);
                if (session.ValueKind != JsonValueKind.Object || !session.TryGetProperty("blocks", out JsonElement blocks) || blocks.ValueKind != JsonValueKind.Array)
                    throw new IOException("The host returned an invalid block list.");
                bool found = false;
                foreach (JsonElement block in blocks.EnumerateArray())
                {
                    if (RequiredId(block, "id") != blockId) continue;
                    found = true;
                    if (ReadExit(block) is int code) return await ReportAsync(output, sessionId, blockId, code);
                    break;
                }
                if (!found) throw new IOException("The block was removed before an exit status was observed.");
                await Task.Delay(delay, cancellation);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 1000));
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { await error.WriteLineAsync("Wait cancelled; no stop or retry was sent."); return 130; }
        catch (Exception ex) when (ex is ArgumentException or UriFormatException or MultiplexerAutomationException)
        { await error.WriteLineAsync(ex.Message); return 2; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or OperationCanceledException or MultiplexingApiException)
        { await error.WriteLineAsync(ex.Message); return 1; }
    }

    private static int FindWaitOption(string[] args, int start)
    {
        // Accept both `run --wait SESSION` and `run SESSION --wait`. Never consume a child argument or an option value.
        if (args.ElementAtOrDefault(start + 1) == "--wait") return start + 1;
        for (int i = start + 2; i < args.Length && args[i] != "--"; ++i)
        {
            if (args[i] is "--label" or "--cwd") { ++i; continue; }
            if (args[i] == "--wait") return i;
        }
        return -1;
    }

    private static Guid RequiredId(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out JsonElement id) && id.ValueKind == JsonValueKind.String && id.TryGetGuid(out Guid value) && value != Guid.Empty
            ? value : throw new IOException($"The host returned an invalid {name}.");

    private static int? ReadExit(JsonElement block)
    {
        if (!block.TryGetProperty("state", out JsonElement state) || state.ValueKind != JsonValueKind.String)
            throw new IOException("Missing process state.");
        return state.GetString() switch
        {
            "Starting" or "Running" => null,
            "Exited" when block.TryGetProperty("exitCode", out JsonElement code) && code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out int value) => value,
            _ => throw new IOException("No reliable process exit code was reported."),
        };
    }

    private static async Task<int> ReportAsync(TextWriter output, Guid session, Guid block, int code)
    {
        await output.WriteLineAsync(FormattableString.Invariant($$"""{"sessionId":"{{session:D}}","blockId":"{{block:D}}","exitCode":{{code}}}"""));
        return code;
    }
}
