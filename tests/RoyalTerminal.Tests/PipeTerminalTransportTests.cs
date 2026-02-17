// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Pipe;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class PipeTerminalTransportTests
{
    [Fact]
    public async Task StartAsync_StreamsCommandOutput_AndRaisesExit()
    {
        using PipeTerminalTransport transport = new();

        StringBuilder output = new();
        TaskCompletionSource<int> exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        transport.DataReceived += (data, length) =>
        {
            output.Append(Encoding.UTF8.GetString(data, 0, length));
        };
        transport.ProcessExited += exitCode => exited.TrySetResult(exitCode);

        await transport.StartAsync(CreateEchoOptions("RT_PIPE_OUTPUT_TEST"));

        int exitCode = await exited.Task.WaitAsync(TimeSpan.FromSeconds(8));
        await WaitForMarkerAsync(output, "RT_PIPE_OUTPUT_TEST", TimeSpan.FromSeconds(2));

        Assert.Equal(0, exitCode);
        Assert.Contains("RT_PIPE_OUTPUT_TEST", output.ToString(), StringComparison.Ordinal);

        await transport.StopAsync();
    }

    [Fact]
    public async Task MergeStdErrIntoStdOut_True_EmitsStandardError()
    {
        using PipeTerminalTransport transport = new();

        StringBuilder output = new();
        TaskCompletionSource<int> exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        transport.DataReceived += (data, length) =>
        {
            output.Append(Encoding.UTF8.GetString(data, 0, length));
        };
        transport.ProcessExited += exitCode => exited.TrySetResult(exitCode);

        await transport.StartAsync(CreateStdErrOptions("RT_PIPE_STDERR_TEST", mergeStdErrIntoStdOut: true));

        _ = await exited.Task.WaitAsync(TimeSpan.FromSeconds(8));
        await WaitForMarkerAsync(output, "RT_PIPE_STDERR_TEST", TimeSpan.FromSeconds(2));

        Assert.Contains("RT_PIPE_STDERR_TEST", output.ToString(), StringComparison.Ordinal);

        await transport.StopAsync();
    }

    [Fact]
    public async Task MergeStdErrIntoStdOut_False_DoesNotEmitStandardError()
    {
        using PipeTerminalTransport transport = new();

        StringBuilder output = new();
        TaskCompletionSource<int> exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        transport.DataReceived += (data, length) =>
        {
            output.Append(Encoding.UTF8.GetString(data, 0, length));
        };
        transport.ProcessExited += exitCode => exited.TrySetResult(exitCode);

        await transport.StartAsync(CreateStdErrOptions("RT_PIPE_STDERR_TEST", mergeStdErrIntoStdOut: false));

        _ = await exited.Task.WaitAsync(TimeSpan.FromSeconds(8));
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        Assert.DoesNotContain("RT_PIPE_STDERR_TEST", output.ToString(), StringComparison.Ordinal);

        await transport.StopAsync();
    }

    [Fact]
    public async Task ProcessExited_IsRaisedAfterOutputDrain()
    {
        using PipeTerminalTransport transport = new();

        const string marker = "RT_PIPE_EXIT_ORDER_TEST";
        StringBuilder output = new();
        TaskCompletionSource<int> exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool markerObservedBeforeExit = false;

        transport.DataReceived += (data, length) =>
        {
            output.Append(Encoding.UTF8.GetString(data, 0, length));
        };
        transport.ProcessExited += exitCode =>
        {
            markerObservedBeforeExit = output.ToString().Contains(marker, StringComparison.Ordinal);
            exited.TrySetResult(exitCode);
        };

        await transport.StartAsync(CreateEchoOptions(marker));

        _ = await exited.Task.WaitAsync(TimeSpan.FromSeconds(8));

        Assert.True(
            markerObservedBeforeExit,
            $"Expected marker '{marker}' to be observed before ProcessExited. Output: {output}");

        await transport.StopAsync();
    }

    private static PipeTransportOptions CreateEchoOptions(string marker)
    {
        TerminalCommandSpec command = OperatingSystem.IsWindows()
            ? new TerminalCommandSpec("cmd.exe", new[] { "/c", $"echo {marker}" })
            : new TerminalCommandSpec("/bin/sh", new[] { "-lc", $"printf '{marker}'" });

        return new PipeTransportOptions(
            Command: command,
            WorkingDirectory: null,
            Environment: null,
            MergeStdErrIntoStdOut: false,
            Dimensions: new TerminalSessionDimensions(80, 24, 640, 480));
    }

    private static PipeTransportOptions CreateStdErrOptions(string marker, bool mergeStdErrIntoStdOut)
    {
        TerminalCommandSpec command = OperatingSystem.IsWindows()
            ? new TerminalCommandSpec("cmd.exe", new[] { "/c", $"echo {marker} 1>&2" })
            : new TerminalCommandSpec("/bin/sh", new[] { "-lc", $"echo '{marker}' 1>&2" });

        return new PipeTransportOptions(
            Command: command,
            WorkingDirectory: null,
            Environment: null,
            MergeStdErrIntoStdOut: mergeStdErrIntoStdOut,
            Dimensions: new TerminalSessionDimensions(80, 24, 640, 480));
    }

    private static async Task WaitForMarkerAsync(StringBuilder output, string marker, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (output.ToString().Contains(marker, StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(25);
        }
    }
}
