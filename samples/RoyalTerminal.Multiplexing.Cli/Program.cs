// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Multiplexing.Cli;

using CancellationTokenSource cancellation = new();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
string? token = Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_TOKEN");
string? endpoint = Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_URL");
string? unixSocket = Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_SOCKET");
if (args.ElementAtOrDefault(0) == "--socket")
{
    if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1])) { Console.Error.WriteLine("--socket requires an absolute Unix socket path."); return 2; }
    unixSocket = args[1]; args = args[2..];
    if (args.ElementAtOrDefault(0) is "--endpoint" or "--socket") { Console.Error.WriteLine("Specify one transport, not multiple endpoints."); return 2; }
}
else if (args.ElementAtOrDefault(0) == "--endpoint") unixSocket = null;
HttpMessageHandler handler;
try { handler = WorkspaceConnection.CreateHandler(unixSocket); }
catch (Exception error) when (error is ArgumentException or PlatformNotSupportedException) { Console.Error.WriteLine(error.Message); return 2; }
using HttpClient http = new(handler) { Timeout = TimeSpan.FromSeconds(130) };
if (!string.IsNullOrEmpty(unixSocket)) endpoint = "http://localhost/";
int command = args.Length >= 2 && args[0] == "--endpoint" ? 2 : 0;
if (args.ElementAtOrDefault(command) == "lua")
    return await LuaScriptCli.RunAsync(args[(command + 1)..], command == 2 ? args[1] : endpoint, token, http, Console.Error, cancellation.Token);
if (args.ElementAtOrDefault(command) == "mirror")
{
    // WebSocket handshakes use the same handler/Unix socket as HTTP, without HttpClient content buffering.
    using HttpMessageInvoker invoker = new(handler, disposeHandler:false);
    return await CheckpointMirror.RunAsync(args[(command + 1)..], command == 2 ? args[1] : endpoint, token, http, invoker, Console.Out, Console.Error, cancellation.Token);
}
int result = await WorkspaceCli.RunAsync(args, token, endpoint, Console.In, Console.Out, Console.Error, http, cancellation.Token,
    new CliCurrentContext(Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_SESSION"), Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_BLOCK"), Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_WINDOW")));
if (args.ElementAtOrDefault(command) is null or "-h" or "--help" or "help")
{
    Console.WriteLine("  lua FILE [ARGS...]                 Run trusted local Lua 5.1 with the authenticated workspace API.");
    Console.WriteLine("  --socket /ABSOLUTE/PATH COMMAND    Use HTTP over a private Unix socket; no TCP fallback.");
    Console.WriteLine("  mirror SESSION BLOCK [--wait]      Restore a managed checkpoint; optionally follow as observer through process exit.");
}
return result;
