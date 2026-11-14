// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace RoyalTerminal.Multiplexing.Inspection;

/// <summary>Only the fixed-width public PROC_PIDTBSDINFO ABI is read; no task ports, argv or environment.</summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class DarwinJobStat
{
    // The CI probe checks size, offsets and constants against the installed macOS SDK before running a real PTY.
    [StructLayout(LayoutKind.Explicit, Size=136)]
    private struct BsdInfo
    {
        [FieldOffset(4)] public uint Status;
        [FieldOffset(12)] public int Pid;
        [FieldOffset(16)] public int Parent;
        [FieldOffset(48)] public fixed byte Comm[16];
        [FieldOffset(64)] public fixed byte Name[32];
        [FieldOffset(100)] public int Group;
        [FieldOffset(108)] public uint Tty;
        [FieldOffset(112)] public int Foreground;
        [FieldOffset(120)] public ulong BirthSeconds;
        [FieldOffset(128)] public ulong BirthMicroseconds;
    }

    [LibraryImport("/usr/lib/libproc.dylib", EntryPoint="proc_pidinfo")]
    private static partial int GetInfo(int pid, int flavor, ulong arg, BsdInfo* buffer, int size);
    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint="getsid")]
    private static partial int GetSession(int pid);

    internal static UnixJobStat? Read(int pid)
    {
        BsdInfo info = default;
        if (GetInfo(pid,3,0,&info,sizeof(BsdInfo)) != sizeof(BsdInfo) || info.Pid != pid || info.BirthMicroseconds >= 1_000_000 || info.BirthSeconds > (ulong.MaxValue-999_999)/1_000_000) return null;
        int session = GetSession(pid);
        if (session <= 0) return null;
        ReadOnlySpan<byte> name = info.Name[0] == 0 ? new(info.Comm,16) : new(info.Name,32);
        int end = name.IndexOf((byte)0);
        return new(pid,Encoding.UTF8.GetString(end < 0 ? name : name[..end]),info.Status != 5,info.Parent,info.Group,session,
            info.Tty == uint.MaxValue ? 0 : unchecked((int)info.Tty),info.Foreground,info.BirthSeconds * 1_000_000 + info.BirthMicroseconds);
    }
}
