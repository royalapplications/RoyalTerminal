// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Reactive.Linq;
using System.Text;
using ReactiveUI;
using ReactiveUI.Reactive;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.App.ViewModels;

/// <summary>Framework-independent presentation of the active terminal's program statuses.</summary>
public sealed class ProgramStatusViewModel : ReactiveObject
{
    private string _summary = string.Empty;
    private string _details = string.Empty;
    private readonly ObservableAsPropertyHelper<bool> _hasStatus;

    /// <summary>Creates empty presentation state.</summary>
    public ProgramStatusViewModel()
        => _hasStatus = this.WhenAnyValue(model => model.Summary)
            .Select(summary => summary.Length != 0).ToProperty(this, model => model.HasStatus);

    /// <summary>Most urgent current status, including its terminal identity.</summary>
    public string Summary
    {
        get => _summary;
        private set => this.RaiseAndSetIfChanged(ref _summary, value);
    }

    /// <summary>Plain-text details for every record in the active terminal.</summary>
    public string Details
    {
        get => _details;
        private set => this.RaiseAndSetIfChanged(ref _details, value);
    }

    /// <summary>Whether there is a status to display.</summary>
    public bool HasStatus => _hasStatus.Value;

    /// <summary>Replaces the display snapshot. Application inheritance must already be resolved.</summary>
    public void Update(string terminalName, IReadOnlyList<TerminalProgramStatus> records)
    {
        ArgumentNullException.ThrowIfNull(terminalName);
        ArgumentNullException.ThrowIfNull(records);
        StringBuilder details = new();
        string summary = string.Empty;
        int highestPriority = -1;
        string source = TerminalHyperlinkSafety.SanitizeDisplay(terminalName);
        for (int i = 0; i < records.Count; i++)
        {
            TerminalProgramStatus record = records[i];
            string state = record.State switch
            {
                TerminalProgramStatusState.Blocked => record.Kind switch
                {
                    TerminalProgramStatusKind.Permission => "Waiting for approval",
                    TerminalProgramStatusKind.Question => "Waiting for an answer",
                    TerminalProgramStatusKind.Auth => "Waiting for authentication",
                    _ => "Waiting for you",
                },
                TerminalProgramStatusState.Working => "Working",
                TerminalProgramStatusState.Done => "Done",
                TerminalProgramStatusState.Error => "Failed",
                _ => "Idle",
            };
            if (record.Progress is { } progress) state += $" {progress}%";
            string label = record.Title.Length != 0 ? record.Title : record.Id;
            if (record.App.Length != 0) label = label.Length == 0 ? record.App : $"{record.App}: {label}";
            string line = source + (label.Length == 0 ? string.Empty : " / " + TerminalHyperlinkSafety.SanitizeDisplay(label)) + ": " + state;
            if (record.Message.Length != 0) line += " — " + TerminalHyperlinkSafety.SanitizeDisplay(record.Message);
            if (details.Length != 0) details.AppendLine();
            details.Append(line);
            int priority = record.State switch
            {
                TerminalProgramStatusState.Blocked => 4,
                TerminalProgramStatusState.Error => 3,
                TerminalProgramStatusState.Working => 2,
                TerminalProgramStatusState.Done => 1,
                _ => 0,
            };
            if (priority < highestPriority) continue;
            highestPriority = priority;
            summary = line;
        }
        Details = details.ToString();
        Summary = summary;
    }
}
