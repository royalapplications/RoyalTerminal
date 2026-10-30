// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using RoyalTerminal.Multiplexing.Cli;

using CancellationTokenSource cancellation = new();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};
using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
return await MultiplexingCli.RunAsync(args, Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_TOKEN"),
    Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_URL"), Console.In, Console.Out, Console.Error,
    http, cancellation.Token);
