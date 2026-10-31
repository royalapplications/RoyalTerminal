// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Multiplexing.Automation;

/// <summary>The disposition of one key processed by the key-mode engine.</summary>
public enum MultiplexerKeyDispatchKind
{
    /// <summary>No binding applies; forward the current key to the terminal.</summary>
    Unhandled,
    /// <summary>The key started or continued a sequence; it was consumed.</summary>
    Pending,
    /// <summary>A named action is ready for an explicit host dispatch.</summary>
    Action,
    /// <summary>A local mode operation completed.</summary>
    ModeChanged,
    /// <summary>An exclusive mode or blocked binding consumed the key.</summary>
    Blocked,
    /// <summary>Escape canceled an incomplete sequence.</summary>
    Canceled,
}

/// <summary>A passive decision; the engine never performs API mutations itself.</summary>
public sealed record MultiplexerKeyDispatch(MultiplexerKeyDispatchKind Kind, bool Consumed,
    string? ActionId = null, string? Sequence = null, string? Mode = null);

/// <summary>Deterministic stacked key modes and chords, intended for one client event loop.</summary>
/// <remarks>Higher frames win; an exclusive frame blocks lower frames. Prefix conflicts are resolved
/// by configuration merge order. Escape cancels a chord before it pops a mode. Once applies to a
/// frame's first resolved shortcut, including a blocked binding, not to an unrelated unbound key.</remarks>
public sealed class MultiplexerKeyEngine
{
    private readonly Dictionary<string, Mode> _modes = new(StringComparer.Ordinal);
    private readonly List<Frame> _stack = [];
    private readonly MultiplexerAutomationLimits _limits;
    private readonly TimeProvider _timeProvider;
    private string? _pending;
    private Frame? _pendingFrame;
    private long _pendingTimestamp;

    /// <summary>Compiles an owned keymap from the merged configuration.</summary>
    public MultiplexerKeyEngine(MultiplexerResolvedConfiguration configuration,
        MultiplexerAutomationLimits? limits = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _limits = limits ?? new();
        _limits.Validate();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _modes.Add("default", new(false));
        if (configuration.Configuration.Modes.Count > _limits.MaxModes || configuration.Configuration.Bindings.Count > _limits.MaxBindings)
            throw MultiplexerArgumentValidator.Invalid("Keymap exceeds configured budgets.");
        foreach (MultiplexerKeyModeDefinition definition in configuration.Configuration.Modes)
        {
            MultiplexerConfigurationLoader.ValidateModeName(definition.Name);
            _modes[definition.Name] = new(definition.Exclusive);
        }
        HashSet<string> actions = new(configuration.Configuration.Actions.Select(action => action.Id), StringComparer.Ordinal);
        foreach (MultiplexerKeyBinding raw in configuration.Configuration.Bindings)
        {
            MultiplexerKeyBinding binding = MultiplexerConfigurationLoader.ValidateBinding(raw, _limits);
            if (!_modes.TryGetValue(binding.Mode, out Mode? mode)) throw MultiplexerArgumentValidator.Invalid("A binding refers to an undefined mode.");
            if (binding.ActionId is { } action && !actions.Contains(action)) throw MultiplexerArgumentValidator.Invalid("A binding refers to an undefined action.");
            if (binding.EnterMode is { } target && !_modes.ContainsKey(target)) throw MultiplexerArgumentValidator.Invalid("A binding enters an undefined mode.");
            if (!mode.Bindings.TryAdd(binding.Sequence, binding)) throw MultiplexerArgumentValidator.Invalid("The keymap contains an unresolved duplicate binding.");
        }
        foreach (Mode mode in _modes.Values)
        {
            foreach (string sequence in mode.Bindings.Keys)
            {
                int offset = sequence.IndexOf('>');
                while (offset >= 0)
                {
                    string prefix = sequence[..offset];
                    if (mode.Bindings.ContainsKey(prefix)) throw MultiplexerArgumentValidator.Invalid("The keymap contains an unresolved immediate/sequence conflict.");
                    mode.Prefixes.Add(prefix);
                    offset = sequence.IndexOf('>', offset + 1);
                }
            }
        }
        _stack.Add(new("default", false));
    }

    /// <summary>Current top mode.</summary>
    public string CurrentMode => _stack[^1].Name;
    /// <summary>Current stack from default to the highest-priority mode.</summary>
    public IReadOnlyList<string> ModeStack => _stack.Select(frame => frame.Name).ToArray();
    /// <summary>Captured sequence prefix, if one is waiting for another key.</summary>
    public string? PendingSequence => _pending;

    /// <summary>Pushes a known mode and cancels any incomplete sequence.</summary>
    public void EnterMode(string name, bool once = false)
    {
        if (!_modes.ContainsKey(name)) throw MultiplexerArgumentValidator.Invalid("Cannot enter an undefined key mode.");
        if (_stack.Count >= _limits.MaxModeDepth) throw MultiplexerArgumentValidator.Invalid("The key-mode stack limit has been reached.");
        ClearPending();
        _stack.Add(new(name, once));
    }

    /// <summary>Pops the current mode. The permanent default frame cannot be removed.</summary>
    public bool ExitMode()
    {
        ClearPending();
        if (_stack.Count == 1) return false;
        _stack.RemoveAt(_stack.Count - 1);
        return true;
    }

    /// <summary>Returns to the permanent default frame and cancels incomplete sequences.</summary>
    public void ExitAllModes()
    {
        ClearPending();
        if (_stack.Count > 1) _stack.RemoveRange(1, _stack.Count - 1);
    }

    /// <summary>Expires an incomplete sequence without invoking an action.</summary>
    public bool ExpirePending()
    {
        if (_pending is null || _timeProvider.GetElapsedTime(_pendingTimestamp).TotalMilliseconds < _limits.ChordTimeoutMilliseconds) return false;
        ClearPending();
        return true;
    }

    /// <summary>Processes one key. Forward only the current key when Consumed is false.</summary>
    public MultiplexerKeyDispatch ProcessKey(string key)
    {
        string stroke = MultiplexerKeySyntax.NormalizeKey(key);
        ExpirePending();
        if (_pending is { } prefix && _pendingFrame is { } frame)
        {
            if (stroke == "escape")
            {
                ClearPending();
                return new(MultiplexerKeyDispatchKind.Canceled, true, Mode: frame.Name);
            }
            string sequence = prefix + ">" + stroke;
            Mode mode = _modes[frame.Name];
            if (TryMatch(mode, frame, sequence, out MultiplexerKeyDispatch? dispatch)) return dispatch;
            ClearPending();
            if (mode.Exclusive) return new(MultiplexerKeyDispatchKind.Blocked, true, Sequence: sequence, Mode: frame.Name);
            // Earlier keys stay consumed. A nonexclusive mismatch reconsiders only this last key.
        }
        for (int index = _stack.Count - 1; index >= 0; index--)
        {
            Frame activeFrame = _stack[index];
            Mode mode = _modes[activeFrame.Name];
            if (TryMatch(mode, activeFrame, stroke, out MultiplexerKeyDispatch? dispatch)) return dispatch;
            if (stroke == "escape" && index == _stack.Count - 1 && index > 0)
            {
                ExitMode();
                return new(MultiplexerKeyDispatchKind.ModeChanged, true, Mode: CurrentMode);
            }
            if (mode.Exclusive) return new(MultiplexerKeyDispatchKind.Blocked, true, Sequence: stroke, Mode: activeFrame.Name);
        }
        return new(MultiplexerKeyDispatchKind.Unhandled, false, Sequence: stroke, Mode: CurrentMode);
    }

    private bool TryMatch(Mode mode, Frame frame, string sequence, out MultiplexerKeyDispatch dispatch)
    {
        if (mode.Bindings.TryGetValue(sequence, out MultiplexerKeyBinding? binding))
        {
            ClearPending();
            dispatch = Execute(binding, frame);
            return true;
        }
        if (mode.Prefixes.Contains(sequence))
        {
            _pending = sequence;
            _pendingFrame = frame;
            _pendingTimestamp = _timeProvider.GetTimestamp();
            dispatch = new(MultiplexerKeyDispatchKind.Pending, true, Sequence: sequence, Mode: frame.Name);
            return true;
        }
        dispatch = null!;
        return false;
    }

    private MultiplexerKeyDispatch Execute(MultiplexerKeyBinding binding, Frame frame)
    {
        if (binding.ExitAllModes)
        {
            ExitAllModes();
            return new(MultiplexerKeyDispatchKind.ModeChanged, true, Sequence: binding.Sequence, Mode: CurrentMode);
        }
        if (binding.ExitMode)
        {
            ExitMode();
            return new(MultiplexerKeyDispatchKind.ModeChanged, true, Sequence: binding.Sequence, Mode: CurrentMode);
        }
        if (frame.Once && _stack.IndexOf(frame) is > 0 and var index) _stack.RemoveAt(index);
        if (binding.EnterMode is { } target)
        {
            EnterMode(target, binding.Once);
            return new(MultiplexerKeyDispatchKind.ModeChanged, true, Sequence: binding.Sequence, Mode: CurrentMode);
        }
        if (binding.Block) return new(MultiplexerKeyDispatchKind.Blocked, true, Sequence: binding.Sequence, Mode: frame.Name);
        return new(MultiplexerKeyDispatchKind.Action, true, binding.ActionId, binding.Sequence, frame.Name);
    }

    private void ClearPending()
    {
        _pending = null;
        _pendingFrame = null;
    }

    private sealed class Mode(bool exclusive)
    {
        internal bool Exclusive { get; } = exclusive;
        internal Dictionary<string, MultiplexerKeyBinding> Bindings { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> Prefixes { get; } = new(StringComparer.Ordinal);
    }

    private sealed class Frame(string name, bool once)
    {
        internal string Name { get; } = name;
        internal bool Once { get; } = once;
    }
}
