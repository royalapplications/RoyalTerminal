// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Avalonia.App - Host-owned split pane policy extension points.

using System;
using System.Collections.Generic;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Avalonia.App.Services;

/// <summary>
/// Describes the active pane and the default cloned launch profile used when the app shell creates a split pane.
/// </summary>
/// <param name="Request">Requested split direction.</param>
/// <param name="TabTitle">Title of the tab that owns the active pane.</param>
/// <param name="SourcePaneId">Stable identifier of the source pane.</param>
/// <param name="SourcePaneTitle">Optional source pane title.</param>
/// <param name="SourceProfileId">Optional source session profile identifier.</param>
/// <param name="SourceTransportId">Transport identifier used by the source pane.</param>
/// <param name="SourceTransportProfileId">Optional transport-specific source profile identifier.</param>
/// <param name="SourceWorkingDirectory">Optional working directory inherited from the source pane or tab.</param>
/// <param name="SourceHasActiveSession">Whether the source pane currently has an active terminal session.</param>
/// <param name="SourceLaunchProfile">Launch profile captured from the source pane.</param>
/// <param name="DefaultLaunchProfile">Default launch profile the shell would use for the new pane.</param>
public sealed record TerminalPaneSplitContext(
    TerminalPaneSplitRequest Request,
    string TabTitle,
    string SourcePaneId,
    string? SourcePaneTitle,
    string? SourceProfileId,
    string SourceTransportId,
    string? SourceTransportProfileId,
    string? SourceWorkingDirectory,
    bool SourceHasActiveSession,
    TerminalSessionProfile SourceLaunchProfile,
    TerminalSessionProfile DefaultLaunchProfile);

/// <summary>
/// Describes whether a pane split is allowed and optionally replaces the shell's default cloned launch profile.
/// </summary>
public sealed record TerminalPaneSplitDecision
{
    private TerminalPaneSplitDecision(bool isAllowed, TerminalSessionProfile? launchProfile, string? statusMessage)
    {
        IsAllowed = isAllowed;
        LaunchProfile = launchProfile;
        StatusMessage = statusMessage;
    }

    /// <summary>
    /// Gets a value indicating whether the split should be created.
    /// </summary>
    public bool IsAllowed { get; }

    /// <summary>
    /// Gets an optional replacement launch profile for the new pane.
    /// </summary>
    public TerminalSessionProfile? LaunchProfile { get; }

    /// <summary>
    /// Gets an optional status message displayed by the shell after the decision is applied.
    /// </summary>
    public string? StatusMessage { get; }

    /// <summary>
    /// Allows the split and optionally supplies a replacement launch profile for the new pane.
    /// </summary>
    /// <param name="launchProfile">Optional replacement profile for the cloned pane session.</param>
    /// <param name="statusMessage">Optional status message for the shell.</param>
    /// <returns>An allowed split decision.</returns>
    public static TerminalPaneSplitDecision Allow(
        TerminalSessionProfile? launchProfile = null,
        string? statusMessage = null)
        => new(true, launchProfile, statusMessage);

    /// <summary>
    /// Denies the split and displays the supplied status message.
    /// </summary>
    /// <param name="statusMessage">Status message explaining why the split was denied.</param>
    /// <returns>A denied split decision.</returns>
    public static TerminalPaneSplitDecision Deny(string statusMessage)
        => new(false, null, statusMessage);
}

/// <summary>
/// Evaluates whether the reusable app shell may split the active pane and how the cloned session should be launched.
/// </summary>
public interface ITerminalPaneSplitPolicy
{
    /// <summary>
    /// Evaluates the requested pane split.
    /// </summary>
    /// <param name="context">Current split context.</param>
    /// <returns>A decision that allows, denies, or replaces the cloned launch profile.</returns>
    TerminalPaneSplitDecision Evaluate(TerminalPaneSplitContext context);
}

/// <summary>
/// Split policy that always lets the shell create an independent cloned pane session.
/// </summary>
public sealed class AllowAllTerminalPaneSplitPolicy : ITerminalPaneSplitPolicy
{
    /// <summary>
    /// Gets the shared allow-all policy instance.
    /// </summary>
    public static AllowAllTerminalPaneSplitPolicy Instance { get; } = new();

    /// <inheritdoc/>
    public TerminalPaneSplitDecision Evaluate(TerminalPaneSplitContext context)
        => TerminalPaneSplitDecision.Allow();
}

/// <summary>
/// Split policy that delegates the decision to a host-supplied callback.
/// </summary>
public sealed class DelegateTerminalPaneSplitPolicy : ITerminalPaneSplitPolicy
{
    private readonly Func<TerminalPaneSplitContext, TerminalPaneSplitDecision> _evaluate;

    /// <summary>
    /// Initializes a new instance of the <see cref="DelegateTerminalPaneSplitPolicy"/> class.
    /// </summary>
    /// <param name="evaluate">Host callback that evaluates each split request.</param>
    public DelegateTerminalPaneSplitPolicy(Func<TerminalPaneSplitContext, TerminalPaneSplitDecision> evaluate)
    {
        _evaluate = evaluate ?? throw new ArgumentNullException(nameof(evaluate));
    }

    /// <inheritdoc/>
    public TerminalPaneSplitDecision Evaluate(TerminalPaneSplitContext context)
        => _evaluate(context);
}

/// <summary>
/// Split policy that allows only selected transport identifiers.
/// </summary>
public sealed class TransportAllowListTerminalPaneSplitPolicy : ITerminalPaneSplitPolicy
{
    private readonly HashSet<string> _allowedTransportIds;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransportAllowListTerminalPaneSplitPolicy"/> class.
    /// </summary>
    /// <param name="allowedTransportIds">Transport identifiers allowed to create split panes.</param>
    public TransportAllowListTerminalPaneSplitPolicy(IEnumerable<string> allowedTransportIds)
    {
        ArgumentNullException.ThrowIfNull(allowedTransportIds);

        _allowedTransportIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string transportId in allowedTransportIds)
        {
            if (!string.IsNullOrWhiteSpace(transportId))
            {
                _allowedTransportIds.Add(transportId.Trim());
            }
        }

        if (_allowedTransportIds.Count == 0)
        {
            throw new ArgumentException("At least one transport identifier must be allowed.", nameof(allowedTransportIds));
        }
    }

    /// <inheritdoc/>
    public TerminalPaneSplitDecision Evaluate(TerminalPaneSplitContext context)
    {
        if (_allowedTransportIds.Contains(context.SourceTransportId))
        {
            return TerminalPaneSplitDecision.Allow();
        }

        string transportName = string.IsNullOrWhiteSpace(context.SourceTransportId)
            ? "this transport"
            : context.SourceTransportId;
        return TerminalPaneSplitDecision.Deny(
            $"Split panes are not available for {transportName} sessions.");
    }
}

/// <summary>
/// Factory helpers for common reusable app-shell split policies.
/// </summary>
public static class TerminalPaneSplitPolicies
{
    /// <summary>
    /// Gets a policy that allows all transports to create independent split pane sessions.
    /// </summary>
    public static ITerminalPaneSplitPolicy AllowAll { get; } = AllowAllTerminalPaneSplitPolicy.Instance;

    /// <summary>
    /// Gets a policy that allows local PTY pane splits and denies every other transport.
    /// </summary>
    public static ITerminalPaneSplitPolicy PtyOnly { get; } = new TransportAllowListTerminalPaneSplitPolicy(
        [TerminalTransportIds.Pty]);

    /// <summary>
    /// Creates a policy that allows only the supplied transport identifiers.
    /// </summary>
    /// <param name="transportIds">Allowed transport identifiers.</param>
    /// <returns>A transport allow-list split policy.</returns>
    public static ITerminalPaneSplitPolicy AllowTransports(params string[] transportIds)
        => new TransportAllowListTerminalPaneSplitPolicy(transportIds);
}
