// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders - Slang CLI shader compiler backend.

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace RoyalTerminal.Shaders;

/// <summary>
/// Compiles full terminal shader packages by invoking the Slang command-line compiler.
/// </summary>
public sealed class TerminalShaderSlangCliCompiler : ITerminalShaderCompiler
{
    private readonly string _executablePath;
    private readonly TimeSpan _timeout;

    /// <summary>
    /// Initializes a new Slang CLI compiler wrapper.
    /// </summary>
    /// <param name="executablePath">Path to the <c>slangc</c> executable.</param>
    /// <param name="timeout">Optional per-pass compiler timeout.</param>
    public TerminalShaderSlangCliCompiler(string executablePath = "slangc", TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("Slang executable path must be non-empty.", nameof(executablePath));
        }

        _executablePath = executablePath.Trim();
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Checks whether a Slang executable can be started.
    /// </summary>
    /// <param name="executablePath">Path to the <c>slangc</c> executable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when Slang can be started; otherwise <c>false</c>.</returns>
    public static async ValueTask<bool> IsAvailableAsync(
        string executablePath = "slangc",
        CancellationToken cancellationToken = default)
    {
        TerminalShaderSlangCliCompiler compiler = new(executablePath, TimeSpan.FromSeconds(5));
        TerminalShaderProcessResult result = await compiler
            .RunSlangAsync(["-version"], null, cancellationToken)
            .ConfigureAwait(false);
        return result.Started && result.ExitCode == 0;
    }

    /// <inheritdoc />
    public async ValueTask<TerminalShaderCompilationResult> CompileAsync(
        TerminalShaderCompilationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Options.BackendKind == TerminalShaderBackendKind.SkiaRuntimeEffect)
        {
            return TerminalShaderCompilationResult.Failed(
                [
                    new TerminalShaderDiagnostic(
                        TerminalShaderDiagnosticSeverity.Error,
                        "RTSHADERSLANG000",
                        "Slang CLI does not compile Skia Runtime Effect shaders. Use the Skia shader source path for this backend."),
                ]);
        }

        string tempRoot = Path.Combine(Path.GetTempPath(), "RoyalTerminal.Shaders.Slang", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            await WriteResolvedFilesAsync(tempRoot, request.ResolvedFiles, cancellationToken)
                .ConfigureAwait(false);

            List<TerminalShaderCompiledPass> compiledPasses = [];
            List<TerminalShaderDiagnostic> diagnostics = [];
            for (int i = 0; i < request.Package.Passes.Count; i++)
            {
                TerminalShaderPass pass = request.Package.Passes[i];
                if (pass.Stage is not (TerminalShaderStage.Pixel or TerminalShaderStage.Compute))
                {
                    continue;
                }

                TerminalShaderCompilationResult passResult = await CompilePassAsync(
                    tempRoot,
                    request,
                    pass,
                    cancellationToken).ConfigureAwait(false);
                diagnostics.AddRange(passResult.Diagnostics);
                compiledPasses.AddRange(passResult.Passes);
            }

            return new TerminalShaderCompilationResult(compiledPasses, diagnostics);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private async ValueTask<TerminalShaderCompilationResult> CompilePassAsync(
        string tempRoot,
        TerminalShaderCompilationRequest request,
        TerminalShaderPass pass,
        CancellationToken cancellationToken)
    {
        if (pass.SourcePath is null || pass.EntryPoint is null)
        {
            return TerminalShaderCompilationResult.Failed(
                [
                    new TerminalShaderDiagnostic(
                        TerminalShaderDiagnosticSeverity.Error,
                        "RTSHADERSLANG010",
                        $"Pass '{pass.Name}' is missing source or entry-point information."),
                ]);
        }

        string sourcePath = Path.Combine(tempRoot, pass.SourcePath.Replace('/', Path.DirectorySeparatorChar));
        string outputPath = Path.Combine(tempRoot, ".out", pass.Name + GetOutputExtension(request.Options.BackendKind));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        List<string> arguments =
        [
            sourcePath,
            "-entry", pass.EntryPoint,
            "-stage", GetStageName(pass.Stage),
            "-target", GetTargetName(request.Options.BackendKind),
            "-profile", GetTargetProfile(pass),
            "-I", tempRoot,
            "-o", outputPath,
        ];

        foreach (KeyValuePair<string, string> define in request.Options.Defines)
        {
            arguments.Add("-D");
            arguments.Add(string.IsNullOrEmpty(define.Value) ? define.Key : $"{define.Key}={define.Value}");
        }

        TerminalShaderProcessResult processResult = await RunSlangAsync(arguments, tempRoot, cancellationToken)
            .ConfigureAwait(false);
        if (!processResult.Started)
        {
            return TerminalShaderCompilationResult.Failed(
                [
                    new TerminalShaderDiagnostic(
                        TerminalShaderDiagnosticSeverity.Error,
                        "RTSHADERSLANG001",
                        $"Slang executable '{_executablePath}' could not be started: {processResult.ErrorText}"),
                ]);
        }

        if (processResult.TimedOut)
        {
            return TerminalShaderCompilationResult.Failed(
                [
                    new TerminalShaderDiagnostic(
                        TerminalShaderDiagnosticSeverity.Error,
                        "RTSHADERSLANG002",
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Slang timed out after {_timeout.TotalSeconds:0.###} seconds while compiling pass '{pass.Name}'.")),
                ]);
        }

        if (processResult.ExitCode != 0 || !File.Exists(outputPath))
        {
            return TerminalShaderCompilationResult.Failed(
                [
                    new TerminalShaderDiagnostic(
                        TerminalShaderDiagnosticSeverity.Error,
                        "RTSHADERSLANG003",
                        BuildCompilerFailureMessage(pass.Name, processResult),
                        pass.SourcePath),
                ]);
        }

        byte[] code = await File.ReadAllBytesAsync(outputPath, cancellationToken).ConfigureAwait(false);
        TerminalShaderReflectionResult reflectionResult =
            TerminalShaderHlslReflectionScanner.ScanPass(request.ResolvedFiles, pass);
        TerminalShaderCompiledPass compiledPass = new(
            pass.Name,
            pass.Stage,
            GetOutputFormat(request.Options.BackendKind),
            code,
            reflectionResult.Reflection);
        return new TerminalShaderCompilationResult([compiledPass], reflectionResult.Diagnostics);
    }

    private async ValueTask<TerminalShaderProcessResult> RunSlangAsync(
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = _executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        if (workingDirectory is not null)
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        for (int i = 0; i < arguments.Count; i++)
        {
            startInfo.ArgumentList.Add(arguments[i]);
        }

        using Process process = new() { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return TerminalShaderProcessResult.NotStarted("Process.Start returned false.");
            }
        }
        catch (Win32Exception ex)
        {
            return TerminalShaderProcessResult.NotStarted(ex.Message);
        }
        catch (FileNotFoundException ex)
        {
            return TerminalShaderProcessResult.NotStarted(ex.Message);
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        Task waitTask = process.WaitForExitAsync(cancellationToken);
        Task timeoutTask = Task.Delay(_timeout, cancellationToken);

        Task completed = await Task.WhenAny(waitTask, timeoutTask).ConfigureAwait(false);
        if (completed == timeoutTask)
        {
            TryKill(process);
            return new TerminalShaderProcessResult(
                Started: true,
                TimedOut: true,
                ExitCode: -1,
                OutputText: string.Empty,
                ErrorText: string.Empty);
        }

        await waitTask.ConfigureAwait(false);
        string stdout = await stdoutTask.ConfigureAwait(false);
        string stderr = await stderrTask.ConfigureAwait(false);
        return new TerminalShaderProcessResult(
            Started: true,
            TimedOut: false,
            ExitCode: process.ExitCode,
            OutputText: stdout,
            ErrorText: stderr);
    }

    private static async ValueTask WriteResolvedFilesAsync(
        string tempRoot,
        IReadOnlyList<TerminalShaderFile> files,
        CancellationToken cancellationToken)
    {
        for (int i = 0; i < files.Count; i++)
        {
            TerminalShaderFile file = files[i];
            string outputPath = Path.Combine(tempRoot, file.VirtualPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(outputPath, file.Source, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string GetStageName(TerminalShaderStage stage)
    {
        return stage == TerminalShaderStage.Compute ? "compute" : "fragment";
    }

    private static string GetTargetProfile(TerminalShaderPass pass)
    {
        if (pass.TargetProfile is not null)
        {
            return pass.TargetProfile.Name.Replace('_', '-');
        }

        return pass.Stage == TerminalShaderStage.Compute ? "cs-6-0" : "ps-6-0";
    }

    private static string GetTargetName(TerminalShaderBackendKind backendKind)
    {
        return backendKind switch
        {
            TerminalShaderBackendKind.Vulkan => "spirv",
            TerminalShaderBackendKind.Metal => "metal",
            TerminalShaderBackendKind.D3D11 or TerminalShaderBackendKind.D3D12 => "dxil",
            _ => "dxil",
        };
    }

    private static TerminalShaderCompiledCodeFormat GetOutputFormat(TerminalShaderBackendKind backendKind)
    {
        return backendKind switch
        {
            TerminalShaderBackendKind.Vulkan => TerminalShaderCompiledCodeFormat.SpirV,
            TerminalShaderBackendKind.Metal => TerminalShaderCompiledCodeFormat.Msl,
            _ => TerminalShaderCompiledCodeFormat.Dxil,
        };
    }

    private static string GetOutputExtension(TerminalShaderBackendKind backendKind)
    {
        return backendKind switch
        {
            TerminalShaderBackendKind.Vulkan => ".spv",
            TerminalShaderBackendKind.Metal => ".metal",
            _ => ".dxil",
        };
    }

    private static string BuildCompilerFailureMessage(
        string passName,
        TerminalShaderProcessResult processResult)
    {
        string text = string.Join(
            Environment.NewLine,
            new[] { processResult.ErrorText.Trim(), processResult.OutputText.Trim() }
                .Where(static value => value.Length > 0));
        return text.Length == 0
            ? $"Slang failed while compiling pass '{passName}'."
            : $"Slang failed while compiling pass '{passName}': {text}";
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private readonly record struct TerminalShaderProcessResult(
        bool Started,
        bool TimedOut,
        int ExitCode,
        string OutputText,
        string ErrorText)
    {
        public static TerminalShaderProcessResult NotStarted(string errorText)
        {
            return new TerminalShaderProcessResult(
                Started: false,
                TimedOut: false,
                ExitCode: -1,
                OutputText: string.Empty,
                ErrorText: errorText);
        }
    }
}
