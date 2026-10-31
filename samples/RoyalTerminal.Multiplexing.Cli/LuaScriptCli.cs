// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Cli;

/// <summary>Runs trusted, explicitly selected local Lua scripts. No server endpoint accepts executable scripts.</summary>
internal static class LuaScriptCli
{
    private const int MaxFrame = 256 * 1024;
    private const int MaxReply = 4 * 1024 * 1024;
    public static async Task<int> RunAsync(string[] args, string? endpoint, string? token, HttpClient http, TextWriter diagnostics, CancellationToken cancellation)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            await diagnostics.WriteLineAsync("Usage: royalterminal-mux lua FILE [ARGS...] (requires Lua 5.1 and dkjson; ROYALTERMINAL_LUA51 selects the interpreter). Only run trusted local files.");
            return args.Length == 0 ? 2 : 0;
        }
        using CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        lifetime.CancelAfter(TimeSpan.FromMinutes(30));
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(token);
            string path = Path.GetFullPath(args[0]);
            if (!File.Exists(path)) throw new FileNotFoundException("Lua script was not found.", path);
            var client = new MultiplexingApiClient(http, new Uri(endpoint ?? "http://127.0.0.1:7331/", UriKind.Absolute), token);
            ProcessStartInfo start = new(Environment.GetEnvironmentVariable("ROYALTERMINAL_LUA51") ?? (OperatingSystem.IsWindows() ? "lua5.1.exe" : "lua5.1"))
            { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, StandardInputEncoding = new UTF8Encoding(false), CreateNoWindow = true };
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Lua", "workspace.lua"));
            start.ArgumentList.Add(path);
            foreach (string argument in args.Skip(1)) start.ArgumentList.Add(argument);
            start.Environment.Remove("ROYALTERMINAL_MUX_TOKEN"); start.Environment.Remove("LUA_INIT");
            // Diagnostics deliberately inherit stderr instead of competing with framed stdout.
            using Process process = Process.Start(start) ?? throw new IOException("Could not start Lua.");
            try
            {
                await PumpAsync(process.StandardOutput.BaseStream, process.StandardInput.BaseStream, client, lifetime.Token);
                await process.WaitForExitAsync(lifetime.Token); return process.ExitCode;
            }
            finally
            {
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) when (process.HasExited) { }
                    await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { await diagnostics.WriteLineAsync("Lua run cancelled; remote workspace processes were not stopped."); return 130; }
        catch (OperationCanceledException) { await diagnostics.WriteLineAsync("The local script deadline expired."); return 124; }
        catch (Exception ex) when (ex is IOException or Win32Exception or ArgumentException or FormatException or JsonException or HttpRequestException or TimeoutException or UnauthorizedAccessException)
        { await diagnostics.WriteLineAsync(ex.Message); return 1; }
    }

    internal static async Task PumpAsync(Stream input, Stream output, MultiplexingApiClient client, CancellationToken token)
    {
        var clock = Stopwatch.StartNew(); byte[] chunk = new byte[8192]; using MemoryStream line = new();
        int messages = 0;
        for (;;)
        {
            int n = await input.ReadAsync(chunk, token);
            if (n == 0) { if (line.Length != 0) throw new IOException("Truncated Lua RPC frame."); return; }
            for (int start = 0; start < n;)
            {
                int end = Array.IndexOf(chunk, (byte)'\n', start, n - start); int length = (end < 0 ? n : end) - start;
                if (line.Length + length > MaxFrame) throw new IOException("Lua RPC input exceeded the 256 KiB frame limit.");
                line.Write(chunk, start, length); if (end < 0) break;
                if (++messages > 100000) throw new IOException("Lua RPC operation limit exceeded.");
                using JsonDocument doc = JsonDocument.Parse(line.GetBuffer().AsMemory(0, checked((int)line.Length)));
                JsonElement frame = doc.RootElement; line.SetLength(0);
                if (frame.ValueKind != JsonValueKind.Object || !frame.TryGetProperty("op", out JsonElement op) || op.ValueKind != JsonValueKind.String) throw new IOException("Invalid Lua RPC envelope.");
                using MemoryStream response = new();
                using (Utf8JsonWriter w = new(response))
                {
                    w.WriteStartObject();
                    try
                    {
                        switch (op.GetString())
                        {
                            case "clock": w.WriteNumber("value", clock.Elapsed.TotalSeconds); break;
                            case "sleep":
                                if (!frame.TryGetProperty("seconds", out var sec) || sec.ValueKind != JsonValueKind.Number || !sec.TryGetDouble(out double seconds) || !double.IsFinite(seconds) || seconds < 0 || seconds > 1800) throw new ArgumentException("sleep expects 0..1800 seconds.");
                                await Task.Delay(TimeSpan.FromSeconds(seconds), token); w.WriteBoolean("value", true); break;
                            case "call":
                                if (!frame.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String || !frame.TryGetProperty("args", out var args) || args.ValueKind != JsonValueKind.Object) throw new ArgumentException("Expected method name and object arguments.");
                                JsonElement result = await client.CallAsync(method.GetString()!, args, token);
                                if (Encoding.UTF8.GetByteCount(result.GetRawText()) > MaxReply - 64) throw new IOException("Lua RPC result exceeded its budget.");
                                w.WritePropertyName("value"); result.WriteTo(w); break;
                            default: throw new ArgumentException("Unknown Lua RPC operation.");
                        }
                    }
                    catch (Exception e) when (e is HttpRequestException or IOException or ArgumentException or MultiplexingApiException) { w.WriteString("error", e.Message); }
                    w.WriteEndObject();
                }
                if (response.Length > MaxReply) throw new IOException("Encoded Lua RPC reply exceeded four MiB.");
                response.WriteByte((byte)'\n'); await output.WriteAsync(response.GetBuffer().AsMemory(0,(int)response.Length), token); await output.FlushAsync(token);
                start = end + 1;
            }
        }
    }
}
