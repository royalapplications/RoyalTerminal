// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using System.Text.Json;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Cli;

internal static class MultiplexingCli
{
    private const string Usage = """
        RoyalTerminal experimental multiplexer CLI

        Usage: royalterminal-mux [--endpoint URL] COMMAND

          sessions                          List durable sessions
          new [LABEL]                       Create a session
          show SESSION                      Describe a session, windows, and blocks
          close SESSION                     Explicitly terminate a session
          run SESSION [OPTIONS] -- EXE ARGS  Start an independent terminal block
              --label TEXT --cwd PATH --pipe
          stop SESSION BLOCK                Explicitly close a terminal block
          send SESSION BLOCK TEXT           Send UTF-8 text to a block
          capture SESSION BLOCK             Read the retained block event log
          api list                          List discoverable automation methods
          api describe METHOD               Show one method's contract
          api call METHOD [JSON|-]          Call a method with JSON arguments

        Authentication: ROYALTERMINAL_MUX_TOKEN environment variable (never a URL or command argument).
        Endpoint: --endpoint, then ROYALTERMINAL_MUX_URL, then http://127.0.0.1:7331/.
        All management output is JSON. '-' reads bounded JSON from standard input.
        Closing a CLI invocation never terminates a session. Use close/stop explicitly.
        """;

    public static async Task<int> RunAsync(string[] args, string? accessToken, string? configuredEndpoint,
        TextReader input, TextWriter output, TextWriter error, HttpClient http, CancellationToken cancellationToken)
    {
        try
        {
            int index = 0;
            string endpoint = configuredEndpoint ?? "http://127.0.0.1:7331/";
            if (args.Length > 0 && args[0] == "--endpoint")
            {
                Require(args, 2);
                endpoint = args[1];
                index = 2;
            }

            string[] command = args[index..];
            if (command.Length == 0 || command[0] is "help" or "--help" or "-h")
            {
                await output.WriteLineAsync(Usage);
                return 0;
            }

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new ArgumentException("Set ROYALTERMINAL_MUX_TOKEN to the host's bearer credential.");
            }

            MultiplexingApiClient client = new(http, new Uri(endpoint, UriKind.Absolute), accessToken);
            JsonElement result = command[0] switch
            {
                "sessions" => await ListAsync(client, command, cancellationToken),
                "new" => await NewAsync(client, command, cancellationToken),
                "show" => await SessionAsync(client, command, HttpMethod.Get, cancellationToken),
                "close" => await SessionAsync(client, command, HttpMethod.Delete, cancellationToken),
                "stop" => await BlockAsync(client, command, HttpMethod.Delete, "", cancellationToken),
                "capture" => await BlockAsync(client, command, HttpMethod.Get, "/capture", cancellationToken),
                "send" => await SendAsync(client, command, cancellationToken),
                "run" => await RunBlockAsync(client, command, cancellationToken),
                "api" => await ApiAsync(client, command, input, cancellationToken),
                _ => throw new ArgumentException($"Unknown command '{command[0]}'. Run --help for usage."),
            };
            await output.WriteLineAsync(Pretty(result));
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await error.WriteLineAsync("Cancelled.");
            return 130;
        }
        catch (ArgumentException exception)
        {
            await error.WriteLineAsync(exception.Message);
            return 2;
        }
        catch (UriFormatException exception)
        {
            await error.WriteLineAsync(exception.Message);
            return 2;
        }
        catch (JsonException exception)
        {
            await error.WriteLineAsync("Invalid JSON: " + exception.Message);
            return 2;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException)
        {
            await error.WriteLineAsync(exception.Message);
            return 1;
        }
    }

    private static async ValueTask<JsonElement> ListAsync(MultiplexingApiClient client, string[] args, CancellationToken token)
    {
        Require(args, 1, 1);
        return await client.SendAsync(HttpMethod.Get, "api/sessions", cancellationToken: token);
    }

    private static async ValueTask<JsonElement> NewAsync(MultiplexingApiClient client, string[] args, CancellationToken token)
    {
        Require(args, 1, 2);
        JsonElement body = BuildJson(writer => writer.WriteString("label", args.Length == 2 ? args[1] : "New session"));
        return await client.SendAsync(HttpMethod.Post, "api/sessions", body, token);
    }

    private static async ValueTask<JsonElement> SessionAsync(MultiplexingApiClient client, string[] args,
        HttpMethod method, CancellationToken token)
    {
        Require(args, 2, 2);
        Guid session = Id(args[1]);
        return await client.SendAsync(method, $"api/sessions/{session:D}", cancellationToken: token);
    }

    private static async ValueTask<JsonElement> BlockAsync(MultiplexingApiClient client, string[] args,
        HttpMethod method, string suffix, CancellationToken token)
    {
        Require(args, 3, 3);
        Guid session = Id(args[1]);
        Guid block = Id(args[2]);
        return await client.SendAsync(method, $"api/sessions/{session:D}/blocks/{block:D}{suffix}", cancellationToken: token);
    }

    private static async ValueTask<JsonElement> SendAsync(MultiplexingApiClient client, string[] args, CancellationToken token)
    {
        Require(args, 4, 4);
        Guid session = Id(args[1]);
        Guid block = Id(args[2]);
        JsonElement body = BuildJson(writer => writer.WriteString("data", Convert.ToBase64String(Encoding.UTF8.GetBytes(args[3]))));
        return await client.SendAsync(HttpMethod.Post, $"api/sessions/{session:D}/blocks/{block:D}/input", body, token);
    }

    private static async ValueTask<JsonElement> RunBlockAsync(MultiplexingApiClient client, string[] args, CancellationToken token)
    {
        Require(args, 4);
        Guid session = Id(args[1]);
        string label = "Terminal";
        string? directory = null;
        string transport = "pty";
        int index = 2;
        while (index < args.Length && args[index] != "--")
        {
            switch (args[index])
            {
                case "--pipe": transport = "pipe"; index++; break;
                case "--label":
                    if (++index >= args.Length) { throw new ArgumentException("--label requires a value."); }
                    label = args[index++]; break;
                case "--cwd":
                    if (++index >= args.Length) { throw new ArgumentException("--cwd requires a path."); }
                    directory = args[index++]; break;
                default: throw new ArgumentException("Expected --label, --cwd, --pipe, or -- before the executable.");
            }
        }

        if (index >= args.Length - 1 || args[index++] != "--")
        {
            throw new ArgumentException("run requires -- followed by an executable and optional arguments.");
        }

        string executable = args[index++];
        string[] arguments = args[index..];
        JsonElement body = BuildJson(writer =>
        {
            writer.WriteString("label", label);
            writer.WriteString("transport", transport);
            if (directory is not null) { writer.WriteString("workingDirectory", directory); }
            writer.WriteStartObject("command");
            writer.WriteString("fileName", executable);
            writer.WriteStartArray("arguments");
            foreach (string argument in arguments) { writer.WriteStringValue(argument); }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteStartObject("dimensions");
            writer.WriteNumber("columns", 120);
            writer.WriteNumber("rows", 32);
            writer.WriteNumber("widthPixels", 0);
            writer.WriteNumber("heightPixels", 0);
            writer.WriteEndObject();
        });
        return await client.SendAsync(HttpMethod.Post, $"api/sessions/{session:D}/blocks", body, token);
    }

    private static async ValueTask<JsonElement> ApiAsync(MultiplexingApiClient client, string[] args,
        TextReader input, CancellationToken token)
    {
        Require(args, 2);
        if (args[1] == "list")
        {
            Require(args, 2, 2);
            return await client.GetMethodsAsync(token);
        }

        if (args[1] == "describe")
        {
            Require(args, 3, 3);
            JsonElement catalog = await client.GetMethodsAsync(token);
            JsonElement methods;
            if (catalog.ValueKind == JsonValueKind.Array) { methods = catalog; }
            else if (catalog.ValueKind == JsonValueKind.Object && catalog.TryGetProperty("methods", out JsonElement listed)
                && listed.ValueKind == JsonValueKind.Array) { methods = listed; }
            else { throw new IOException("The host returned an invalid API method catalog."); }
            foreach (JsonElement method in methods.EnumerateArray())
            {
                if (method.TryGetProperty("name", out JsonElement name) && name.GetString() == args[2])
                {
                    return method.Clone();
                }
            }

            throw new ArgumentException($"No method named '{args[2]}' is advertised by this host.");
        }

        if (args[1] == "call")
        {
            Require(args, 3, 4);
            string json = args.Length == 3 ? "{}" : args[3] == "-" ? await ReadBoundedAsync(input, token) : args[3];
            if (json.Length > 1024 * 1024) { throw new ArgumentException("JSON arguments exceed one MiB."); }
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("API arguments must be a JSON object.");
            }

            return await client.CallAsync(args[2], document.RootElement, token);
        }

        throw new ArgumentException("Expected api list, api describe, or api call.");
    }

    private static async Task<string> ReadBoundedAsync(TextReader input, CancellationToken token)
    {
        StringBuilder content = new();
        char[] buffer = new char[4096];
        while (true)
        {
            int read = await input.ReadAsync(buffer.AsMemory(), token);
            if (read == 0) { return content.ToString(); }
            if (content.Length + read > 1024 * 1024) { throw new ArgumentException("JSON arguments exceed one MiB."); }
            content.Append(buffer, 0, read);
        }
    }

    private static JsonElement BuildJson(Action<Utf8JsonWriter> writeProperties)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            writeProperties(writer);
            writer.WriteEndObject();
        }
        using JsonDocument document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    private static string Pretty(JsonElement value)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true })) { value.WriteTo(writer); }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static Guid Id(string value)
        => Guid.TryParse(value, out Guid id) && id != Guid.Empty ? id : throw new ArgumentException("A nonempty session or block GUID is required.");

    private static void Require(string[] args, int minimum, int? maximum = null)
    {
        if (args.Length < minimum || (maximum is { } max && args.Length > max))
        {
            throw new ArgumentException("Incorrect command arguments. Run --help for usage.");
        }
    }
}
