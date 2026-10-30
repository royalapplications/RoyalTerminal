// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Avalonia.Rendering;
using RoyalTerminal.Terminal;

internal static class ProgramStatusBenchmark
{
    internal static void Run()
    {
        if (!GhosttyVtProcessor.IsAvailable()) throw new InvalidOperationException("Native comparison requires libghostty-vt.");
        PerformanceMeasurement.Header();
        foreach (int count in new[] { 1, 64, 256 })
        {
            byte[][] reports = new byte[count * 2][];
            for (int i = 0; i < reports.Length; i++)
                reports[i] = Encoding.ASCII.GetBytes($"\u001b]7501;state=working:id=job{i % count}:app=build:title=QnVpbGQ=:msg=UnVubmluZw==:progress={(i < count ? 42 : 43)}\a");
            foreach (bool native in new[] { false, true })
            {
                using IVtProcessor processor = native ? new GhosttyVtProcessor(new TerminalScreen(80, 24, 0))
                    : new BasicVtProcessor(new TerminalScreen(80, 24, 0), new() { ContinuationMaxBytes = 0 });
                ITerminalProgramStatusSource status = (ITerminalProgramStatusSource)processor;
                long changes = 0;
                status.ProgramStatusChangedCallback = () => changes++;
                int index = 0;
                PerformanceMeasurement.Run($"status-{count}", native ? "native-adapter" : "managed", 20_000,
                    () => { processor.Process(reports[index]); index = (index + 1) % reports.Length; },
                    () => changes + status.ProgramStatuses.Count);
                if (status.ProgramStatuses.Count != count) throw new InvalidOperationException("Status capacity mismatch.");
            }

            TerminalProgramStatusStore store = new();
            store.Apply(new(TerminalProgramStatusState.Idle, App: "build"));
            for (int i = 1; i < count; i++) store.Apply(new(TerminalProgramStatusState.Working, $"jobs/{i}"));
            long characters = 0;
            PerformanceMeasurement.Run($"status-inheritance-{count}", "shared-store", 1000,
                () => { for (int i = 0; i < store.Records.Count; i++) characters += store.GetApplication(store.Records[i].Id).Length; },
                () => characters);
        }
    }
}
