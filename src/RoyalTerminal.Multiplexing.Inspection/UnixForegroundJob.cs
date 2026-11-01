// Copyright (c) Royal Apps. Licensed under the MIT license.
using System.Globalization;
using System.Text.Json.Serialization;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Multiplexing.Inspection;

/// <summary>A sampled change in PTY job control. Ancestors are nearest first and limited to this tty and OS session.</summary>
public sealed record WorkspaceForegroundChange(long Generation,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] WorkspaceProcessIdentity? Foreground,
    IReadOnlyList<WorkspaceProcessIdentity> Ancestors);

internal sealed record UnixJobStat(int Pid, string Name, bool Live, int Parent, int Group, int Session, int Tty, int Foreground, ulong Birth)
{
    internal WorkspaceProcessIdentity Identity => new(Pid, Name);
    internal bool OnTerminal(UnixJobStat root) => Live && Tty != 0 && Tty == root.Tty && Session == root.Session;
    internal static UnixJobStat? Read(int pid)
    {
        if (pid <= 0) return null;
        if (OperatingSystem.IsMacOS()) return DarwinJobStat.Read(pid);
        if (!OperatingSystem.IsLinux()) return null;
        try { return ParseLinux(File.ReadAllText(FormattableString.Invariant($"/proc/{pid}/stat")),pid); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    // comm may contain spaces, parentheses and even newline. Fields after its final ')' are numeric except state.
    internal static UnixJobStat? ParseLinux(string stat, int expectedPid)
    {
        int left=stat.IndexOf('('), right=stat.LastIndexOf(')');
        if (left<1 || right<=left || right+2>=stat.Length || !int.TryParse(stat.AsSpan(0,left).Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out int pid) || pid!=expectedPid) return null;
        var tail=stat.AsSpan(right+2).Trim(); Span<Range> fields=stackalloc Range[21];
        if (tail.Split(fields,' ',StringSplitOptions.RemoveEmptyEntries)<20 || tail[fields[0]].Length!=1) return null;
        static bool Int(ReadOnlySpan<char> s,out int n)=>int.TryParse(s,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out n);
        if(!Int(tail[fields[1]],out int parent)||!Int(tail[fields[2]],out int group)||!Int(tail[fields[3]],out int session)||!Int(tail[fields[4]],out int tty)||!Int(tail[fields[5]],out int fg)||!ulong.TryParse(tail[fields[19]],NumberStyles.None,CultureInfo.InvariantCulture,out ulong birth))return null;
        return new(pid,stat.Substring(left+1,Math.Min(128,right-left-1)),tail[fields[0]][0] is not ('Z' or 'X' or 'x'),parent,group,session,tty,fg,birth);
    }
}

public sealed partial class InspectingTerminalTransport
{
    private readonly record struct JobStamp(ulong RootBirth, int Group, int Pid, ulong Birth, string? Name);
    private JobStamp? _observedJob;
    private long _jobGeneration;
    internal bool SupportsJobObservation => (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) && _inner is ITerminalPtyTransport;

    // One serialized sample per factory. Do not hold the parser gate during potentially blocking kernel calls.
    internal void SampleForegroundJob()
    {
        if (_inner is not ITerminalPtyTransport pty) return;
        int pid;
        try { lock (_gate) { if (!_started || _disposed || _exited || !pty.IsRunning) return; pid = pty.Pty.ChildPid; } }
        catch (InvalidOperationException) { return; }
        var root = UnixJobStat.Read(pid);
        if (root is not { Live:true } || root.Tty == 0) return;
        var leader = root.Foreground == pid ? root : UnixJobStat.Read(root.Foreground);
        if (leader is not null && (!leader.OnTerminal(root) || leader.Group != root.Foreground)) leader = null;
        var stamp = new JobStamp(root.Birth,root.Foreground,leader?.Pid ?? 0,leader?.Birth ?? 0,leader?.Name);
        if (_observedJob == stamp || (!_observedJob.HasValue && leader is null)) return;
        List<WorkspaceProcessIdentity> ancestors = [];
        var node = leader;
        while (node is not null && node.Pid != pid && node.Parent > 0 && ancestors.Count < 12)
        {
            var parent = node.Parent == pid ? root : UnixJobStat.Read(node.Parent);
            if (parent is null || !parent.OnTerminal(root) || parent.Birth > node.Birth || parent.Pid == node.Pid) break;
            ancestors.Add(parent.Identity); node = parent;
        }
        if (leader is not null && leader.Pid != pid)
        {
            var check = UnixJobStat.Read(leader.Pid);
            if (check is null || !check.OnTerminal(root) || check.Group != leader.Group || check.Birth != leader.Birth || check.Name != leader.Name) return;
        }
        var fence = UnixJobStat.Read(pid);
        if (fence is not { Live:true } || fence.Birth != root.Birth || fence.Session != root.Session || fence.Tty != root.Tty || fence.Foreground != root.Foreground || (leader?.Pid == pid && fence.Name != root.Name)) return;
        lock (_gate)
        {
            if (_disposed || _exited || !_started) return;
            _observedJob = stamp;
            Metadata.JobChanged(new(++_jobGeneration,leader?.Identity,ancestors.AsReadOnly()));
        }
    }
}
