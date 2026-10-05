// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Runtime.ExceptionServices;

namespace RoyalTerminal.Terminal;

internal sealed class ManagedTerminalThread : ITerminalThread
{
    private readonly Thread _thread;
    private ExceptionDispatchInfo? _failure;

    internal ManagedTerminalThread(Action action, string name)
    {
        _thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { _failure = ExceptionDispatchInfo.Capture(exception); }
        }) { IsBackground = true, Name = name };
    }

    public bool IsCurrent => ReferenceEquals(Thread.CurrentThread, _thread);
    public void Start() => _thread.Start();
    public void Join()
    {
        if (IsCurrent) throw new InvalidOperationException("A terminal worker cannot join itself.");
        _thread.Join();
        _failure?.Throw();
    }
}
