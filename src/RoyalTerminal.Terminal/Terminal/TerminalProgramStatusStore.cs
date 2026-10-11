// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Collections.ObjectModel;

namespace RoyalTerminal.Terminal;

/// <summary>Bounded OSC 7501 records and ancestry. Callers serialize all access.</summary>
public sealed class TerminalProgramStatusStore
{
    private readonly List<TerminalProgramStatus> _records = [];
    private readonly ReadOnlyCollection<TerminalProgramStatus> _view;
    // Index only explicitly named applications. Inheritance remains dynamic,
    // so replacing/removing a parent immediately changes its descendants.
    private readonly Dictionary<string, string> _applications = new(StringComparer.Ordinal);

    /// <summary>Maximum records per terminal, as specified by OSC 7501.</summary>
    public const int Capacity = 256;

    /// <summary>Creates an empty record store.</summary>
    public TerminalProgramStatusStore() => _view = _records.AsReadOnly();

    /// <summary>Current records in order of last update, oldest first.</summary>
    public IReadOnlyList<TerminalProgramStatus> Records => _view;

    /// <summary>Applies a validated report atomically. Missing fields replace old values.
    /// Returns whether state or update order changed.</summary>
    public bool Apply(TerminalProgramStatus report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.State == TerminalProgramStatusState.Clear)
        {
            bool changed = false;
            for (int i = _records.Count - 1; i >= 0; i--)
            {
                string id = _records[i].Id;
                if (report.Id.Length != 0 && id != report.Id &&
                    !(id.StartsWith(report.Id, StringComparison.Ordinal) &&
                      id.Length > report.Id.Length && id[report.Id.Length] == '/')) continue;
                RemoveAt(i);
                changed = true;
            }
            return changed;
        }

        for (int i = 0; i < _records.Count; i++)
        {
            if (_records[i].Id != report.Id) continue;
            if (i == _records.Count - 1 && _records[i] == report) return false;
            RemoveAt(i);
            break;
        }
        if (_records.Count == Capacity) RemoveAt(0);
        _records.Add(report);
        if (report.App.Length != 0) _applications[report.Id] = report.App;
        return true;
    }

    /// <summary>Removes working and blocked records after a prompt or process exit.</summary>
    public bool ClearTransient()
    {
        bool changed = false;
        for (int i = _records.Count - 1; i >= 0; i--)
        {
            if (_records[i].State is not (TerminalProgramStatusState.Working or TerminalProgramStatusState.Blocked)) continue;
            RemoveAt(i);
            changed = true;
        }
        return changed;
    }

    /// <summary>Removes all records on RIS or a new session.</summary>
    public bool Reset()
    {
        if (_records.Count == 0) return false;
        _records.Clear();
        _applications.Clear();
        return true;
    }

    /// <summary>Resolves an application name using the nearest ancestor, including root.</summary>
    public string GetApplication(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (_applications.Count == 0) return string.Empty;
        Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> applications =
            _applications.GetAlternateLookup<ReadOnlySpan<char>>();
        ReadOnlySpan<char> candidate = id;
        while (true)
        {
            if (applications.TryGetValue(candidate, out string? application)) return application;
            if (candidate.IsEmpty) return string.Empty;
            int separator = candidate.LastIndexOf('/');
            candidate = separator < 0 ? [] : candidate[..separator];
        }
    }

    private void RemoveAt(int index)
    {
        _applications.Remove(_records[index].Id);
        _records.RemoveAt(index);
    }
}
