// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Globalization;
using System.Text.Json.Serialization;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Multiplexing.Inspection;

/// <summary>A sampled change in Linux PTY job control. Ancestors are nearest first and confined to this tty and OS session.</summary>
public sealed record WorkspaceForegroundChange(long Generation,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] WorkspaceProcessIdentity? Foreground,
    IReadOnlyList<WorkspaceProcessIdentity> Ancestors);

internal sealed record LinuxJobStat(int Pid, string Name, char State, int Parent, int Group, int Session, int Tty, int Foreground, ulong Birth)
{
    internal static LinuxJobStat? Read(int pid)
    {
        if (pid <= 0) return null;
        try { return Parse(File.ReadAllText(FormattableString.Invariant($"/proc/{pid}/stat")),pid); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    // comm may contain spaces, parentheses and even newline. Fields after its final ')' are numeric except state.
    internal static LinuxJobStat? Parse(string stat, int expectedPid)
    {
        int left=stat.IndexOf('('), right=stat.LastIndexOf(')');
        if (left<1 || right<=left || right+2>=stat.Length || !int.TryParse(stat.AsSpan(0,left).Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out int pid) || pid!=expectedPid) return null;
        var tail=stat.AsSpan(right+2).Trim(); Span<Range> fields=stackalloc Range[21];
        if (tail.Split(fields,' ',StringSplitOptions.RemoveEmptyEntries)<20 || tail[fields[0]].Length!=1) return null;
        static bool Int(ReadOnlySpan<char> s,out int n)=>int.TryParse(s,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out n);
        if(!Int(tail[fields[1]],out int parent)||!Int(tail[fields[2]],out int group)||!Int(tail[fields[3]],out int session)||!Int(tail[fields[4]],out int tty)||!Int(tail[fields[5]],out int fg)||!ulong.TryParse(tail[fields[19]],NumberStyles.None,CultureInfo.InvariantCulture,out ulong birth))return null;
        string name=stat.Substring(left+1,Math.Min(128,right-left-1));
        return new(pid,name,tail[fields[0]][0],parent,group,session,tty,fg,birth);
    }
    internal bool Live => State is not ('Z' or 'X' or 'x');
    internal WorkspaceProcessIdentity Identity => new(Pid,Name);
    internal bool OnTerminal(LinuxJobStat root) => Live && Tty!=0 && Tty==root.Tty && Session==root.Session;
}

public sealed partial class InspectingTerminalTransport
{
    private readonly record struct JobStamp(ulong RootBirth,int Group,int Pid,ulong Birth,string? Name);
    private JobStamp? _observedJob;
    private long _jobGeneration;
    internal bool SupportsJobObservation => OperatingSystem.IsLinux() && _inner is ITerminalPtyTransport;

    // Called by one bounded round-robin sampler per factory. Never hold the parser gate over procfs reads.
    internal void SampleForegroundJob()
    {
        if (_inner is not ITerminalPtyTransport pty) return;
        int pid;
        try { lock(_gate) { if(!_started||_disposed||_exited||!pty.IsRunning)return; pid=pty.Pty.ChildPid; } }
        catch(InvalidOperationException){ return; }
        var root=LinuxJobStat.Read(pid);
        if(root is not {Live:true} || root.Tty==0) return;
        var leader = root.Foreground==pid ? root : LinuxJobStat.Read(root.Foreground);
        if(leader is not null && (!leader.OnTerminal(root)||leader.Group!=root.Foreground)) leader=null;
        var stamp=new JobStamp(root.Birth,root.Foreground,leader?.Pid??0,leader?.Birth??0,leader?.Name);
        if(_observedJob==stamp) return;
        // No trustworthy leader on the very first observation is not evidence of a transition.
        if(!_observedJob.HasValue && leader is null)return;
        var ancestors=new List<WorkspaceProcessIdentity>(); var node=leader;
        while(node is not null && node.Pid!=pid && node.Parent>0 && ancestors.Count<12)
        {
            var parent=node.Parent==pid?root:LinuxJobStat.Read(node.Parent);
            if(parent is null || !parent.OnTerminal(root) || parent.Birth>node.Birth || parent.Pid==node.Pid) break;
            ancestors.Add(parent.Identity); node=parent;
        }
        if (leader is not null && leader.Pid!=pid && LinuxJobStat.Read(leader.Pid) is not { } verified) return;
        else if (leader is not null && leader.Pid!=pid)
        {
            // Verify the identity independently of its mutable running/sleeping state.
            var check=LinuxJobStat.Read(leader.Pid);
            if(check is null || !check.OnTerminal(root)||check.Group!=leader.Group||check.Birth!=leader.Birth||check.Name!=leader.Name) return;
        }
        var fence=LinuxJobStat.Read(pid);
        if(fence is not {Live:true}||fence.Birth!=root.Birth||fence.Session!=root.Session||fence.Tty!=root.Tty||fence.Foreground!=root.Foreground)return;
        lock(_gate)
        {
            if(_disposed||_exited||!_started)return;
            _observedJob=stamp;
            Metadata.JobChanged(new(++_jobGeneration,leader?.Identity,ancestors.AsReadOnly()));
        }
    }
}
