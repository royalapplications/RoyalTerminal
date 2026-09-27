// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using ReactiveUI.Reactive;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.App.ViewModels;

/// <summary>Framework-independent confirmation and blocked-target dialog state.</summary>
public sealed class HyperlinkPromptViewModel : ReactiveObject, IDisposable
{
    /// <summary>Creates dialog state without launching, querying associations or accessing the clipboard.</summary>
    public HyperlinkPromptViewModel(TerminalHyperlinkRequest request, string? handler)
    {
        ArgumentNullException.ThrowIfNull(request);
        TerminalHyperlinkRequest classified = TerminalHyperlinkSafety.Classify(request.Target);
        // Inspection can add a canonical preview and a file-specific denial,
        // never permission to confirm an unsafe file. Retain raw copy identity.
        if (classified.Disposition == TerminalHyperlinkDisposition.InspectFile && request.Disposition == TerminalHyperlinkDisposition.Deny &&
            request.DenialReason is TerminalHyperlinkDenialReason.InaccessibleFile or TerminalHyperlinkDenialReason.UnsafeFile or TerminalHyperlinkDenialReason.FileInspectionUnavailable)
            classified = classified with { DisplayText = TerminalHyperlinkSafety.SanitizeDisplay(request.DisplayText),
                Disposition = TerminalHyperlinkDisposition.Deny, DenialReason = request.DenialReason };
        request = classified;
        Target = request.DisplayText;
        CanOpen = request.Disposition == TerminalHyperlinkDisposition.Confirm && !string.IsNullOrWhiteSpace(handler);
        Title = CanOpen ? "Open terminal link?" : "Terminal link blocked";
        Handler = handler is null ? string.Empty : TerminalHyperlinkSafety.SanitizeDisplay(handler);
        Message = request.Disposition switch
        {
            TerminalHyperlinkDisposition.Confirm when CanOpen => "This terminal link will be sent to the application below. Only open it if you trust the target and application.",
            TerminalHyperlinkDisposition.Confirm => "The registered application could not be identified. This link will not be opened.",
            TerminalHyperlinkDisposition.InspectFile => "This local target has not been inspected and cannot be opened.",
            _ => request.DenialReason switch
            {
                TerminalHyperlinkDenialReason.UnsafeCharacters => "The target contains invisible, line-breaking or malformed Unicode characters.",
                TerminalHyperlinkDenialReason.InvalidWebHost => "The web target does not contain a valid host.",
                TerminalHyperlinkDenialReason.InaccessibleFile => "The local target does not exist, is inaccessible, or is not a regular file or directory.",
                TerminalHyperlinkDenialReason.UnsafeFile => "Opening this local target could execute code.",
                TerminalHyperlinkDenialReason.FileInspectionUnavailable => "The platform could not inspect this local target safely.",
                _ => "The target cannot be opened safely.",
            },
        };
        OpenCommand = ReactiveCommand.Create(() => true, Observable.Return(CanOpen));
        CancelCommand = ReactiveCommand.Create(() => false);
        CopyTargetInteraction = new Interaction<string, Unit>();
        CopyCommand = ReactiveCommand.CreateFromTask(async () => await CopyTargetInteraction.Handle(request.Target));
    }

    /// <summary>Gets the dialog title.</summary>
    public string Title { get; }
    /// <summary>Gets the escaped display target, never a launch URI.</summary>
    public string Target { get; }
    /// <summary>Gets the escaped application name or bundle identifier.</summary>
    public string Handler { get; }
    /// <summary>Gets the explanation of the required decision or denial.</summary>
    public string Message { get; }
    /// <summary>Whether this dialog can grant permission to dispatch the custom scheme.</summary>
    public bool CanOpen { get; }
    /// <summary>Grants permission; disabled for blocked targets.</summary>
    public ReactiveCommand<Unit, bool> OpenCommand { get; }
    /// <summary>Closes without granting permission.</summary>
    public ReactiveCommand<Unit, bool> CancelCommand { get; }
    /// <summary>Copies the original target only after an explicit user action.</summary>
    public ReactiveCommand<Unit, Unit> CopyCommand { get; }
    /// <summary>Requests a host-owned clipboard operation.</summary>
    public Interaction<string, Unit> CopyTargetInteraction { get; }

    /// <summary>Releases command subscriptions when the dialog closes.</summary>
    public void Dispose()
    {
        OpenCommand.Dispose();
        CancelCommand.Dispose();
        CopyCommand.Dispose();
    }
}
