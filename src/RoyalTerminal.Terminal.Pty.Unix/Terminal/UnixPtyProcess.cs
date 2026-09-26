// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RoyalTerminal.Terminal;

/// <summary>Creates a PTY session without returning into managed code after fork.</summary>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal static unsafe partial class UnixPtyProcess
{
    internal static (int Pid, int Master) Start(string executable, int columns, int rows,
        string? workingDirectory, IReadOnlyDictionary<string, string>? environment, IReadOnlyList<string>? arguments)
    {
        using UnixPtyEnvironment env = new(environment);
        using UnixPtyArguments argv = new(executable, arguments);
        bool darwin = OperatingSystem.IsMacOS();
        // Native opaque storage, aligned to 8 bytes: Darwin uses pointers;
        // supported Linux LP64 ABIs use <=336-byte attrs and <=80-byte actions.
        ulong* attributes = stackalloc ulong[64];
        ulong* actions = stackalloc ulong[16];
        ulong* signals = stackalloc ulong[16];
        byte* tty = stackalloc byte[4096];
        WinSize size = new() { Columns = checked((ushort)columns), Rows = checked((ushort)rows) };
        int master = -1, slave = -1;
        bool attrsReady = false, actionsReady = false;
        try
        {
            int opened = darwin ? OpenPtyMac(out master, out slave, tty, null, &size)
                : OpenPtyLinux(out master, out slave, tty, null, &size);
            if (opened != 0) throw new IOException($"openpty failed: {Marshal.GetLastPInvokeError()}");
            nuint closeOnExec = darwin ? 0x20006601u : 0x5451u; // FIOCLEX, no variadic argument
            if (Ioctl(master, closeOnExec) != 0 || Ioctl(slave, closeOnExec) != 0)
                throw new IOException($"PTY close-on-exec setup failed: {Marshal.GetLastPInvokeError()}");

            Check(AttrInit(attributes), "spawn attributes");
            attrsReady = true;
            Check(ActionsInit(actions), "spawn actions");
            actionsReady = true;
            Check(SigFill(signals), "signal defaults");
            Check(SetSignalDefault(attributes, signals), "spawn signal defaults");
            Check(SigEmpty(signals), "signal mask");
            Check(SetSignalMask(attributes, signals), "spawn signal mask");
            short flags = (short)(4 | 8 | (darwin ? 0x0400 | 0x4000 : 0x0080));
            Check(SetFlags(attributes, flags), "spawn session flags");
            if (darwin)
            {
                int result = SpawnMac(out int child, executable, executable, arguments, master, slave,
                    attributes, env.Pointer, workingDirectory);
                Check(result, $"launch '{executable}'");
                int ownedMaster = master;
                master = -1;
                return (child, ownedMaster);
            }
            if (!string.IsNullOrEmpty(workingDirectory))
                Check(AddChdir(actions, workingDirectory), "spawn working directory");
            Check(AddClose(actions, master), "close child master");
            Check(AddClose(actions, slave), "close inherited slave");
            // Linux runs these actions after SETSID; Darwin acquires the tty in
            // the native launcher because its kernel applies SETSID afterwards.
            Check(AddOpen(actions, 0, tty, 2, 0), "open child controlling tty");
            Check(AddDup(actions, 0, 1), "child stdout");
            Check(AddDup(actions, 0, 2), "child stderr");

            int error = 2;
            bool denied = false;
            foreach (string candidate in Candidates(executable, workingDirectory, environment))
            {
                int pid;
                do { error = Spawn(out pid, candidate, actions, attributes, argv.Pointer, env.Pointer); }
                while (error == 4); // EINTR
                if (error == 8) // ENOEXEC: execvp-compatible script fallback
                {
                    using UnixPtyArguments script = new("/bin/sh", arguments, candidate);
                    do { error = Spawn(out pid, "/bin/sh", actions, attributes, script.Pointer, env.Pointer); }
                    while (error == 4);
                }
                if (error == 0)
                {
                    int ownedMaster = master;
                    master = -1;
                    return (pid, ownedMaster);
                }
                if (error == 13) denied = true;
                else if (error is not (2 or 20)) break; // ENOENT, ENOTDIR
            }
            throw new IOException($"Unable to spawn PTY command '{executable}': errno {(denied && error is 2 or 20 ? 13 : error)}");
        }
        finally
        {
            if (actionsReady) _ = ActionsDestroy(actions);
            if (attrsReady) _ = AttrDestroy(attributes);
            if (slave >= 0) _ = Close(slave);
            if (master >= 0) _ = Close(master);
        }
    }

    private static int SpawnMac(out int pid, string path, string executable, IReadOnlyList<string>? arguments,
        int master, int slave, void* attributes, byte** environment, string? cwd)
    {
        string helper = Path.Combine(AppContext.BaseDirectory, "royalterminal-pty-spawn");
        if (!File.Exists(helper) && AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") is string searchPaths)
        {
            foreach (string directory in searchPaths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = Path.Combine(directory, "royalterminal-pty-spawn");
                if (File.Exists(candidate)) { helper = candidate; break; }
            }
        }
        if (!File.Exists(helper)) throw new FileNotFoundException("The macOS PTY launcher is missing.", helper);
        // NuGet extraction may omit Unix executable permission on native assets.
        UnixFileMode mode = File.GetUnixFileMode(helper);
        if ((mode & UnixFileMode.UserExecute) == 0) File.SetUnixFileMode(helper, mode | UnixFileMode.UserExecute);
        List<string> values = [path, executable];
        if (arguments is not null) values.AddRange(arguments);
        using UnixPtyArguments argv = new(helper, values);
        int* pipe = stackalloc int[2];
        if (Pipe(pipe) != 0) throw new IOException($"PTY status pipe failed: {Marshal.GetLastPInvokeError()}");
        ulong* actions = stackalloc ulong[16];
        bool ready = false;
        pid = -1;
        try
        {
            if (Ioctl(pipe[0], 0x20006601u) != 0 || Ioctl(pipe[1], 0x20006601u) != 0)
                throw new IOException($"PTY status close-on-exec setup failed: {Marshal.GetLastPInvokeError()}");
            Check(ActionsInit(actions), "launcher actions");
            ready = true;
            if (!string.IsNullOrEmpty(cwd)) Check(AddChdir(actions, cwd), "launcher cwd");
            Check(AddDup(actions, slave, 0), "launcher stdin");
            Check(AddDup(actions, slave, 1), "launcher stdout");
            Check(AddDup(actions, slave, 2), "launcher stderr");
            // dup2 already replaced any PTY descriptors allocated into closed
            // parent stdio slots; do not close those newly installed streams.
            if (master > 2) Check(AddClose(actions, master), "launcher master");
            if (slave > 2) Check(AddClose(actions, slave), "launcher slave");
            Check(AddDup(actions, pipe[1], 3), "launcher status");
            int result = Spawn(out pid, helper, actions, attributes, argv.Pointer, environment);
            _ = Close(pipe[1]);
            pipe[1] = -1;
            if (result != 0) return result;
            int error = 0;
            int offset = 0;
            while (offset < sizeof(int))
            {
                nint read = Read(pipe[0], (byte*)&error + offset, (nuint)(sizeof(int) - offset));
                if (read == 0) break; // exec closed the CLOEXEC status descriptor
                if (read < 0)
                {
                    if (Marshal.GetLastPInvokeError() == 4) continue;
                    error = 5; // EIO: status handshake failed
                    break;
                }
                offset += (int)read;
            }
            if (error == 0 && offset == 0) return 0;
            if (error == 0 || offset != sizeof(int)) error = 5;
            while (WaitPid(pid, null, 0) < 0 && Marshal.GetLastPInvokeError() == 4) { }
            pid = -1;
            return error;
        }
        finally
        {
            if (ready) _ = ActionsDestroy(actions);
            _ = Close(pipe[0]);
            if (pipe[1] >= 0) _ = Close(pipe[1]);
        }
    }

    private static List<string> Candidates(string executable, string? cwd, IReadOnlyDictionary<string, string>? environment)
    {
        string directory = string.IsNullOrEmpty(cwd) ? Environment.CurrentDirectory : Path.GetFullPath(cwd);
        if (executable.Contains('/'))
        {
            return [Path.GetFullPath(executable, directory)];
        }
        string path = environment is not null && environment.TryGetValue("PATH", out string? configured)
            ? configured : Environment.GetEnvironmentVariable("PATH") ?? "/bin:/usr/bin";
        List<string> candidates = [];
        foreach (string part in path.Split(':'))
            candidates.Add(Path.GetFullPath(Path.Combine(part, executable), directory));
        return candidates;
    }

    private static void Check(int error, string operation)
    {
        if (error != 0) throw new IOException($"PTY {operation} failed: {error}");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinSize { internal ushort Rows, Columns, Width, Height; }

    [LibraryImport("libSystem.dylib", EntryPoint = "openpty", SetLastError = true)]
    private static partial int OpenPtyMac(out int master, out int slave, byte* name, void* term, WinSize* size);
    [LibraryImport("libutil.so.1", EntryPoint = "openpty", SetLastError = true)]
    private static partial int OpenPtyLinux(out int master, out int slave, byte* name, void* term, WinSize* size);
    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static partial int Ioctl(int fd, nuint request);
    [LibraryImport("libc", EntryPoint = "close")]
    private static partial int Close(int fd);
    [LibraryImport("libc", EntryPoint = "pipe", SetLastError = true)]
    private static partial int Pipe(int* descriptors);
    [LibraryImport("libc", EntryPoint = "read", SetLastError = true)]
    private static partial nint Read(int fd, byte* buffer, nuint length);
    [LibraryImport("libc", EntryPoint = "waitpid", SetLastError = true)]
    private static partial int WaitPid(int pid, int* status, int options);
    [LibraryImport("libc", EntryPoint = "posix_spawnattr_init")]
    private static partial int AttrInit(void* attributes);
    [LibraryImport("libc", EntryPoint = "posix_spawnattr_destroy")]
    private static partial int AttrDestroy(void* attributes);
    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_init")]
    private static partial int ActionsInit(void* actions);
    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_destroy")]
    private static partial int ActionsDestroy(void* actions);
    [LibraryImport("libc", EntryPoint = "posix_spawnattr_setflags")]
    private static partial int SetFlags(void* attributes, short flags);
    [LibraryImport("libc", EntryPoint = "posix_spawnattr_setsigdefault")]
    private static partial int SetSignalDefault(void* attributes, void* signals);
    [LibraryImport("libc", EntryPoint = "posix_spawnattr_setsigmask")]
    private static partial int SetSignalMask(void* attributes, void* signals);
    [LibraryImport("libc", EntryPoint = "sigfillset")]
    private static partial int SigFill(void* signals);
    [LibraryImport("libc", EntryPoint = "sigemptyset")]
    private static partial int SigEmpty(void* signals);
    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_addclose")]
    private static partial int AddClose(void* actions, int fd);
    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_addopen")]
    private static partial int AddOpen(void* actions, int fd, byte* path, int flags, uint mode);
    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_adddup2")]
    private static partial int AddDup(void* actions, int source, int destination);
    [LibraryImport("libc", EntryPoint = "posix_spawn_file_actions_addchdir_np", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int AddChdir(void* actions, string path);
    [LibraryImport("libc", EntryPoint = "posix_spawn", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Spawn(out int pid, string path, void* actions, void* attributes, byte** argv, byte** env);
}
