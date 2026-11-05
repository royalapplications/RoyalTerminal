// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text.Json;
using RoyalTerminal.Multiplexing.Automation;
using RoyalTerminal.Terminal;
using RoyalTerminal.Terminal.Transport.Multiplexing;

namespace RoyalTerminal.Multiplexing.Cli;

internal static class ArchiveCommand
{
    internal const string Usage = "archive save SESSION FILE [--replace] [--recipient age1…] | archive list FILE [--identity FILE] | archive view FILE BLOCK-GUID [--identity FILE]";

    internal static async Task<int> RunAsync(string[] args, string? endpoint, string? credential, HttpClient http, TextWriter output, TextWriter error, CancellationToken token)
    {
        try
        {
            bool saving = args.FirstOrDefault() == "save";
            List<string> recipients = saving ? ArchiveProtection.Recipients(ref args) : [];
            string? identity = saving ? null : ArchiveProtection.Identity(ref args);
            switch (args)
            {
                case ["save", var selector, var path]: return await Save(selector,path,false,recipients);
                case ["save", var selector, var path,"--replace"]: return await Save(selector,path,true,recipients);
                case ["list",var path]:
                {
                    var archive=await ArchiveProtection.ReadAsync(path,identity,token);
                    await output.WriteLineAsync(JsonSerializer.Serialize(archive.Session,ArchiveJson.Default.JsonElement));return 0;
                }
                case ["view",var path,var block] when Guid.TryParse(block,out var id):
                {
                    var archive=await ArchiveProtection.ReadAsync(path,identity,token); var cp=archive.Blocks.FirstOrDefault(b=>b.BlockId==id) ?? throw new ArgumentException("No such block in the archive.");
                    using var restored = ManagedTerminalSnapshot.Restore(cp.Binary, new()
                    {
                        DecodeLimits = new(MaximumPayloadBytes:4*1024*1024,MaximumTotalPayloadBytes:8*1024*1024,MaximumCells:1024*1024,MaximumPages:4096,MaximumSuffixCodepointsPerPage:256*1024,MaximumStringBytesPerRecord:256*1024,MaximumContinuationBytes:64*1024),
                        ScrollbackQuota = new(){MaximumBytes=16*1024*1024},ProcessorOptions=new(){GlyphProtocolEnabled=false,SixelGraphicsEnabled=false,KittyGraphicsMaxApcBytes=0,KittyGraphicsMaxImageBytes=0,KittyGraphicsStorageLimitBytes=0,ClipboardWriteLimitBytes=0,UnknownSequenceMaxBytes=0},
                    });
                    if (!restored.Processor.TryExportSnapshot(TerminalSnapshotExportFormat.PlainText,default,out var text)) throw new InvalidDataException("Unable to format the archived screen.");
                    var safe = new string(text.Where(c=>c is '\n' or '\t' || (!char.IsControl(c) && c is not ('\u202A' or '\u202B' or '\u202C' or '\u202D' or '\u202E' or '\u2066' or '\u2067' or '\u2068' or '\u2069'))).ToArray());
                    await output.WriteLineAsync(safe);return 0;
                }
                default: await error.WriteLineAsync(Usage);return 2;
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){await error.WriteLineAsync("Archive operation cancelled.");return 130;}
        catch(Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or HttpRequestException or JsonException or ArgumentException or InvalidOperationException or MultiplexerAutomationException or OperationCanceledException or TimeoutException)
        { await error.WriteLineAsync(ex.Message);return 1; }

        async Task<int> Save(string selector,string path,bool overwrite,IReadOnlyList<string> recipients)
        {
            if (recipients.Count == 0 && path.EndsWith(".age",StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Refusing to write plaintext under an .age name. Supply --recipient.");
            ArgumentException.ThrowIfNullOrWhiteSpace(credential);
            Uri origin = new(endpoint ?? "http://127.0.0.1:7331/",UriKind.Absolute);
            if(origin.Scheme != Uri.UriSchemeHttps && !(origin.IsLoopback && origin.Scheme == Uri.UriSchemeHttp)) throw new ArgumentException("Use HTTPS, local loopback or a private Unix socket.");
            if (origin.UserInfo.Length!=0 || origin.Query.Length!=0 || origin.Fragment.Length!=0) throw new ArgumentException("Credentials do not belong in the endpoint URL.");
            var api=new MultiplexingApiClient(http,origin,credential); Guid id=await new CliTargetResolver(api).SessionAsync(selector,token);
            string dest=Path.GetFullPath(path), tmp=Path.Combine(Path.GetDirectoryName(dest)!,".royalworkspace-"+Guid.NewGuid().ToString("N")+".tmp");
            if (!overwrite && File.Exists(dest)) throw new IOException("Destination exists. Supply --replace to replace it atomically.");
            var create=new FileStreamOptions{Mode=FileMode.CreateNew,Access=FileAccess.Write,Share=FileShare.None,Options=FileOptions.Asynchronous};
            if(!OperatingSystem.IsWindows()) create.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
            bool created=false;
            try
            {
                var before=await View(token);
                var options=new MultiplexingTransportOptions(origin,credential,id,Guid.Empty,new(80,24,0,0)){Observer=true};
                async Task Capture(Stream dst,CancellationToken ct) => await WorkspaceArchive.WriteAsync(dst,before,(b,c)=>MultiplexingCheckpointClient.ReadAsync(http,options with{BlockId=b},c),ct);
                await using(var file=new FileStream(tmp,create))
                {
                    created=true;
                    if(recipients.Count==0) await Capture(file,token); else await ArchiveProtection.EncryptAsync(recipients,file,Capture,token);
                    var after=await View(token);
                    if(after.GetProperty("revision").GetInt64()!=before.GetProperty("revision").GetInt64()) throw new IOException("Layout changed during capture; no archive was published.");
                    file.Flush(flushToDisk:true);
                }
                token.ThrowIfCancellationRequested();File.Move(tmp,dest,overwrite);created=false;
                await output.WriteLineAsync($"Saved session {id:D}, revision {before.GetProperty("revision").GetInt64()}.");return 0;
            }
            finally {if(created)File.Delete(tmp);}
            async Task<JsonElement> View(CancellationToken ct)
            {
                var view=await api.SendAsync(HttpMethod.Get,$"api/sessions/{id:D}",cancellationToken:ct);
                if(view.GetProperty("id").GetGuid()!=id)throw new InvalidDataException("Archive response has the wrong session identity.");return view;
            }
        }
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(JsonElement))]
internal partial class ArchiveJson : System.Text.Json.Serialization.JsonSerializerContext;
