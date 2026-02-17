// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia — Windows pseudo-terminal using the ConPTY API.
// Spawns a shell process with a real PTY so terminal features work on Windows.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace RoyalTerminal.Terminal;

/// <summary>
/// Managed PTY for Windows using the ConPTY API (Windows 10 1809+).
/// Spawns a child shell process (e.g., cmd.exe, powershell, pwsh) and
/// provides read/write streams.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPty : IPty
{
    private nint _ptyHandle;
    private SafeFileHandle? _pipeIn;   // write to child stdin
    private SafeFileHandle? _pipeOut;  // read from child stdout
    private FileStream? _inputStream;
    private Process? _process;
    private Thread? _readThread;
    private bool _disposed;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _writeSync = new();

    /// <summary>Raised when data is received from the PTY.</summary>
    public event Action<byte[], int>? DataReceived;

    /// <summary>Raised when the child process exits.</summary>
    public event Action<int>? ProcessExited;

    /// <summary>Whether the PTY is currently active.</summary>
    public bool IsRunning => _process is not null && !_process.HasExited && !_disposed;

    /// <summary>The child process ID.</summary>
    public int ChildPid => _process?.Id ?? -1;

    /// <summary>
    /// Spawns a shell process with a ConPTY.
    /// </summary>
    /// <param name="shell">Shell path. Null = auto-detect (pwsh → powershell → cmd).</param>
    /// <param name="columns">Initial terminal width.</param>
    /// <param name="rows">Initial terminal height.</param>
    /// <param name="workingDirectory">Working directory for the shell.</param>
    /// <param name="environment">Additional environment variables.</param>
    public void Start(
        string? shell = null,
        int columns = 80,
        int rows = 24,
        string? workingDirectory = null,
        Dictionary<string, string>? environment = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException("WindowsPty is only supported on Windows.");

        shell ??= DetectShell();
        workingDirectory ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Create pipes for communicating with the ConPTY
        CreatePipe(out var pipeInRead, out var pipeInWrite);
        CreatePipe(out var pipeOutRead, out var pipeOutWrite);

        // Create the pseudo-console
        var size = new COORD { X = (short)columns, Y = (short)rows };
        var hr = CreatePseudoConsole(size, pipeInRead.DangerousGetHandle(), pipeOutWrite.DangerousGetHandle(), 0, out _ptyHandle);
        if (hr != 0)
        {
            pipeInRead.Dispose();
            pipeInWrite.Dispose();
            pipeOutRead.Dispose();
            pipeOutWrite.Dispose();
            throw new Win32Exception(hr, $"CreatePseudoConsole failed: 0x{hr:X8}");
        }

        // Close the handles the ConPTY now owns
        pipeInRead.Dispose();
        pipeOutWrite.Dispose();

        _pipeIn = pipeInWrite;
        _pipeOut = pipeOutRead;
        _inputStream = new FileStream(_pipeIn, FileAccess.Write, bufferSize: 0, isAsync: false);

        // Launch the shell process attached to the ConPTY
        _process = LaunchProcess(shell, workingDirectory, environment);

        // Start reading from the PTY output
        _readThread = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = "WindowsPty-Read",
        };
        _readThread.Start();
    }

    /// <summary>
    /// Writes data to the PTY input (child stdin).
    /// </summary>
    public void Write(string text)
    {
        if (_disposed || _pipeIn is null) return;
        var bytes = Encoding.UTF8.GetBytes(text);
        Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Writes raw bytes to the PTY input.
    /// </summary>
    public void Write(byte[] data, int offset, int count)
    {
        if (_disposed || _inputStream is null) return;
        lock (_writeSync)
        {
            _inputStream.Write(data, offset, count);
            _inputStream.Flush();
        }
    }

    /// <summary>
    /// Resizes the pseudo-console.
    /// </summary>
    public void Resize(int columns, int rows)
    {
        if (_disposed || _ptyHandle == 0) return;
        var size = new COORD { X = (short)columns, Y = (short)rows };
        ResizePseudoConsole(_ptyHandle, size);
    }

    /// <summary>
    /// Stops the PTY and kills the child process.
    /// </summary>
    public void Stop()
    {
        if (_disposed) return;

        _cts.Cancel();

        try { _process?.Kill(entireProcessTree: true); } catch { /* best effort */ }

        _inputStream?.Dispose();
        _inputStream = null;

        _pipeIn?.Dispose();
        _pipeIn = null;

        _pipeOut?.Dispose();
        _pipeOut = null;

        if (_ptyHandle != 0)
        {
            ClosePseudoConsole(_ptyHandle);
            _ptyHandle = 0;
        }

        _readThread?.Join(TimeSpan.FromSeconds(2));
        _process?.Dispose();
        _process = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _cts.Dispose();
    }

    #region Private Implementation

    private void ReadLoop()
    {
        var buffer = new byte[4096];
        try
        {
            using var stream = new FileStream(_pipeOut!, FileAccess.Read, bufferSize: 0, isAsync: false);
            while (!_cts.IsCancellationRequested)
            {
                var bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead <= 0) break;
                DataReceived?.Invoke(buffer, bytesRead);
            }
        }
        catch (IOException) { /* pipe closed */ }
        catch (ObjectDisposedException) { /* handle disposed */ }

        // Report process exit
        try
        {
            _process?.WaitForExit(1000);
            var exitCode = _process?.HasExited == true ? _process.ExitCode : -1;
            ProcessExited?.Invoke(exitCode);
        }
        catch { ProcessExited?.Invoke(-1); }
    }

    private static string DetectShell()
    {
        // Prefer pwsh (PowerShell 7+), then powershell, then cmd
        var pwsh = FindInPath("pwsh.exe");
        if (pwsh is not null) return pwsh;

        var powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(powershell)) return powershell;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "cmd.exe");
    }

    private static string? FindInPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (path is null) return null;

        foreach (var dir in path.Split(Path.PathSeparator))
        {
            var fullPath = Path.Combine(dir, fileName);
            if (File.Exists(fullPath)) return fullPath;
        }
        return null;
    }

    private Process LaunchProcess(string shell, string workingDirectory, Dictionary<string, string>? environment)
    {
        var startupInfo = new STARTUPINFOEX();
        startupInfo.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();

        // Initialize the thread attribute list with the pseudo-console handle
        nint attrList = 0;
        nuint attrListSize = 0;
        InitializeProcThreadAttributeList(0, 1, 0, ref attrListSize);
        attrList = Marshal.AllocHGlobal((int)attrListSize);

        try
        {
            if (!InitializeProcThreadAttributeList(attrList, 1, 0, ref attrListSize))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            if (!UpdateProcThreadAttribute(
                attrList, 0, PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                _ptyHandle, (nuint)nint.Size, 0, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            startupInfo.lpAttributeList = attrList;

            // Build environment block
            nint envBlock = 0;
            if (environment is not null && environment.Count > 0)
            {
                var envVars = Environment.GetEnvironmentVariables();
                foreach (var (key, value) in environment)
                    envVars[key] = value;

                envVars["TERM"] = "xterm-256color";

                var sb = new StringBuilder();
                foreach (System.Collections.DictionaryEntry entry in envVars)
                    sb.Append($"{entry.Key}={entry.Value}\0");
                sb.Append('\0');
                envBlock = Marshal.StringToHGlobalUni(sb.ToString());
            }

            try
            {
                if (!CreateProcessW(
                    null, shell, 0, 0, false,
                    EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT,
                    envBlock, workingDirectory,
                    ref startupInfo, out var processInfo))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                CloseHandle(processInfo.hThread);

                return Process.GetProcessById(processInfo.dwProcessId);
            }
            finally
            {
                if (envBlock != 0) Marshal.FreeHGlobal(envBlock);
            }
        }
        finally
        {
            DeleteProcThreadAttributeList(attrList);
            Marshal.FreeHGlobal(attrList);
        }
    }

    private static void CreatePipe(out SafeFileHandle readSide, out SafeFileHandle writeSide)
    {
        if (!CreatePipe(out readSide, out writeSide, 0, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    #endregion

    #region Native Interop

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public nint lpReserved;
        public nint lpDesktop;
        public nint lpTitle;
        public int dwX, dwY, dwXSize, dwYSize;
        public int dwXCountChars, dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public nint lpReserved2;
        public nint hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public nint lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public nint hProcess;
        public nint hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    private const int EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private const int CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private static readonly nint PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = (nint)0x00020016;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(COORD size, nint hInput, nint hOutput, uint dwFlags, out nint phPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int ResizePseudoConsole(nint hPC, COORD size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void ClosePseudoConsole(nint hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, nint lpPipeAttributes, uint nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(nint lpAttributeList, int dwAttributeCount, int dwFlags, ref nuint lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(nint lpAttributeList, uint dwFlags, nint attribute, nint lpValue, nuint cbSize, nint lpPreviousValue, nint lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void DeleteProcThreadAttributeList(nint lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(
        string? lpApplicationName, string lpCommandLine,
        nint lpProcessAttributes, nint lpThreadAttributes,
        bool bInheritHandles, int dwCreationFlags,
        nint lpEnvironment, string? lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);

    #endregion
}
