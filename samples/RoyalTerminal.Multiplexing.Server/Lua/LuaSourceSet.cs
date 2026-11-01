// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
namespace RoyalTerminal.Multiplexing.Server;

/// <summary>A bounded, owned source snapshot. Actions never re-open module files after a generation is published.</summary>
internal sealed record LuaSourceSet(string Main, Dictionary<string,string>? Modules, string Digest)
{
    private const int TotalBytes = 128 * 1024, MaxModules = 64, MaxEntries = 256, MaxDepth = 8;
    private static readonly UTF8Encoding Utf8 = new(false,true);

    internal static async Task<LuaSourceSet> ReadAsync(string mainPath, string? moduleRoot, CancellationToken ct)
    {
        int remaining = TotalBytes, entries = 0;
        string main = await ReadSource(mainPath);
        if (string.IsNullOrEmpty(moduleRoot))
            return new(main,null,Convert.ToHexStringLower(SHA256.HashData(Utf8.GetBytes(main))));
        if (!Path.IsPathFullyQualified(moduleRoot)) throw new ArgumentException("The Lua module directory must be absolute.");
        var modules = new Dictionary<string,string>(StringComparer.Ordinal);
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var root = new DirectoryInfo(moduleRoot);
        RequireOrdinary(root);
        await Visit(root, "", 0);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        digest.AppendData("RT-LUA-SOURCE-SET-V1\0"u8);Field("@main");Field(main);
        foreach (var name in modules.Keys.Order(StringComparer.Ordinal)){Field(name);Field(modules[name]);}
        return new(main,modules,Convert.ToHexStringLower(digest.GetHashAndReset()));

        async Task<string> ReadSource(string path)
        {
            ct.ThrowIfCancellationRequested();
            await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete,4096,FileOptions.Asynchronous|FileOptions.SequentialScan);
            long initial=stream.Length;
            if(initial<0 || initial>remaining) throw new ArgumentException("The combined Lua configuration exceeds 128 KiB.");
            var bytes=new byte[checked((int)initial+1)];
            int n=await stream.ReadAtLeastAsync(bytes,bytes.Length,throwOnEndOfStream:false,cancellationToken:ct);
            if(n!=initial)throw new IOException("Lua source changed while being read; no generation was published.");
            remaining-=n;
            if(n>0 && bytes[0]==27)throw new ArgumentException("Lua bytecode is not accepted.");
            return Utf8.GetString(bytes,0,n);
        }
        async Task Visit(DirectoryInfo directory,string prefix,int depth)
        {
            if(depth>MaxDepth)throw new ArgumentException("The Lua module tree is too deep.");
            foreach(var node in directory.EnumerateFileSystemInfos())
            {
                ct.ThrowIfCancellationRequested();
                if(++entries>MaxEntries)throw new ArgumentException("The Lua module tree contains too many entries.");
                RequireOrdinary(node);
                if(node is DirectoryInfo child){Segment(child.Name);await Visit(child,prefix+child.Name+".",depth+1);continue;}
                if(!node.Name.EndsWith(".lua",StringComparison.Ordinal))continue;
                string segment=node.Name[..^4];Segment(segment);
                string name=prefix.Length>0 && segment=="init"?prefix[..^1]:prefix+segment;
                if(name.Length>128 || modules.Count>=MaxModules || !unique.Add(name))throw new ArgumentException("Duplicate Lua module or module count/name limit exceeded.");
                modules.Add(name,await ReadSource(node.FullName));
            }
        }
        void Field(string text)
        {
            byte[] bytes=Utf8.GetBytes(text);Span<byte> length=stackalloc byte[4];BinaryPrimitives.WriteInt32BigEndian(length,bytes.Length);
            digest.AppendData(length);digest.AppendData(bytes);
        }
    }
    private static void Segment(string text)
    {
        if(text.Length is <1 or >64 || !(text[0]=='_' || char.IsAsciiLetter(text[0])) || text.Any(c=>c!='_'&&!char.IsAsciiLetterOrDigit(c)))
            throw new ArgumentException("Lua module paths must consist of ASCII identifiers.");
    }
    private static void RequireOrdinary(FileSystemInfo info)
    {
        if(info.LinkTarget is not null || (info.Attributes&(FileAttributes.ReparsePoint|FileAttributes.Device))!=0)
            throw new ArgumentException("Links, reparse points and device files are not allowed in the Lua module tree.");
    }
}
