// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.PtyHarness - Cross-platform PTY harness for keyboard/mouse mode integration tests.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;

namespace RoyalTerminal.PtyHarness;

internal static partial class Program
{
    private const int StdinHandle = -10;
    private const uint EnableProcessedInput = 0x0001;
    private const uint EnableLineInput = 0x0002;
    private const uint EnableEchoInput = 0x0004;
    private const uint EnableWindowInput = 0x0008;
    private const uint EnableMouseInput = 0x0010;
    private const uint EnableVirtualTerminalInput = 0x0200;

    private static int Main()
    {
        string? logPath = Environment.GetEnvironmentVariable("RT_HARNESS_LOG");
        if (string.IsNullOrWhiteSpace(logPath))
        {
            return 2;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(logPath) ?? ".");
        double timeoutSeconds = ParseTimeoutSeconds(Environment.GetEnvironmentVariable("RT_HARNESS_TIMEOUT_SEC"));

        HarnessLogger logger = new(logPath);
        try
        {
            using TerminalInputModeScope inputModeScope = TerminalInputModeScope.Enable(logger);

            int[] modes = ParseModes(Environment.GetEnvironmentVariable("RT_HARNESS_MODES"));
            EmitMouseModeEnables(modes);

            if (!TryGetConsoleSize(out int rows, out int cols))
            {
                rows = 0;
                cols = 0;
            }

            logger.Log($"READY {rows}x{cols}");
            logger.Log($"SIZE {rows}x{cols}");
            logger.Log($"MODE {string.Join(",", modes)}");
            logger.Log("KEY none");
            logger.Log("MOUSE none");

            return RunLoop(logger, timeoutSeconds, rows, cols);
        }
        catch (Exception ex)
        {
            logger.Log($"ERROR {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static int RunLoop(HarnessLogger logger, double timeoutSeconds, int initialRows, int initialCols)
    {
        Stream input = Console.OpenStandardInput();
        byte[] readBuffer = new byte[256];
        List<byte> parseBuffer = new(capacity: 1024);

        DateTime deadlineUtc = DateTime.UtcNow + TimeSpan.FromSeconds(timeoutSeconds);
        DateTime lastActivityUtc = DateTime.UtcNow;

        int rows = initialRows;
        int cols = initialCols;

        Task<int> pendingRead = input.ReadAsync(readBuffer, 0, readBuffer.Length);

        while (DateTime.UtcNow < deadlineUtc)
        {
            if (TryGetConsoleSize(out int nextRows, out int nextCols)
                && (nextRows != rows || nextCols != cols))
            {
                rows = nextRows;
                cols = nextCols;
                logger.Log($"RESIZE {rows}x{cols}");
                lastActivityUtc = DateTime.UtcNow;
                deadlineUtc = lastActivityUtc + TimeSpan.FromSeconds(timeoutSeconds);
            }

            if (pendingRead.Wait(millisecondsTimeout: 50))
            {
                int count = pendingRead.GetAwaiter().GetResult();
                if (count <= 0)
                {
                    logger.Log("EXIT eof");
                    return 0;
                }

                for (int i = 0; i < count; i++)
                {
                    parseBuffer.Add(readBuffer[i]);
                }

                bool shouldExit = ParseInput(parseBuffer, logger);
                lastActivityUtc = DateTime.UtcNow;
                deadlineUtc = lastActivityUtc + TimeSpan.FromSeconds(timeoutSeconds);

                if (shouldExit)
                {
                    return 0;
                }

                pendingRead = input.ReadAsync(readBuffer, 0, readBuffer.Length);
            }
        }

        logger.Log("EXIT timeout");
        return 0;
    }

    private static bool ParseInput(List<byte> buffer, HarnessLogger logger)
    {
        int index = 0;
        bool shouldExit = false;

        while (index < buffer.Count)
        {
            if (buffer[index] == 0x1B)
            {
                if (!TryParseEscape(buffer, ref index, logger))
                {
                    break;
                }

                continue;
            }

            byte b = buffer[index++];
            logger.Log($"KEY code={b}");
            if (b is (byte)'q' or (byte)'Q')
            {
                logger.Log("EXIT quit");
                shouldExit = true;
            }
        }

        if (index > 0)
        {
            buffer.RemoveRange(0, index);
        }

        return shouldExit;
    }

    private static bool TryParseEscape(List<byte> buffer, ref int index, HarnessLogger logger)
    {
        if (buffer.Count - index < 2)
        {
            return false;
        }

        if (buffer[index + 1] != (byte)'[')
        {
            index += 2;
            return true;
        }

        if (buffer.Count - index < 3)
        {
            return false;
        }

        if (buffer[index + 2] == (byte)'M')
        {
            if (buffer.Count - index < 6)
            {
                return false;
            }

            int cb = buffer[index + 3] - 32;
            int col = buffer[index + 4] - 32;
            int row = buffer[index + 5] - 32;
            logger.Log($"MOUSE encoding=default cb={cb} col={col} row={row}");
            index += 6;
            return true;
        }

        if (buffer.Count - index >= 4 && buffer[index + 2] == (byte)'<')
        {
            if (!TryParseSgr(buffer, ref index, logger))
            {
                return false;
            }

            return true;
        }

        if (buffer.Count - index >= 4 && IsAsciiDigit(buffer[index + 2]))
        {
            if (!TryParseUrxvt(buffer, ref index, logger))
            {
                return false;
            }

            return true;
        }

        int csiFinal = FindCsiFinal(buffer, index + 2);
        if (csiFinal < 0)
        {
            return false;
        }

        index = csiFinal + 1;
        return true;
    }

    private static bool TryParseSgr(List<byte> buffer, ref int index, HarnessLogger logger)
    {
        int scan = index + 3;
        while (scan < buffer.Count)
        {
            byte b = buffer[scan];
            if (b is (byte)'M' or (byte)'m')
            {
                string payload = Encoding.ASCII.GetString(buffer.GetRange(index + 3, scan - (index + 3)).ToArray());
                string[] parts = payload.Split(';');
                if (parts.Length == 3 &&
                    int.TryParse(parts[0], out int cb) &&
                    int.TryParse(parts[1], out int col) &&
                    int.TryParse(parts[2], out int row))
                {
                    char action = (char)b;
                    logger.Log($"MOUSE encoding=sgr cb={cb} col={col} row={row} action={action}");
                }

                index = scan + 1;
                return true;
            }

            if (!(IsAsciiDigit(b) || b == (byte)';'))
            {
                index = scan + 1;
                return true;
            }

            scan++;
        }

        return false;
    }

    private static bool TryParseUrxvt(List<byte> buffer, ref int index, HarnessLogger logger)
    {
        int scan = index + 2;
        while (scan < buffer.Count)
        {
            byte b = buffer[scan];
            if (b == (byte)'M')
            {
                string payload = Encoding.ASCII.GetString(buffer.GetRange(index + 2, scan - (index + 2)).ToArray());
                string[] parts = payload.Split(';');
                if (parts.Length == 3 &&
                    int.TryParse(parts[0], out int cbEncoded) &&
                    int.TryParse(parts[1], out int col) &&
                    int.TryParse(parts[2], out int row))
                {
                    logger.Log($"MOUSE encoding=urxvt cb={cbEncoded - 32} col={col} row={row}");
                }

                index = scan + 1;
                return true;
            }

            if (!(IsAsciiDigit(b) || b == (byte)';'))
            {
                index = scan + 1;
                return true;
            }

            scan++;
        }

        return false;
    }

    private static int FindCsiFinal(List<byte> buffer, int start)
    {
        for (int i = start; i < buffer.Count; i++)
        {
            byte b = buffer[i];
            if (b is >= 0x40 and <= 0x7E)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsAsciiDigit(byte b) => b is >= (byte)'0' and <= (byte)'9';

    private static bool TryGetConsoleSize(out int rows, out int cols)
    {
        try
        {
            rows = Console.WindowHeight;
            cols = Console.WindowWidth;
            return rows >= 0 && cols >= 0;
        }
        catch
        {
            rows = 0;
            cols = 0;
            return false;
        }
    }

    private static int[] ParseModes(string? modes)
    {
        if (string.IsNullOrWhiteSpace(modes))
        {
            return [1000, 1006];
        }

        HashSet<int> parsed = [];
        string[] parts = modes.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            if (int.TryParse(parts[i], out int mode) && mode > 0)
            {
                parsed.Add(mode);
            }
        }

        if (parsed.Count == 0)
        {
            return [1000, 1006];
        }

        return [.. parsed.OrderBy(static m => m)];
    }

    private static void EmitMouseModeEnables(int[] modes)
    {
        if (modes.Length == 0)
        {
            return;
        }

        StringBuilder builder = new(capacity: modes.Length * 8);
        for (int i = 0; i < modes.Length; i++)
        {
            builder.Append("\x1b[?");
            builder.Append(modes[i]);
            builder.Append('h');
        }

        byte[] sequence = Encoding.ASCII.GetBytes(builder.ToString());
        Stream output = Console.OpenStandardOutput();
        output.Write(sequence, 0, sequence.Length);
        output.Flush();
    }

    private static double ParseTimeoutSeconds(string? value)
    {
        if (double.TryParse(value, out double seconds) && seconds > 0)
        {
            return seconds;
        }

        return 20.0;
    }

    private sealed class HarnessLogger
    {
        private readonly string _path;
        private readonly object _sync = new();

        public HarnessLogger(string path)
        {
            _path = path;
        }

        public void Log(string line)
        {
            lock (_sync)
            {
                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
    }

    private sealed class TerminalInputModeScope : IDisposable
    {
        private readonly IntPtr _stdinHandle;
        private readonly uint _originalConsoleMode;
        private readonly bool _restoreConsoleMode;
        private readonly string? _sttyState;
        private readonly bool _restoreStty;
        private readonly string? _sttyDeviceFlag;

        private TerminalInputModeScope(
            IntPtr stdinHandle,
            uint originalConsoleMode,
            bool restoreConsoleMode,
            string? sttyState,
            bool restoreStty,
            string? sttyDeviceFlag)
        {
            _stdinHandle = stdinHandle;
            _originalConsoleMode = originalConsoleMode;
            _restoreConsoleMode = restoreConsoleMode;
            _sttyState = sttyState;
            _restoreStty = restoreStty;
            _sttyDeviceFlag = sttyDeviceFlag;
        }

        public static TerminalInputModeScope Enable(HarnessLogger logger)
        {
            if (OperatingSystem.IsWindows())
            {
                IntPtr stdin = GetStdHandle(StdinHandle);
                if (stdin != IntPtr.Zero &&
                    stdin != new IntPtr(-1) &&
                    GetConsoleMode(stdin, out uint originalMode))
                {
                    uint rawMode = (originalMode |
                                    EnableVirtualTerminalInput |
                                    EnableMouseInput |
                                    EnableWindowInput) &
                                   ~(EnableLineInput | EnableEchoInput | EnableProcessedInput);
                    _ = SetConsoleMode(stdin, rawMode);
                    logger.Log($"INPUT-MODE windows raw={rawMode}");

                    return new TerminalInputModeScope(
                        stdin,
                        originalMode,
                        restoreConsoleMode: true,
                        sttyState: null,
                        restoreStty: false,
                        sttyDeviceFlag: null);
                }

                logger.Log("INPUT-MODE windows unavailable");
                return new TerminalInputModeScope(
                    IntPtr.Zero,
                    0,
                    restoreConsoleMode: false,
                    sttyState: null,
                    restoreStty: false,
                    sttyDeviceFlag: null);
            }

            string? sttyDeviceFlag = OperatingSystem.IsMacOS() ? "-f"
                : OperatingSystem.IsLinux() ? "-F"
                : null;

            if (sttyDeviceFlag is not null &&
                TryRunStty([sttyDeviceFlag, "/dev/tty", "-g"], out string state) &&
                TryRunStty([sttyDeviceFlag, "/dev/tty", "raw", "-echo"], out _))
            {
                logger.Log("INPUT-MODE unix raw");
                return new TerminalInputModeScope(
                    IntPtr.Zero,
                    0,
                    restoreConsoleMode: false,
                    sttyState: state.Trim(),
                    restoreStty: true,
                    sttyDeviceFlag: sttyDeviceFlag);
            }

            logger.Log("INPUT-MODE unix unavailable");
            return new TerminalInputModeScope(
                IntPtr.Zero,
                0,
                restoreConsoleMode: false,
                sttyState: null,
                restoreStty: false,
                sttyDeviceFlag: null);
        }

        public void Dispose()
        {
            if (_restoreConsoleMode)
            {
                _ = SetConsoleMode(_stdinHandle, _originalConsoleMode);
            }

            if (_restoreStty)
            {
                string[] baseArgs = _sttyDeviceFlag is null
                    ? []
                    : [_sttyDeviceFlag, "/dev/tty"];

                if (!string.IsNullOrWhiteSpace(_sttyState))
                {
                    _ = TryRunStty([.. baseArgs, _sttyState], out _);
                }
                else
                {
                    _ = TryRunStty([.. baseArgs, "sane"], out _);
                }
            }
        }
    }

    private static bool TryRunStty(string[] arguments, out string output)
    {
        output = string.Empty;
        ProcessStartInfo startInfo = new("stty")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        for (int i = 0; i < arguments.Length; i++)
        {
            startInfo.ArgumentList.Add(arguments[i]);
        }

        try
        {
            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            output = process.StandardOutput.ReadToEnd();
            string _ = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(3000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            output = string.Empty;
            return false;
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GetStdHandle(int nStdHandle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
}
