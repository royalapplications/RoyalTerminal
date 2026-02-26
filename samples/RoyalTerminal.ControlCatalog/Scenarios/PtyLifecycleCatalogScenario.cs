// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.ControlCatalog;

internal sealed class PtyLifecycleCatalogScenario : ICatalogScenario
{
    public string Title => "PTY lifecycle catalog";

    public string Description => "Create/start/write/read/resize/stop PTY smoke.";

    public bool IncludeInFullSweep => true;

    public CatalogScenarioResult Execute()
    {
        List<string> lines = [];
        bool success = true;
        string outputTail = string.Empty;

        try
        {
            using IPty pty = new DefaultPtyFactory().Create();
            using ManualResetEventSlim tokenSeenEvent = new(initialState: false);
            using ManualResetEventSlim exitSeenEvent = new(initialState: false);
            StringBuilder outputBuffer = new();
            int exitCode = int.MinValue;

            pty.DataReceived += (buffer, count) =>
            {
                string chunk = Encoding.UTF8.GetString(buffer, 0, count);
                lock (outputBuffer)
                {
                    outputBuffer.Append(chunk);
                    string snapshot = outputBuffer.ToString();
                    if (snapshot.Contains(CatalogConstants.PtySmokeToken, StringComparison.Ordinal))
                    {
                        tokenSeenEvent.Set();
                    }

                    outputTail = snapshot.Length <= 300
                        ? snapshot
                        : snapshot[^300..];
                }
            };

            pty.ProcessExited += code =>
            {
                exitCode = code;
                exitSeenEvent.Set();
            };

            Dictionary<string, string> environment = new(StringComparer.Ordinal)
            {
                ["RT_CONTROL_CATALOG"] = "1",
            };

            pty.Start(
                shell: null,
                columns: 80,
                rows: 24,
                workingDirectory: null,
                environment: environment,
                arguments: null);

            lines.Add($"PTY started: running={pty.IsRunning} pid={pty.ChildPid}");

            pty.Resize(columns: 100, rows: 30, widthPixels: 1000, heightPixels: 600);
            lines.Add("PTY resized: 100x30 (1000x600 px).");

            pty.Write($"echo {CatalogConstants.PtySmokeToken}\r");
            pty.Write("exit\r");

            bool tokenObserved = tokenSeenEvent.Wait(TimeSpan.FromSeconds(8));
            bool exitObserved = exitSeenEvent.Wait(TimeSpan.FromSeconds(8));

            lines.Add($"Token observed: {tokenObserved}");
            lines.Add($"Exit observed: {exitObserved} code={exitCode}");

            if (!tokenObserved)
            {
                success = false;
                lines.Add($"Output tail: {ControlTextFormatter.FormatControl(outputTail)}");
            }

            if (!exitObserved)
            {
                success = false;
            }

            pty.Stop();
            lines.Add("PTY stopped.");
        }
        catch (Exception ex)
        {
            success = false;
            lines.Add($"PTY lifecycle failed: {ex.GetType().Name}: {ex.Message}");
        }

        return new CatalogScenarioResult(Title, success, lines);
    }
}
