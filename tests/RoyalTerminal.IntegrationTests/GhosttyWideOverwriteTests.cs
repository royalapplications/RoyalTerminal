// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.GhosttySharp;
using RoyalTerminal.IntegrationTests.TestInfrastructure;
using Xunit;

namespace RoyalTerminal.IntegrationTests;

public sealed class GhosttyWideOverwriteTests
{
    // The overlay skips only a doomed wide-cell fast-path scan. One-byte
    // writes force upstream's scalar path, including UTF-8 decoding. Compare
    // the full native snapshot, not just visible text, after every operation.
    [GhosttyNativeFact]
    public void BulkWideOverwritesMatchScalarStateAtMarginsAndComplexCells()
    {
        foreach (ushort columns in new ushort[] { 1, 2, 3, 8, 80 })
        foreach (bool clustered in new[] { false, true })
        foreach (bool linked in new[] { false, true })
        foreach (bool margins in new[] { false, true })
        {
            using GhosttyTerminal bulk = new(columns, 4);
            using GhosttyTerminal scalar = new(columns, 4);
            bulk.SetContinuationMaxBytes(0);
            scalar.SetContinuationMaxBytes(0);
            string setup = clustered ? "\u001b[?2027h" : "\u001b[?2027l";
            if (margins && columns >= 4) setup += $"\u001b[?69h\u001b[2;{columns - 1}s\u001b[2;3r";
            if (linked) setup += "\u001b]8;id=wide;https://example.test\u001b\\";
            string wide = new('界', columns + 3);
            string[] operations = [setup, wide, "\r" + wide, "\u001b[1;2H" + wide,
                "\u001b[31;1m\r中\u0301" + wide, "\u001b[32;3m\r" + wide,
                "\u001b]8;;\u001b\\\r" + wide, "\rabc" + wide + "Z",
                "\u001b[3b\u0301Q\r\n" + wide];
            foreach (string operation in operations)
            {
                byte[] input = Encoding.UTF8.GetBytes(operation);
                bulk.Write(input);
                for (int index = 0; index < input.Length; index++) scalar.Write(input.AsSpan(index, 1));
                Assert.True(GhosttySnapshot.Encode(scalar).AsSpan().SequenceEqual(GhosttySnapshot.Encode(bulk)),
                    $"columns={columns}, clustered={clustered}, linked={linked}, margins={margins}, operation={operation}");
            }
        }
    }
}
