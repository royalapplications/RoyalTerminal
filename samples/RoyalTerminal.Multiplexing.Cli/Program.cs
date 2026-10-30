// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Multiplexing.Cli;

using CancellationTokenSource cancellation = new();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
// The host permits action deadlines up to 120 seconds; allow a small transport margin.
using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(130) };
return await MultiplexingCli.RunAsync(args, Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_TOKEN"),
    Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_URL"), Console.In, Console.Out, Console.Error,
    http, cancellation.Token, new CliCurrentContext(
        Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_SESSION"),
        Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_BLOCK"),
        Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_WINDOW")));
