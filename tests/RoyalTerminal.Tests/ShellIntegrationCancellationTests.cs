// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Terminal;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed class ShellIntegrationCancellationTests
{
    [Theory]
    [InlineData("\u001b]133;A\u0018\a", 0)]
    [InlineData("\u001b]133;A\u001a\a", 0)]
    [InlineData("\u001b]133;A\u0001\a", 1)]
    [InlineData("\u001b]133;A\u001b[H", 1)]
    [InlineData("\u001b]+133;A\a", 0)]
    [InlineData("\u001b]0133;A\a", 0)]
    [InlineData("\u001b] 133;A\a", 0)]
    public void LegacyCommandHistoryObserverFollowsNativeOscFraming(string input, int expected)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(input);
        foreach (bool native in new[] { false, true })
        {
            if (native && !GhosttyVtProcessor.IsAvailable()) continue;
            for (int split = 0; split <= bytes.Length; split++)
            {
                using IVtProcessor processor = native ? new GhosttyVtProcessor(new(8, 4)) : new BasicVtProcessor(new(8, 4));
                List<TerminalShellIntegrationEvent> events = [];
                ((ITerminalShellIntegrationEventSource)processor).ShellIntegrationEventReceived += (_, e) => events.Add(e.Value);
                processor.Process(bytes.AsSpan(0, split));
                processor.Process(bytes.AsSpan(split));
                Assert.Equal(expected, events.Count);
                processor.Process("\u001b]133;A\a"u8);
                Assert.Equal(expected + 1, events.Count);
                Assert.Equal(TerminalShellIntegrationEventKind.FreshLineNewPrompt, events[^1].Kind);
            }
        }
    }
}
