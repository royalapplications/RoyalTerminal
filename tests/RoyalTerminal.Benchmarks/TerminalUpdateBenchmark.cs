// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;

// Uses APIs present before the October update so the identical harness can be
// compiled against both pins. Direct native and adapter costs are kept separate.
internal static class TerminalUpdateBenchmark
{
    internal static void Run()
    {
        if (!GhosttyVtProcessor.IsAvailable()) throw new InvalidOperationException("Native comparison requires libghostty-vt.");
        PerformanceMeasurement.Header();
        foreach ((string name, string text) in new[]
        {
            ("ascii", new string('q', 79) + "\r"),
            ("unicode", new string('λ', 79) + "\r"),
            ("wide", new string('界', 39) + "\r"),
            ("styled", "\u001b[31;1m" + new string('q', 79) + "\r\u001b[0m"),
            ("title", "\u001b]2;build status\a"),
            ("prompt", "\u001b]133;A\a\u001b]133;B\a\u001b]133;C;cmdline=echo\\ hi\a\u001b]133;D;0\a"),
            ("soft-reset", "\u001b[31;1m\u001b[!p"),
        })
        {
            byte[] input = Encoding.UTF8.GetBytes(text);
            foreach (bool native in new[] { false, true })
            {
                TerminalScreen screen = new(80, 24, 0);
                using IVtProcessor processor = native ? new GhosttyVtProcessor(screen)
                    : new BasicVtProcessor(screen, new() { ContinuationMaxBytes = 0 });
                PerformanceMeasurement.Run(name, native ? "native-adapter" : "managed", 25_000,
                    () => processor.Process(input), () => processor.CursorCol + processor.CursorRow * 80L);
            }
            using GhosttyTerminal terminal = new(80, 24, 0);
            terminal.SetContinuationMaxBytes(0);
            PerformanceMeasurement.Run(name, "native-core", 25_000,
                () => terminal.Write(input), () => (long)terminal.GetTotalRows());
        }
    }
}
