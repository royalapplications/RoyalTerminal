// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
using System.Diagnostics.CodeAnalysis;
using RoyalTerminal.Terminal;

namespace RoyalTerminal.Multiplexing.Inspection;

/// <summary>Decorates transports and resolves their inspection state by the original options object's identity.</summary>
/// <remarks>Use a distinct options instance for each live block. The broker owns transport lifetime;
/// disposing a transport removes its registration. Equal-valued options remain independent.</remarks>
public sealed class InspectingTerminalTransportFactory : ITerminalTransportFactory
{
    private readonly object _gate = new();
    private readonly ITerminalTransportFactory _inner;
    private readonly TerminalInspectionOptions _options;
    private readonly Dictionary<ITerminalTransportOptions, InspectingTerminalTransport> _transports = new(ReferenceEqualityComparer.Instance);
    private InspectingTerminalTransport[] _jobs = [];
    private Timer? _timer;
    private int _next, _sampling;
    private const int MaxSamplesPerTick = 32;

    /// <summary>Creates a factory without taking ownership of the wrapped factory.</summary>
    public InspectingTerminalTransportFactory(ITerminalTransportFactory inner, TerminalInspectionOptions? options = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _options = options ?? new(); _options.Validate();
    }

    /// <inheritdoc />
    public ITerminalTransport Create(ITerminalTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options); _options.ValidateDimensions(options.Dimensions);
        lock (_gate)
        {
            if (_transports.ContainsKey(options)) throw new InvalidOperationException("Each live inspected transport requires a distinct options instance.");
            ITerminalTransport inner = _inner.Create(options);
            InspectingTerminalTransport transport;
            try { transport = new(inner, options.Dimensions, _options); } catch { inner.Dispose(); throw; }
            transport.Disposed += () => Unregister(options,transport);
            _transports.Add(options,transport);
            if (transport.SupportsJobObservation) { _jobs=[.._jobs,transport]; _timer??=CreateTimer(); }
            return transport;
        }
    }
    private Timer CreateTimer()
    {
        // The first PTY may belong to a bearer-authenticated HTTP request. Never capture that request's execution context.
        Timer Start() => new(static state=>((InspectingTerminalTransportFactory)state!).Tick(),this,250,250);
        if (ExecutionContext.IsFlowSuppressed()) return Start();
        using (ExecutionContext.SuppressFlow()) return Start();
    }

    /// <summary>Finds a live wrapper using reference equality, for binding inspection to a broker block after creation.</summary>
    public bool TryGetInspection(ITerminalTransportOptions options,[NotNullWhen(true)] out InspectingTerminalTransport? inspection)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (_gate) return _transports.TryGetValue(options,out inspection);
    }
    private void Tick()
    {
        if(Interlocked.Exchange(ref _sampling,1)!=0) return;
        try
        {
            InspectingTerminalTransport[] jobs; int start,count;
            lock(_gate)
            {
                jobs=_jobs; if(jobs.Length==0)return;
                start=_next%jobs.Length;count=Math.Min(MaxSamplesPerTick,jobs.Length);_next=(start+count)%jobs.Length;
            }
            for(int i=0;i<count;i++) jobs[(start+i)%jobs.Length].SampleForegroundJob();
        }
        finally { Volatile.Write(ref _sampling,0); }
    }
    private void Unregister(ITerminalTransportOptions options,InspectingTerminalTransport transport)
    {
        lock (_gate)
        {
            if(!_transports.TryGetValue(options,out var current)||!ReferenceEquals(current,transport))return;
            _transports.Remove(options);
            if(!transport.SupportsJobObservation)return;
            _jobs=Array.FindAll(_jobs,x=>!ReferenceEquals(x,transport));
            if(_jobs.Length==0){_timer?.Dispose();_timer=null;_next=0;}
        }
    }
}
