// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

namespace RoyalTerminal.Terminal;

/// <summary>A generation-aware owner of reusable terminal output storage.</summary>
public interface ITerminalOutputLeaseOwner
{
    /// <summary>Returns a lease once; stale or repeated generations must be ignored.</summary>
    void Release(long generation);
}

/// <summary>Output bytes whose storage remains valid until this lease is disposed.</summary>
/// <remarks>
/// The receiver owns the lease and must dispose it after parsing, including failure paths.
/// Data retained after disposal must be copied. A generation prevents an old copied lease
/// from returning storage that has since been leased again.
/// </remarks>
public readonly struct TerminalOutputLease : IDisposable
{
    private readonly ITerminalOutputLeaseOwner? _owner;
    private readonly long _generation;

    /// <summary>Creates output backed by stable managed memory that needs no explicit return.</summary>
    public TerminalOutputLease(ReadOnlyMemory<byte> data)
    {
        Data = data;
    }

    /// <summary>Creates a lease for one generation of reusable output storage.</summary>
    public TerminalOutputLease(ReadOnlyMemory<byte> data, ITerminalOutputLeaseOwner owner, long generation)
    {
        Data = data;
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _generation = generation;
    }

    /// <summary>Gets the output bytes, valid until disposal.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Gets whether retaining bytes after disposal requires a copy.</summary>
    public bool IsBorrowed => _owner is not null;

    /// <summary>Returns this storage generation to its owner.</summary>
    public void Dispose() => _owner?.Release(_generation);
}

/// <summary>An optional single-consumer path for transferring reusable output buffers.</summary>
public interface ITerminalOutputLeaseSource
{
    /// <summary>
    /// Gets or sets the receiver that owns each output lease. Set before starting a session.
    /// A null receiver uses the source's conventional output event instead.
    /// </summary>
    Action<TerminalOutputLease>? OutputLeaseCallback { get; set; }
}
