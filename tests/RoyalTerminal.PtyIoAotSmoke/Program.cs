// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;
using RoyalTerminal.Terminal;

const string marker = "__ROYALTERMINAL_PTY_AOT_SMOKE__";

using IPty pty = new DefaultPtyFactory().Create();
using ManualResetEventSlim sawMarker = new(false);

byte[] markerBytes = Encoding.ASCII.GetBytes(marker);
byte[] tail = new byte[markerBytes.Length - 1];
int tailLength = 0;

pty.DataReceived += (data, length) =>
{
    if (ContainsMarker(data.AsSpan(0, length), markerBytes, tail, ref tailLength))
    {
        sawMarker.Set();
    }
};

if (OperatingSystem.IsWindows())
{
    pty.Start(columns: 80, rows: 24, workingDirectory: Environment.CurrentDirectory);
    pty.Write("echo " + marker + "\r\nexit\r\n");
}
else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
{
    pty.Start(shell: "/bin/sh", columns: 80, rows: 24, workingDirectory: Environment.CurrentDirectory);
    pty.Write("printf '" + marker + "\\n'\nexit\n");
}
else
{
    Console.Error.WriteLine("PTY AOT smoke is supported on Windows, Linux, and macOS.");
    return 3;
}

bool passed = sawMarker.Wait(TimeSpan.FromSeconds(10));
pty.Stop();

if (!passed)
{
    Console.Error.WriteLine("PTY AOT smoke did not observe the marker.");
    return 2;
}

Console.WriteLine("PTY AOT smoke passed.");
return 0;

static bool ContainsMarker(
    ReadOnlySpan<byte> data,
    ReadOnlySpan<byte> marker,
    byte[] tail,
    ref int tailLength)
{
    if (data.IndexOf(marker) >= 0)
    {
        UpdateTail(data, tail, ref tailLength);
        return true;
    }

    Span<byte> combined = stackalloc byte[tail.Length + Math.Min(data.Length, marker.Length)];
    tail.AsSpan(0, tailLength).CopyTo(combined);
    int copyLength = Math.Min(data.Length, marker.Length);
    data[..copyLength].CopyTo(combined[tailLength..]);
    if (combined[..(tailLength + copyLength)].IndexOf(marker) >= 0)
    {
        UpdateTail(data, tail, ref tailLength);
        return true;
    }

    UpdateTail(data, tail, ref tailLength);
    return false;
}

static void UpdateTail(ReadOnlySpan<byte> data, byte[] tail, ref int tailLength)
{
    int copyLength = Math.Min(data.Length, tail.Length);
    data[^copyLength..].CopyTo(tail.AsSpan(0, copyLength));
    tailLength = copyLength;
}
