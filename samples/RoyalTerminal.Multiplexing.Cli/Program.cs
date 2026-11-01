// Copyright (c) Royal Apps. Licensed under the MIT license.
using RoyalTerminal.Multiplexing.Cli;
using var cancellation=new CancellationTokenSource();
Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancellation.Cancel();};
if(args.ElementAtOrDefault(0)=="skill")return SkillCommand.Run(args[1..],Console.Out,Console.Error);
string? token=Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_TOKEN"),endpoint=Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_URL"),unixSocket=Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_SOCKET");
if(args.ElementAtOrDefault(0)=="--socket")
{
    if(args.Length<2||string.IsNullOrWhiteSpace(args[1])){Console.Error.WriteLine("--socket requires an absolute Unix socket path.");return 2;}
    unixSocket=args[1];args=args[2..];if(args.ElementAtOrDefault(0) is "--endpoint" or "--socket"){Console.Error.WriteLine("Specify a single transport.");return 2;}
}
else if(args.ElementAtOrDefault(0)=="--endpoint")unixSocket=null;
HttpMessageHandler handler;
try {handler=WorkspaceConnection.CreateHandler(unixSocket);}catch(Exception e) when(e is ArgumentException or PlatformNotSupportedException){Console.Error.WriteLine(e.Message);return 2;}
using HttpClient http=new(handler){Timeout=TimeSpan.FromSeconds(130)};
if(!string.IsNullOrEmpty(unixSocket))endpoint="http://localhost/";
int command=args.Length>=2&&args[0]=="--endpoint"?2:0;string? target=command==2?args[1]:endpoint;
if(ConfigurationTools.Handles(args.AsSpan(command)))return await ConfigurationTools.RunAsync(args[command..],target,token,http,Console.Out,Console.Error,cancellation.Token);
switch(args.ElementAtOrDefault(command))
{
    case "send-key":case "events":return await TerminalTools.RunAsync(args[command],args[(command+1)..],target,token,http,Console.Out,Console.Error,cancellation.Token);
    case "restore-layout":return await RestoreLayoutCommand.RunAsync(args[(command+1)..],target,token,http,Console.Out,Console.Error,cancellation.Token);
    case "archive":return await ArchiveCommand.RunAsync(args[(command+1)..],target,token,http,Console.Out,Console.Error,cancellation.Token);
    case "lua":return await LuaScriptCli.RunAsync(args[(command+1)..],target,token,http,Console.Error,cancellation.Token);
    case "do":
        var rest=args[(command+1)..];
        if(rest.Length>0 && rest[0]=="--file") { if(rest.Length==1){Console.Error.WriteLine("--file requires a script filename.");return 2;} rest=rest[1..]; }
        else if(rest.Length==0 || rest[0] is not ("-e" or "-" or "-h" or "--help") && !rest[0].EndsWith(".lua",StringComparison.Ordinal) && !rest[0].Contains('/') && !rest[0].Contains('\\'))break;
        return await LuaDoCommand.RunAsync(rest,target,token,http,Console.In,Console.Out,Console.Error,cancellation.Token);
    case "mirror":using(var invoker=new HttpMessageInvoker(handler,disposeHandler:false))return await CheckpointMirror.RunAsync(args[(command+1)..],target,token,http,invoker,Console.Out,Console.Error,cancellation.Token);
}
int result=await WorkspaceCli.RunAsync(args,token,endpoint,Console.In,Console.Out,Console.Error,http,cancellation.Token,new CliCurrentContext(Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_SESSION"),Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_BLOCK"),Environment.GetEnvironmentVariable("ROYALTERMINAL_MUX_WINDOW")));
if(args.ElementAtOrDefault(command) is null or "-h" or "--help" or "help")Console.WriteLine($"""
  lua FILE [ARGS...]                 Execute a trusted local Lua 5.1 script.
  {LuaDoCommand.Usage} (.lua and paths are files; other bare names invoke server actions)
  keymap [--mode NAME]                Inspect the active server Lua keymap, source and generation.
  config check | reload               Evaluate the server-selected init.lua; only reload publishes.
  skill show|setup|status|remove      Read or install the workspace agent guide and startup hooks.
  --socket /ABSOLUTE/PATH COMMAND     Use a private Unix socket without a TCP fallback.
  mirror SESSION BLOCK [--wait]       Restore a checkpoint; optionally follow as an observer.
  send-key SESSION BLOCK KEY...       Send named presses under the remote terminal's keyboard mode.
  {TerminalTools.EventUsage}    Stream ordered terminal metadata as NDJSON.
  {ArchiveCommand.Usage}
  {RestoreLayoutCommand.Usage}
""");
return result;
