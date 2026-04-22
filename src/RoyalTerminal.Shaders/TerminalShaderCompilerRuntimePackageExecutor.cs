// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader package execution.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Compiles shader packages and executes them through a runtime backend.
/// </summary>
public sealed class TerminalShaderCompilerRuntimePackageExecutor : ITerminalShaderPackageExecutor, IDisposable
{
    private readonly SemaphoreSlim _programLock = new(1, 1);
    private readonly ITerminalShaderCompiler _compiler;
    private readonly ITerminalShaderRuntime _runtime;
    private readonly TerminalShaderCompilationOptions _options;
    private readonly ITerminalShaderIncludeProvider? _includeProvider;
    private readonly bool _disposeRuntime;
    private TerminalShaderPackage? _cachedPackage;
    private TerminalShaderRuntimeProgram? _cachedProgram;
    private TerminalShaderCompilationResult? _cachedFailure;
    private bool _disposed;

    /// <summary>
    /// Initializes a new compiler/runtime package executor.
    /// </summary>
    /// <param name="compiler">Shader compiler.</param>
    /// <param name="runtime">Runtime backend.</param>
    /// <param name="options">Compilation options.</param>
    /// <param name="includeProvider">Optional include provider.</param>
    /// <param name="disposeRuntime">Whether disposing this executor also disposes the runtime.</param>
    public TerminalShaderCompilerRuntimePackageExecutor(
        ITerminalShaderCompiler compiler,
        ITerminalShaderRuntime runtime,
        TerminalShaderCompilationOptions options,
        ITerminalShaderIncludeProvider? includeProvider = null,
        bool disposeRuntime = true)
    {
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _includeProvider = includeProvider;
        _disposeRuntime = disposeRuntime;
    }

    /// <inheritdoc />
    public TerminalShaderBackendKind BackendKind => _runtime.Capabilities.BackendKind;

    /// <inheritdoc />
    public TerminalShaderBackendCapabilities Capabilities => _runtime.Capabilities;

    /// <inheritdoc />
    public async ValueTask<TerminalShaderFrameResult> RenderFrameAsync(
        TerminalShaderPackage package,
        TerminalShaderFrameRequest frame,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(frame);

        TerminalShaderRuntimeProgram? program = await GetOrCreateProgramAsync(package, cancellationToken)
            .ConfigureAwait(false);
        if (program is null)
        {
            TerminalShaderCompilationResult failure = _cachedFailure ??
                TerminalShaderCompilationResult.Failed(
                    [
                        new TerminalShaderDiagnostic(
                            TerminalShaderDiagnosticSeverity.Error,
                            "RTSHADEREXECUTOR001",
                            $"Shader package '{package.Name}' did not produce a runtime program."),
                    ]);
            return TerminalShaderFrameResult.Failed(BackendKind, failure.Diagnostics);
        }

        return await TerminalShaderRuntimePipeline
            .RenderFrameAsync(package, _runtime, program, frame, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cachedProgram?.Dispose();
        _programLock.Dispose();
        if (_disposeRuntime)
        {
            _runtime.Dispose();
        }
    }

    private async ValueTask<TerminalShaderRuntimeProgram?> GetOrCreateProgramAsync(
        TerminalShaderPackage package,
        CancellationToken cancellationToken)
    {
        if (ReferenceEquals(package, _cachedPackage) && _cachedProgram is not null)
        {
            return _cachedProgram;
        }

        await _programLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (ReferenceEquals(package, _cachedPackage) && _cachedProgram is not null)
            {
                return _cachedProgram;
            }

            _cachedProgram?.Dispose();
            _cachedProgram = null;
            _cachedFailure = null;
            _cachedPackage = package;

            TerminalShaderCompilationResult compilation = await TerminalShaderCompilationPipeline
                .CompileAsync(package, _compiler, _options, _includeProvider, cancellationToken)
                .ConfigureAwait(false);
            if (!compilation.IsSuccess)
            {
                _cachedFailure = compilation;
                return null;
            }

            TerminalShaderRuntimeProgram program = await _runtime
                .CreateProgramAsync(package, compilation, cancellationToken)
                .ConfigureAwait(false);
            if (!program.Compilation.IsSuccess)
            {
                _cachedFailure = program.Compilation;
                program.Dispose();
                return null;
            }

            _cachedProgram = program;
            return _cachedProgram;
        }
        finally
        {
            _programLock.Release();
        }
    }
}
