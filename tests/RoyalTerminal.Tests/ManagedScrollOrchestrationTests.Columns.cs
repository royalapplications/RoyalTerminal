// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Snapshots;
using Xunit;

namespace RoyalTerminal.Tests;

public sealed partial class ManagedScrollOrchestrationTests
{
    // Ghostty insertColumns/deleteColumns call Screen.cursorAbsolute for every
    // row and restore the original pin. Crossing a page must migrate the pen
    // and cursor hyperlink even though the visible cursor does not move.
    [Theory]
    [InlineData("\u001b[2'}", false)]
    [InlineData("\u001b[2'~", false)]
    [InlineData("\u001b6", false)]
    [InlineData("\u001b9", true)]
    public void ColumnControlsVisitEachPageAndRestoreCursorMetadata(string command, bool rightEdge)
    {
        RequireNative();
        foreach (bool alternate in new[] { false, true })
        foreach (int pageRows in new[] { 1, 2 })
        foreach (bool margins in new[] { false, true })
        foreach (bool explicitLink in new[] { false, true })
        foreach (bool held in new[] { false, true })
        {
            byte[] snapshot = Snapshot(ContentRows(), pageRows, alternate);
            using ManagedTerminalSnapshot managed = ManagedTerminalSnapshot.Restore(snapshot);
            using GhosttyTerminal native = GhosttySnapshot.Decode(snapshot);
            int column = rightEdge ? (margins ? 7 : 8) : (margins ? 2 : 1);
            string setup = (margins ? "\u001b[?69h\u001b[2;7s\u001b[2;4r" : "") +
                $"\u001b[3;{column}H\u001b[44m\u001b]8;{(explicitLink ? "id=column" : "")};u\a";
            string edit = held ? "\u001b[?2026h" + command + "\u001b[?2026l" : command;
            foreach (string input in new[] { setup, edit, "X\u001b]8;;\a" })
            {
                byte[] bytes = Encoding.UTF8.GetBytes(input);
                native.Write(bytes);
                managed.Processor.Process(bytes);
                AssertNative(native, managed, $"column edit {command}, alternate={alternate}, pageRows={pageRows}, margins={margins}, explicit={explicitLink}, held={held}");
            }
        }
    }
}
