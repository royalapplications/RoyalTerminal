// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Diagnostics;
using System.Globalization;
using System.Text;
using RoyalTerminal.Terminal;
namespace RoyalTerminal.Multiplexing.Inspection;

/// <summary>OS-observed process identity. Input, argv and environment are never exposed.</summary>
public sealed record WorkspaceProcessIdentity(int Pid, string? Name);
/// <summary>Foreground is the job-control group's leader when verifiable, not all processes in a pipeline.</summary>
public sealed record WorkspaceProcessSnapshot(WorkspaceProcessIdentity? Child, WorkspaceProcessIdentity? Foreground, int? ForegroundGroupId);

public sealed partial class InspectingTerminalTransport
{
    /// <summary>Reads live identity without holding the parser lock during OS queries. Null means unavailable, never an inferred shell.</summary>
    public WorkspaceProcessSnapshot ReadProcesses()
    {
        int? ChildId() => _inner switch
        {
            ITerminalChildIdentity s => s.LiveChildId,
            ITerminalPtyTransport { IsRunning:true } p when p.Pty.ChildPid is > 0 and var id => id,
            _ => null,
        };
        try
        {
            if (ChildId() is not int child) return new(null,null,null);
            using var process=Process.GetProcessById(child);
            if (process.HasExited) return new(null,null,null);
            string? name;
            try { name=process.ProcessName; } catch (System.ComponentModel.Win32Exception) { name=null; }
            int? group=null; WorkspaceProcessIdentity? foreground=null;
            if (SupportsJobObservation && UnixJobStat.Read(child) is { Live:true } root && root.Tty!=0 && root.Foreground>0)
            {
                var leader = root.Foreground == child ? root : UnixJobStat.Read(root.Foreground);
                var fence = UnixJobStat.Read(child);
                if (fence is { Live:true } && fence.OnTerminal(root) && fence.Birth==root.Birth && fence.Foreground==root.Foreground)
                {
                    group=root.Foreground;
                    if (leader is not null && leader.Group==group && leader.OnTerminal(root)) foreground=leader.Identity;
                }
            }
            if (process.HasExited || ChildId()!=child) return new(null,null,null);
            return new(new(child,name),foreground,group);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException) { return new(null,null,null); }
    }

    /// <summary>Resets the emulated display, not the child process. Publishes RIS through the same ordered output stream.</summary>
    public void ResetDisplay() => PublishDisplayCommand("\u001bc"u8.ToArray());

    /// <summary>Sets the foreground, background and cursor defaults using validated RGB values. Omitted colors remain unchanged.</summary>
    public void SetDisplayColors(string? foreground,string? background,string? cursor)
    {
        StringBuilder vt=new();
        Add(10,foreground); Add(11,background); Add(12,cursor);
        if(vt.Length==0) throw new ArgumentException("At least one color is required.");
        PublishDisplayCommand(Encoding.ASCII.GetBytes(vt.ToString()));
        void Add(int number,string? value)
        {
            if(value is null)return;
            if(value.Length!=7||value[0]!='#'||!value.AsSpan(1).ContainsOnlyHex()) throw new ArgumentException("Use #RRGGBB colors.");
            vt.Append('\u001b').Append(']').Append(number.ToString(CultureInfo.InvariantCulture)).Append(';').Append(value).Append("\u001b\\");
        }
    }
    private void PublishDisplayCommand(byte[] owned)
    {
        lock(_gate)
        {
            ThrowIfDisposed();
            if(!_started || _exited || !_processor.IsParserGround) throw new MultiplexerException(MultiplexerErrorCode.Conflict,"The terminal is not running or is in the middle of a VT/UTF-8 sequence; no command was applied.");
            _processor.Process(owned); DataReceived?.Invoke(owned,owned.Length);
        }
    }
}
internal static class WorkspaceHex
{
    internal static bool ContainsOnlyHex(this ReadOnlySpan<char> text)
    { foreach(char c in text) if(!char.IsAsciiHexDigit(c))return false; return true; }
}
