// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Cli;

/// <summary>Streaming age v1 envelope around the existing RTMUX format. No private key travels to the broker.</summary>
internal static class ArchiveProtection
{
    private const long MaxCiphertext = 130L * 1024 * 1024;
    private const int MaxIdentityBytes = 16 * 1024;
    private const string Alphabet = "023456789acdefghjklmnpqrstuvwxyz";

    internal static List<string> Recipients(ref string[] args)
    {
        List<string> result = [], rest = [];
        for (int i = 0; i < args.Length; ++i)
        {
            if (args[i] != "--recipient") { rest.Add(args[i]); continue; }
            if (++i >= args.Length || result.Count >= 16) throw new ArgumentException("Supply 1–16 distinct public age X25519 recipients.");
            string key = args[i];
            if (key.Length != 62 || !key.StartsWith("age1", StringComparison.Ordinal) || key.Skip(4).Any(c => !Alphabet.Contains(c)) || result.Contains(key, StringComparer.Ordinal))
                throw new ArgumentException("Expected a distinct native age1… public recipient. Never pass a private key as an argument.");
            result.Add(key);
        }
        args = rest.ToArray(); return result;
    }

    internal static string? Identity(ref string[] args)
    {
        int i = Array.IndexOf(args, "--identity");
        if (i < 0) return null;
        if (i != args.Length - 2 || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1] == "-")
            throw new ArgumentException("Use --identity FILE as the final option, with a local age-keygen identity file.");
        var path = Path.GetFullPath(args[i + 1]); args = args[..i]; return path;
    }

    internal static async ValueTask<WorkspaceArchiveImage> ReadAsync(string path, string? identity, CancellationToken ct)
    {
        if (identity is null)
        {
            if (path.EndsWith(".age", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Opening an .age workspace requires --identity FILE.");
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            return await WorkspaceArchive.ReadAsync(input, ct);
        }
        // Only a user-supplied absolute ciphertext path goes on the command line. Identities
        // go over a private pipe, and are restricted to native keys (no plugin dispatch).
        var source = new FileInfo(path);
        if (source.Length is <= 0 or > MaxCiphertext) throw new InvalidDataException("Ciphertext exceeds the workspace archive size budget or is empty.");
        byte[] keys = await ReadIdentitiesAsync(identity, ct);
        try
        {
            return await FilterAsync(["--decrypt", "-i", "-", source.FullName],
                (stdin, token) => stdin.WriteAsync(keys, token).AsTask(),
                (stdout, token) => WorkspaceArchive.ReadAsync(stdout, token).AsTask(), ct);
        }
        finally { CryptographicOperations.ZeroMemory(keys); }
    }

    internal static async Task EncryptAsync(IReadOnlyList<string> recipients, Stream destination,
        Func<Stream,CancellationToken,Task> capture, CancellationToken ct)
    {
        List<string> args = ["--encrypt"];
        foreach (string key in recipients) { args.Add("-r"); args.Add(key); }
        await FilterAsync(args, capture, async (stdout, token) =>
        {
            byte[] buffer = new byte[64 * 1024]; long remaining = MaxCiphertext;
            int n; while ((n = await stdout.ReadAsync(buffer,token)) != 0)
            {
                if (n > remaining) throw new InvalidDataException("Encrypted archive exceeded the output budget.");
                await destination.WriteAsync(buffer.AsMemory(0,n),token); remaining -= n;
            }
            if (remaining == MaxCiphertext) throw new InvalidDataException("Encryption produced no output.");
            return true;
        }, ct);
    }

    private static async Task<byte[]> ReadIdentitiesAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        long length = stream.Length;
        if (length is <= 0 or > MaxIdentityBytes) throw new InvalidDataException("Expected a bounded native age identity file.");
        byte[] bytes = new byte[(int)length];
        try
        {
            await stream.ReadExactlyAsync(bytes, ct);
            if (stream.ReadByte() != -1 || !AreNativeKeys(bytes)) throw new InvalidDataException("Expected only native age X25519 identities and comments.");
            return bytes;
        }
        catch { CryptographicOperations.ZeroMemory(bytes); throw; }
    }

    private static bool AreNativeKeys(ReadOnlySpan<byte> text)
    {
        int count = 0;
        while (!text.IsEmpty)
        {
            int br = text.IndexOf((byte)'\n'); var line = br < 0 ? text : text[..br];
            text = br < 0 ? default : text[(br + 1)..];
            if (!line.IsEmpty && line[^1] == '\r') line = line[..^1];
            if (line.IsEmpty || line[0] == '#') continue;
            if (++count > 16 || line.Length != 74 || !line.StartsWith("AGE-SECRET-KEY-1"u8)) return false;
            foreach (var b in line[16..])
                if (b > 127 || (b >= 'a' && b <= 'z') || !Alphabet.Contains(char.ToLowerInvariant((char)b))) return false;
        }
        return count != 0;
    }

    private static string Executable()
    {
        string? configured = Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_AGE");
        if (configured is not null)
        {
            if (!Path.IsPathFullyQualified(configured) || !File.Exists(configured)) throw new ArgumentException("ROYALTERMINAL_MUX_AGE must name an installed age executable by absolute path.");
            return configured;
        }
        string[] choices = OperatingSystem.IsWindows()
            ? [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "age", "age.exe")]
            : ["/usr/bin/age", "/opt/homebrew/bin/age", "/usr/local/bin/age"];
        return choices.FirstOrDefault(p => Path.IsPathFullyQualified(p) && File.Exists(p)) ?? throw new IOException("Install age or set ROYALTERMINAL_MUX_AGE to its trusted absolute path.");
    }

    private static async Task<T> FilterAsync<T>(IEnumerable<string> args, Func<Stream,CancellationToken,Task> produce,
        Func<Stream,CancellationToken,Task<T>> consume, CancellationToken caller)
    {
        caller.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(Executable()) { UseShellExecute=false, CreateNoWindow=true,
            RedirectStandardInput=true, RedirectStandardOutput=true, RedirectStandardError=true };
        start.Environment.Clear();
        if (OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("SystemRoot") is {} win) start.Environment["SystemRoot"] = win;
        foreach (string arg in args) start.ArgumentList.Add(arg);
        Process process;
        try { process = Process.Start(start) ?? throw new IOException("Could not start age."); }
        catch (Win32Exception e) { throw new IOException("Could not start the configured age executable.", e); }
        using var child = process;
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(caller);
        limit.CancelAfter(TimeSpan.FromMinutes(3)); var ct = limit.Token;
        using var stop = ct.Register(() => { try { if (!child.HasExited) child.Kill(entireProcessTree:true); } catch (InvalidOperationException) { } catch (Win32Exception) { } });
        // A failed consumer must wake a producer blocked on a full pipe and vice versa.
        // Never print the helper's stderr: it may contain user-supplied filenames or data.
        async Task Write() { try { await produce(child.StandardInput.BaseStream,ct); child.StandardInput.BaseStream.Close(); } catch { limit.Cancel(); throw; } }
        async Task<T> Read() { try { return await consume(child.StandardOutput.BaseStream,ct); } catch { limit.Cancel(); throw; } }
        Task<T> value = Read(); Task write = Write();
        try
        {
            await Task.WhenAll(value, write, child.StandardError.BaseStream.CopyToAsync(Stream.Null,ct), child.WaitForExitAsync(ct));
            if (child.ExitCode != 0) throw new InvalidDataException("age did not authenticate or complete the archive. No result was published.");
            return await value;
        }
        catch (Exception e) when (!caller.IsCancellationRequested && (e is EndOfStreamException or OperationCanceledException))
        { throw new InvalidDataException("Incomplete or unauthenticated age archive, or the operation timed out.",e); }
        finally { limit.Cancel(); await child.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)); }
    }
}
