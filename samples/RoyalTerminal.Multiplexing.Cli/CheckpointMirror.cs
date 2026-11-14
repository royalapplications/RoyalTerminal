// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Net.WebSockets;
using RoyalTerminal.Multiplexing.Automation;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Cli;

internal static class CheckpointMirror
{
    internal static async Task<int> RunAsync(string[] args, string? endpoint, string? token, HttpClient http,
        HttpMessageInvoker sockets, TextWriter output, TextWriter error, CancellationToken cancellation)
    {
        if (args.Length is < 2 or > 3 || (args.Length == 3 && args[2] != "--wait"))
        { await error.WriteLineAsync("Usage: mirror SESSION BLOCK [--wait]"); return 2; }
        if (string.IsNullOrWhiteSpace(token)) { await error.WriteLineAsync("Set ROYALTERMINAL_MUX_TOKEN."); return 2; }
        try
        {
            Uri url = new(endpoint ?? "http://127.0.0.1:7331/", UriKind.Absolute);
            CliTargetResolver resolver = new(new MultiplexingApiClient(http,url,token));
            Guid s = await resolver.SessionAsync(args[0],cancellation), b = await resolver.BlockAsync(s,args[1],cancellation);
            var options = new MultiplexingTransportOptions(url,token,s,b,new(80,24,0,0)) { Observer = true };
            for (int attempt = 0;; attempt++)
            {
                var checkpoint = await MultiplexingCheckpointClient.ReadAsync(http,options,cancellation);
                using var replica = ManagedTerminalSnapshot.Restore(checkpoint.Binary,new()
                {
                    DecodeLimits = new(MaximumPayloadBytes:4*1024*1024, MaximumTotalPayloadBytes:8*1024*1024,
                        MaximumCells:1024*1024, MaximumPages:4096, MaximumSuffixCodepointsPerPage:256*1024,
                        MaximumStringBytesPerRecord:256*1024, MaximumContinuationBytes:64*1024),
                    ScrollbackQuota = new(){MaximumBytes=16*1024*1024},
                    ProcessorOptions = new(){GlyphProtocolEnabled=false, SixelGraphicsEnabled=false, KittyGraphicsMaxApcBytes=0,
                        KittyGraphicsMaxImageBytes=0, KittyGraphicsStorageLimitBytes=0, ClipboardWriteLimitBytes=0, UnknownSequenceMaxBytes=0},
                });
                int? exit = checkpoint.ExitCode;
                if (args.Length == 3 && exit is null)
                {
                    // The snapshot is private until restoration and a successful ordered handshake both finish.
                    // There are no callbacks for VT replies, file reads, the clipboard, notifications or host effects.
                    await using var transport = new MultiplexingTerminalTransport(new SharedSocketConnector(sockets));
                    TaskCompletionSource<int?> stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    transport.DataReceived += (bytes,count) => replica.Processor.Process(bytes.AsSpan(0,count));
                    transport.AuthoritativeDimensionsChanged += d => replica.Processor.ResizeScreen(d.Columns,d.Rows,d.WidthPixels,d.HeightPixels);
                    transport.ProcessExited += code => stopped.TrySetResult(code);
                    transport.ConnectionLost += cause => { if (cause is null) stopped.TrySetResult(null); else stopped.TrySetException(cause); };
                    try { await transport.StartAsync(options with { AfterSequence=checkpoint.Sequence },cancellation); }
                    catch (MultiplexingProtocolException ex) when (ex.Code == "HistoryGap" && attempt < 2)
                    { continue; } // Read-only: no input or mutation is ever repeated.
                    exit = await stopped.Task.WaitAsync(cancellation);
                    if (exit is null) throw new IOException("The observer disconnected before the process reported exit.");
                    await transport.StopAsync(); // drain the reader before formatting or disposing its parser
                }
                if (!replica.Processor.TryExportSnapshot(TerminalSnapshotExportFormat.PlainText,default,out string text)) throw new IOException("Could not format the restored screen.");
                await output.WriteLineAsync(text);
                return args.Length == 3 ? exit!.Value : 0;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { await error.WriteLineAsync("Mirror detached; the remote process was not stopped."); return 130; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or HttpRequestException or WebSocketException or OperationCanceledException or ArgumentException or FormatException or MultiplexerAutomationException)
        { await error.WriteLineAsync(ex.Message); return 1; }
    }

    private sealed class SharedSocketConnector(HttpMessageInvoker invoker) : IMultiplexingSocketConnector
    {
        public async ValueTask<WebSocket> ConnectAsync(Uri endpoint,string token,CancellationToken cancellationToken)
        {
            var socket = new ClientWebSocket();
            try { socket.Options.SetRequestHeader("Authorization","Bearer "+token); await socket.ConnectAsync(endpoint,invoker,cancellationToken); return socket; }
            catch { socket.Dispose(); throw; }
        }
    }
}
