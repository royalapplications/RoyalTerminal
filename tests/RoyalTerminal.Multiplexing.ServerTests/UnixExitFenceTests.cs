// Copyright (c) Royal Apps. Licensed under the MIT license.
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Multiplexing.ServerTests;

public sealed class UnixExitFenceTests
{
    [Theory]
    [InlineData(false, 23)]
    [InlineData(true, 143)]
    public async Task SlaveCanCloseBeforeItsOwnerBecomesWaitable(bool signal, int expected)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        var dir = Directory.CreateTempSubdirectory("royal-exit-fence-");
        try
        {
            using var pty = new UnixPty();
            var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            int events = 0;
            pty.ProcessExited += code => { Interlocked.Increment(ref events); exited.TrySetResult(code); };
            // The child closes every descriptor connected to the slave, then waits on a
            // separate filesystem gate. PTY HUP cannot be used to predict its exit status.
            const string child = "import os,signal,sys,time,pathlib\nd=pathlib.Path(sys.argv[1]);signal.signal(signal.SIGHUP,signal.SIG_IGN);os.closerange(0,1024);(d/'ready').touch()\nwhile not (d/'go').exists(): time.sleep(.01)\nif sys.argv[2]=='signal': os.kill(os.getpid(),signal.SIGTERM)\nos._exit(23)";
            pty.Start("/usr/bin/python3", arguments: ["-c", child, dir.FullName, signal ? "signal" : "exit"]);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            while (!File.Exists(Path.Combine(dir.FullName, "ready"))) await Task.Delay(10, timeout.Token);
            await Task.Delay(80, timeout.Token);
            Assert.False(exited.Task.IsCompleted);
            File.WriteAllText(Path.Combine(dir.FullName, "go"), string.Empty);
            Assert.Equal(expected, await exited.Task.WaitAsync(timeout.Token));
            Assert.Equal(1, Volatile.Read(ref events));
        }
        finally { dir.Delete(recursive: true); }
    }
}
