// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using RoyalTerminal.Multiplexing.Inspection;
using RoyalTerminal.Terminal;
namespace RoyalTerminal.Multiplexing.Server;

internal sealed record BlockTitle(string Title);
internal sealed record BlockDirectory(string Pwd);
internal sealed record BlockDirectories(string[] Directories);
internal sealed record BlockStatus(TerminalProgramStatus[] Records);

[JsonSourceGenerationOptions(PropertyNamingPolicy=JsonKnownNamingPolicy.CamelCase,UseStringEnumConverter=true)]
[JsonSerializable(typeof(BlockTitle)),JsonSerializable(typeof(BlockDirectory)),JsonSerializable(typeof(BlockDirectories)),JsonSerializable(typeof(BlockStatus))]
[JsonSerializable(typeof(TerminalSessionDimensions)),JsonSerializable(typeof(WorkspaceProcessSnapshot))]
internal partial class BlockWire : JsonSerializerContext;

internal sealed partial class ApiCatalog
{
    private const string TerminalCreator = "com.superlogical.terminal";
    private enum BlockAccess { Read, Control, Administrator }
    private sealed record BlockCall(InspectingTerminalTransport Inspection, ApiArguments Arguments, MultiplexerAttachment? Controller, CancellationToken Token);
    private readonly Dictionary<string,Func<ApiArguments,AccessGrant,CancellationToken,ValueTask<JsonElement>>> _blockMethods = new(StringComparer.Ordinal);

    private void RegisterBlockProtocol()
    {
        var j = BlockWire.Default;
        Bind("title","Return the last terminal-reported title.",[],BlockAccess.Read,j.BlockTitle,c=>new(c.Inspection.Title));
        Bind("pwd","Return the last raw terminal-reported directory; no filesystem access.",[],BlockAccess.Read,j.BlockDirectory,c=>new(c.Inspection.WorkingDirectory));
        Bind("size","Return canonical grid and pixel dimensions.",[],BlockAccess.Read,j.TerminalSessionDimensions,c=>c.Inspection.Dimensions);
        Bind("process","Describe the live OS child; Linux PTYs also report a verifiable foreground group leader. Null denotes unavailable, not the shell.",[],BlockAccess.Read,j.WorkspaceProcessSnapshot,c=>c.Inspection.ReadProcesses());
        Bind("program_status","Return current OSC 7501 program records in update order.",[],BlockAccess.Read,j.BlockStatus,c=>new(c.Inspection.ProgramStatuses.ToArray()));
        Bind("format","Format retained terminal content as text, HTML or VT.",["format:string","trim:boolean","unwrap:boolean"],BlockAccess.Read,ServerJsonContext.Default.ScreenCapture,c=>
        {
            var a=c.Arguments;string name=a.String("format","text");
            var kind=name switch {"text"=>TerminalSnapshotExportFormat.PlainText,"html"=>TerminalSnapshotExportFormat.Html,"vt"=>TerminalSnapshotExportFormat.StyledVt,_=>throw new ArgumentException("Use text, html or vt.")};
            string value=c.Inspection.Capture(kind,new(Unwrap:Flag(a,"unwrap"),TrimTrailingWhitespace:Flag(a,"trim")));
            if(value.Length>4*1024*1024)throw new ApiException(413,"capture_limit","The capture exceeds the response budget.");
            return new(name,value,c.Inspection.Title,c.Inspection.WorkingDirectory);
        });
        Bind("write","Send one UTF-8 text or base64 data payload, never both and never retried.",["text:string","data:string"],BlockAccess.Control,ServerJsonContext.Default.ApiSuccess,c=>
        {
            bool text=c.Arguments.Value.TryGetProperty("text",out _);
            if(text==c.Arguments.Value.TryGetProperty("data",out _))throw new ArgumentException("Specify exactly one of text or base64 data.");
            var bytes=text?Encoding.UTF8.GetBytes(c.Arguments.String("text")):Convert.FromBase64String(c.Arguments.String("data"));
            if(bytes.Length>65536)throw new ApiException(413,"input_limit","Input exceeds 64 KiB.");
            c.Controller!.SendInput(bytes);return new();
        });
        Bind("resize","Set the grid. Omitted pixel dimensions retain their previous values.",["columns:integer!","rows:integer!","widthPixels:integer","heightPixels:integer"],BlockAccess.Control,j.TerminalSessionDimensions,c=>
        {
            var a=c.Arguments;var old=c.Inspection.Dimensions;
            var dim=new TerminalSessionDimensions(a.Integer("columns",-1),a.Integer("rows",-1),a.Integer("widthPixels",old.WidthPixels),a.Integer("heightPixels",old.HeightPixels));
            if(dim.Columns is <2 or >512 || dim.Rows is <1 or >256 || dim.WidthPixels is <0 or >32768 || dim.HeightPixels is <0 or >32768)throw new ArgumentException("Terminal dimensions exceed the workspace limits.");
            c.Controller!.Resize(dim);return c.Inspection.Dimensions;
        });
        Bind("reset","Reset the emulated display. Never restart the child or send bytes to stdin.",[],BlockAccess.Control,ServerJsonContext.Default.ApiSuccess,c=>{c.Inspection.ResetDisplay();return new();});
        Bind("set_theme","Change default foreground, background or cursor colors using #RRGGBB.",["foreground:string","background:string","cursor:string"],BlockAccess.Control,ServerJsonContext.Default.ApiSuccess,c=>
        {var a=c.Arguments;c.Inspection.SetDisplayColors(Optional(a,"foreground"),Optional(a,"background"),Optional(a,"cursor"));return new();});
        Bind("list_dir","List immediate directory names under an absolute broker-host path. Administrator only; no recursive traversal or partial results.",["path:string!"],BlockAccess.Administrator,j.BlockDirectories,c=>
        {
            var path=c.Arguments.String("path");if(path.Length>4096 || !Path.IsPathFullyQualified(path))throw new ArgumentException("An absolute directory is required.");
            var names=new List<string>();int seen=0;
            try
            {
                foreach(var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
                {
                    c.Token.ThrowIfCancellationRequested();
                    if(++seen>4096)throw new ApiException(413,"listing_limit","The directory exceeds the scan limit.");
                    if((entry.Attributes & (FileAttributes.Directory|FileAttributes.ReparsePoint))!=FileAttributes.Directory)continue;
                    if(names.Count>=512)throw new ApiException(413,"listing_limit","The directory exceeds the result limit.");
                    names.Add(entry.Name);
                }
            }
            catch(DirectoryNotFoundException){throw new ApiException(404,"directory_not_found","Directory not found.");}
            catch(UnauthorizedAccessException){throw new ApiException(403,"directory_denied","Directory access denied.");}
            names.Sort(StringComparer.Ordinal);return new(names.ToArray());
        });
        RegisterReader("block.method","Dispatch a terminal method by creator and name, using the same schemas and authority as its fully qualified entry.",
            ["sessionId:string!","blockId:string!","creator:string!","method:string!","args:object"],ServerJsonContext.Default.JsonElement,(a,g,ct)=>
            {
                if(a.String("creator")!=TerminalCreator || !_blockMethods.TryGetValue(a.String("method"),out var method)) throw new ApiException(404,"method_not_found","Unknown typed block method.");
                return method(a,g,ct);
            });
    }
    private static string? Optional(ApiArguments a,string property) => a.Value.TryGetProperty(property,out _)?a.String(property):null;
    private static bool Flag(ApiArguments a,string name)=>a.Value.TryGetProperty(name,out var p) ? p.ValueKind switch {JsonValueKind.True=>true,JsonValueKind.False=>false,_=>throw new ArgumentException($"{name} must be boolean.")} : false;

    private void Bind<T>(string shortName,string description,string[] fields,BlockAccess access,JsonTypeInfo<T> output,Func<BlockCall,T> operation)
    {
        string name=TerminalCreator+"."+shortName;string[] wrapper=["sessionId:string!","blockId:string!","args:object"];
        var input=JsonNode.Parse(Schema(name,wrapper).GetRawText())!.AsObject();
        input["properties"]!["args"]=JsonNode.Parse(Schema(name,fields).GetRawText());
        string[] allowed=fields.Select(f=>f[..f.IndexOf(':')]).ToArray();
        string[] required=fields.Where(f=>f.EndsWith('!')).Select(f=>f[..f.IndexOf(':')]).ToArray();
        using var schema=JsonDocument.Parse(input.ToJsonString());var declared=schema.RootElement.Clone();
        using var result=JsonDocument.Parse(output.GetJsonSchemaAsNode().ToJsonString());
        _methods.Add(name,new(new(name,description,declared,result.RootElement.Clone(),ValidationNotes:$"Access: {access}. Independent RoyalTerminal protocol using session and block UUIDs."),["sessionId","blockId","args"],ApiInputSchemas.ForActions(declared),Invoke));
        _blockMethods.Add(shortName,Invoke);
        ValueTask<JsonElement> Invoke(ApiArguments outer,AccessGrant grant,CancellationToken ct)
        {
            Guid session=Session(outer,grant), block=outer.Id("blockId");ct.ThrowIfCancellationRequested();
            if(access==BlockAccess.Administrator)ServerHttp.RequireAdministrator(grant);
            if(access==BlockAccess.Control && !grant.CanControl)throw new ApiException(403,"forbidden","This capability is read only.");
            var a=new ApiArguments(outer.Value.TryGetProperty("args",out _)?outer.Object("args"):EmptyArguments);a.Validate(allowed);
            foreach(string key in required)if(!a.Value.TryGetProperty(key,out _))throw new ArgumentException($"Missing '{key}'.");
            var inspect=_workspaces.Inspection(session,block);
            var state=access==BlockAccess.Control?_workspaces.GetBlock(session,block):null;
            using var lease=state is null?null:_workspaces.Broker.Attach(session,block,MultiplexerRole.Controller,state.LastSequence);
            if(state is not null && !lease!.HasControl)throw new MultiplexerException(MultiplexerErrorCode.Disconnected,"No live process to control.");
            if(!_tokens.IsActive(grant))throw new ApiException(403,"expired","Workspace access expired.");
            T value=operation(new(inspect,a,lease,ct));ct.ThrowIfCancellationRequested();
            if(!_tokens.IsActive(grant))throw new ApiException(403,"expired","Workspace access expired.");
            return Done(value,output);
        }
    }
}
