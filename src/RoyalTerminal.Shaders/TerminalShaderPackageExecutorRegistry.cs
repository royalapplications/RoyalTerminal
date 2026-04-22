// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Full terminal shader runtime registration.

namespace RoyalTerminal.Shaders;

/// <summary>
/// Resolves compiler/runtime package executors from host-registered shader backends.
/// </summary>
public sealed class TerminalShaderPackageExecutorRegistry
{
    private readonly List<TerminalShaderPackageExecutorRegistration> _registrations = [];

    /// <summary>
    /// Gets registered package executors.
    /// </summary>
    public IReadOnlyList<TerminalShaderPackageExecutorRegistration> Registrations => _registrations;

    /// <summary>
    /// Adds a package-executor registration.
    /// </summary>
    /// <param name="registration">Registration to add.</param>
    public void Register(TerminalShaderPackageExecutorRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _registrations.Add(registration);
    }

    /// <summary>
    /// Attempts to create the best package executor for a backend preference.
    /// </summary>
    /// <param name="preference">Backend preference.</param>
    /// <param name="options">Optional compilation options. Backend and compiler are normalized to the selected registration.</param>
    /// <param name="includeProvider">Optional include provider.</param>
    /// <returns>The creation result.</returns>
    public TerminalShaderPackageExecutorCreationResult TryCreate(
        TerminalShaderBackendPreference preference,
        TerminalShaderCompilationOptions? options = null,
        ITerminalShaderIncludeProvider? includeProvider = null)
    {
        List<TerminalShaderDiagnostic> diagnostics = [];
        TerminalShaderBackendKind firstBackend = TerminalShaderBackendSelector.SelectBackend(preference);

        foreach (TerminalShaderBackendKind backendKind in EnumerateBackendOrder(preference))
        {
            TerminalShaderPackageExecutorCreationResult? result = TryCreateForBackend(
                backendKind,
                options,
                includeProvider,
                diagnostics);
            if (result is null)
            {
                continue;
            }

            if (result.IsSuccess)
            {
                return result;
            }

            if (preference != TerminalShaderBackendPreference.Auto)
            {
                return result;
            }
        }

        if (diagnostics.Count == 0)
        {
            diagnostics.Add(new TerminalShaderDiagnostic(
                TerminalShaderDiagnosticSeverity.Error,
                "RTSHADERREGISTRY001",
                $"No shader package executor is registered for backend preference '{preference}'."));
        }

        return new TerminalShaderPackageExecutorCreationResult(firstBackend, null, diagnostics);
    }

    private TerminalShaderPackageExecutorCreationResult? TryCreateForBackend(
        TerminalShaderBackendKind backendKind,
        TerminalShaderCompilationOptions? options,
        ITerminalShaderIncludeProvider? includeProvider,
        List<TerminalShaderDiagnostic> diagnostics)
    {
        bool foundRegistration = false;
        for (int i = 0; i < _registrations.Count; i++)
        {
            TerminalShaderPackageExecutorRegistration registration = _registrations[i];
            if (registration.BackendKind != backendKind)
            {
                continue;
            }

            foundRegistration = true;
            if (!registration.IsAvailable)
            {
                diagnostics.Add(new TerminalShaderDiagnostic(
                    TerminalShaderDiagnosticSeverity.Warning,
                    "RTSHADERREGISTRY002",
                    $"Shader package executor '{registration.DisplayName}' is registered for backend '{backendKind}' but is unavailable on this host."));
                continue;
            }

            if (options is not null &&
                options.CompilerKind != TerminalShaderCompilerKind.Auto &&
                registration.CompilerKind != TerminalShaderCompilerKind.Auto &&
                options.CompilerKind != registration.CompilerKind)
            {
                diagnostics.Add(new TerminalShaderDiagnostic(
                    TerminalShaderDiagnosticSeverity.Warning,
                    "RTSHADERREGISTRY003",
                    $"Shader package executor '{registration.DisplayName}' uses compiler '{registration.CompilerKind}', not requested compiler '{options.CompilerKind}'."));
                continue;
            }

            TerminalShaderCompilationOptions normalizedOptions = NormalizeOptions(registration, options);
            TerminalShaderPackageExecutorFactoryContext context = new(normalizedOptions, includeProvider);
            ITerminalShaderPackageExecutor? executor;
            try
            {
                executor = registration.TryCreate(context);
            }
            catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException)
            {
                diagnostics.Add(new TerminalShaderDiagnostic(
                    TerminalShaderDiagnosticSeverity.Error,
                    "RTSHADERREGISTRY004",
                    $"Shader package executor '{registration.DisplayName}' failed to initialize: {ex.Message}"));
                return new TerminalShaderPackageExecutorCreationResult(backendKind, null, diagnostics);
            }

            if (executor is not null)
            {
                return new TerminalShaderPackageExecutorCreationResult(backendKind, executor, diagnostics);
            }

            diagnostics.Add(new TerminalShaderDiagnostic(
                TerminalShaderDiagnosticSeverity.Warning,
                "RTSHADERREGISTRY005",
                $"Shader package executor '{registration.DisplayName}' did not create an executor."));
        }

        if (!foundRegistration)
        {
            diagnostics.Add(new TerminalShaderDiagnostic(
                TerminalShaderDiagnosticSeverity.Warning,
                "RTSHADERREGISTRY006",
                $"No shader package executor is registered for backend '{backendKind}'."));
        }

        return null;
    }

    private static TerminalShaderCompilationOptions NormalizeOptions(
        TerminalShaderPackageExecutorRegistration registration,
        TerminalShaderCompilationOptions? options)
    {
        TerminalShaderCompilerKind compilerKind = options is null || options.CompilerKind == TerminalShaderCompilerKind.Auto
            ? registration.CompilerKind
            : options.CompilerKind;
        return new TerminalShaderCompilationOptions(
            registration.BackendKind,
            compilerKind,
            options?.Defines,
            options?.DebugName);
    }

    private static IEnumerable<TerminalShaderBackendKind> EnumerateBackendOrder(
        TerminalShaderBackendPreference preference)
    {
        if (preference != TerminalShaderBackendPreference.Auto)
        {
            yield return TerminalShaderBackendSelector.SelectBackend(preference);
            yield break;
        }

        TerminalShaderBackendKind platformDefault = TerminalShaderBackendSelector.SelectBackend(preference);
        yield return platformDefault;

        TerminalShaderBackendKind[] fallbackOrder =
        [
            TerminalShaderBackendKind.D3D11,
            TerminalShaderBackendKind.D3D12,
            TerminalShaderBackendKind.Vulkan,
            TerminalShaderBackendKind.Metal,
        ];

        for (int i = 0; i < fallbackOrder.Length; i++)
        {
            if (fallbackOrder[i] != platformDefault)
            {
                yield return fallbackOrder[i];
            }
        }
    }
}
