// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.PtyHarness;

// Process-wide signal dispositions and standard descriptors must never be changed
// in the xUnit host. Failures travel through a file because stdio may be closed.
internal static partial class UnixSpawnProbe
{
    internal static int Run(string[] args)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return 2;
        if (args.Length != 4) return 2;
        try
        {
            int value = int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
            switch (args[1])
            {
                case "ignored-signal":
                    CheckIgnoredSignal(value);
                    break;
                case "closed-stdio":
                    CheckClosedDescriptors(value);
                    break;
                default:
                    throw new ArgumentException("Unknown Unix spawn probe.");
            }
            File.WriteAllText(args[3], "PASS");
            return 0;
        }
        catch (Exception exception)
        {
            File.WriteAllText(args[3], exception.ToString());
            return 1;
        }
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static void CheckIgnoredSignal(int signal)
    {
        // Ghostty's pty.zig and node-pty reset inherited ignored dispositions.
        // A noninteractive POSIX shell preserves ignored signals across exec;
        // killing itself must terminate it before it can print SURVIVED.
        nint previous = Signal(signal, 1); // SIG_IGN
        Require(previous != -1, "Unable to ignore the test signal.");
        try
        {
            using UnixPty pty = new();
            using ManualResetEventSlim exited = new(false);
            StringBuilder output = new();
            pty.DataReceived += (bytes, count) => { lock (output) output.Append(Encoding.UTF8.GetString(bytes, 0, count)); };
            pty.ProcessExited += _ => exited.Set();
            bool suspend = signal == (OperatingSystem.IsMacOS() ? 18 : 20);
            // SIGTSTP's default action can be discarded for an orphaned group.
            // A caught signal still runs its trap; an inherited SIG_IGN cannot
            // be trapped by a noninteractive POSIX shell.
            string script = suspend
                ? "trap 'printf \"HANDLED\\n\"' TSTP; printf 'READY\\n'; kill -TSTP $$; printf 'DONE\\n'"
                : $"printf 'READY\\n'; kill -{signal} $$; printf 'SURVIVED\\n'";
            pty.Start("/bin/sh", arguments: ["-c", script]);
            Require(exited.Wait(TimeSpan.FromSeconds(8)), "Child did not terminate after its signal.");
            string transcript;
            lock (output) transcript = output.ToString();
            string expected = suspend ? "READY\nHANDLED\nDONE\n" : "READY\n";
            Require(transcript.Replace("\r\n", "\n", StringComparison.Ordinal) == expected,
                $"Child inherited an ignored signal or failed before signaling itself: {transcript}");
        }
        finally
        {
            Require(Signal(signal, previous) != -1, "Unable to restore the probe's signal disposition.");
        }
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static void CheckClosedDescriptors(int mask)
    {
        using UnixPty pty = new();
        using ManualResetEventSlim ready = new(false), complete = new(false), exited = new(false);
        StringBuilder output = new();
        pty.DataReceived += (bytes, count) =>
        {
            lock (output)
            {
                output.Append(Encoding.UTF8.GetString(bytes, 0, count));
                if (output.ToString().Contains("READY\r\n", StringComparison.Ordinal)) ready.Set();
                if (output.ToString().Contains("ERR:roundtrip\r\n", StringComparison.Ordinal)) complete.Set();
            }
        };
        pty.ProcessExited += _ => exited.Set();
        // Exercise every combination, including master/slave allocated as 0/1/2.
        // This mirrors node-pty's care around low-numbered PTY descriptors.
        for (int descriptor = 0; descriptor < 3; descriptor++)
            if ((mask & (1 << descriptor)) != 0)
                Require(Close(descriptor) == 0, $"Unable to close descriptor {descriptor}.");
        pty.Start("/bin/sh", arguments: ["-c",
            "stty -echo || exit 11; printf 'READY\\n'; IFS= read -r reply || exit 12; " +
            "printf 'OUT:%s\\n' \"$reply\"; printf 'ERR:%s\\n' \"$reply\" >&2; IFS= read -r finish"]);
        Require(ready.Wait(TimeSpan.FromSeconds(8)), "Child stdout/controlling terminal is unavailable.");
        pty.Write("roundtrip\n");
        bool received = complete.Wait(TimeSpan.FromSeconds(8));
        string transcript;
        lock (output) transcript = output.ToString();
        Require(received, $"Child standard-stream roundtrip did not complete: {transcript}");
        Require(transcript.Replace("\r\n", "\n", StringComparison.Ordinal) == "READY\nOUT:roundtrip\nERR:roundtrip\n",
            $"Child standard streams failed with closed-descriptor mask {mask}: {transcript}");
        pty.Write("finish\n");
        Require(exited.Wait(TimeSpan.FromSeconds(8)), "Child failed to exit after completing its roundtrip.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [LibraryImport("libc", EntryPoint = "signal", SetLastError = true)]
    private static partial nint Signal(int signal, nint handler);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int Close(int descriptor);
}
