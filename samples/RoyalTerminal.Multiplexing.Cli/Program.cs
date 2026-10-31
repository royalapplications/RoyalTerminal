// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Multiplexing.Cli;

using CancellationTokenSource cancellation = new();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
using HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(130) };
string? token = Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_TOKEN");
string? endpoint = Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_URL");
int command = args.Length >= 2 && args[0] == "--endpoint" ? 2 : 0;
if (args.ElementAtOrDefault(command) == "lua")
    return await LuaScriptCli.RunAsync(args[(command + 1)..], command == 2 ? args[1] : endpoint, token, http, Console.Error, cancellation.Token);
int result = await WorkspaceCli.RunAsync(args, token, endpoint, Console.In, Console.Out, Console.Error, http, cancellation.Token,
    new CliCurrentContext(Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_SESSION"), Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_BLOCK"), Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_WINDOW")));
if (args.ElementAtOrDefault(command) is null or "-h" or "--help" or "help")
    Console.WriteLine("  lua FILE [ARGS...]                 Run trusted local Lua 5.1 with the authenticated workspace API.");
return result;
