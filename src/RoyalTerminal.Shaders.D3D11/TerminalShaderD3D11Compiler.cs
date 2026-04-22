// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Shaders.D3D11 - Direct3D 11 shader compiler backend.

using System.Text;
using RoyalTerminal.Shaders;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;

namespace RoyalTerminal.Shaders.D3D11;

/// <summary>
/// Compiles HLSL shader packages to Direct3D 11 DXBC bytecode through D3DCompiler.
/// </summary>
public sealed class TerminalShaderD3D11Compiler : ITerminalShaderCompiler
{
    /// <summary>
    /// Gets whether the D3DCompiler backend can run on the current platform.
    /// </summary>
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <inheritdoc />
    public ValueTask<TerminalShaderCompilationResult> CompileAsync(
        TerminalShaderCompilationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = cancellationToken;

        if (!OperatingSystem.IsWindows())
        {
            return ValueTask.FromResult(TerminalShaderCompilationResult.Failed(
                [
                    new TerminalShaderDiagnostic(
                        TerminalShaderDiagnosticSeverity.Error,
                        "RTSHADERD3DCOMPILER000",
                        "D3DCompiler DXBC compilation is available only on Windows."),
                ]));
        }

        if (request.Options.BackendKind != TerminalShaderBackendKind.D3D11)
        {
            return ValueTask.FromResult(TerminalShaderCompilationResult.Failed(
                [
                    new TerminalShaderDiagnostic(
                        TerminalShaderDiagnosticSeverity.Error,
                        "RTSHADERD3DCOMPILER001",
                        $"D3DCompiler DXBC compilation targets Direct3D 11, not '{request.Options.BackendKind}'."),
                ]));
        }

        Dictionary<string, TerminalShaderFile> files = request.ResolvedFiles.ToDictionary(
            static file => file.VirtualPath,
            static file => file,
            StringComparer.OrdinalIgnoreCase);
        ShaderMacro[] macros = request.Options.Defines
            .Select(static pair => new ShaderMacro(pair.Key, pair.Value))
            .ToArray();
        using ResolvedInclude include = new(files);

        List<TerminalShaderCompiledPass> passes = [];
        List<TerminalShaderDiagnostic> diagnostics = [];
        for (int i = 0; i < request.Package.Passes.Count; i++)
        {
            TerminalShaderPass pass = request.Package.Passes[i];
            if (pass.Stage is not (TerminalShaderStage.Pixel or TerminalShaderStage.Compute))
            {
                continue;
            }

            if (pass.SourcePath is null || pass.EntryPoint is null)
            {
                diagnostics.Add(new TerminalShaderDiagnostic(
                    TerminalShaderDiagnosticSeverity.Error,
                    "RTSHADERD3DCOMPILER010",
                    $"Pass '{pass.Name}' is missing source or entry-point information."));
                continue;
            }

            if (!files.TryGetValue(pass.SourcePath, out TerminalShaderFile? file))
            {
                diagnostics.Add(new TerminalShaderDiagnostic(
                    TerminalShaderDiagnosticSeverity.Error,
                    "RTSHADERD3DCOMPILER011",
                    $"Pass '{pass.Name}' references missing source file '{pass.SourcePath}'.",
                    pass.SourcePath));
                continue;
            }

            CompilePass(request.ResolvedFiles, file, pass, macros, include, passes, diagnostics);
        }

        return ValueTask.FromResult(new TerminalShaderCompilationResult(passes, diagnostics));
    }

    private static void CompilePass(
        IReadOnlyList<TerminalShaderFile> resolvedFiles,
        TerminalShaderFile file,
        TerminalShaderPass pass,
        ShaderMacro[] macros,
        Include include,
        List<TerminalShaderCompiledPass> passes,
        List<TerminalShaderDiagnostic> diagnostics)
    {
        Blob? code = null;
        Blob? errors = null;
        try
        {
            Result result = Compiler.Compile(
                file.Source,
                macros,
                include,
                pass.EntryPoint!,
                file.VirtualPath,
                GetTargetProfile(pass),
                ShaderFlags.OptimizationLevel3,
                EffectFlags.None,
                out code,
                out errors);
            if (result.Failure || code is null)
            {
                diagnostics.Add(new TerminalShaderDiagnostic(
                    TerminalShaderDiagnosticSeverity.Error,
                    "RTSHADERD3DCOMPILER012",
                    BuildCompilerFailureMessage(pass.Name, result, errors),
                    file.VirtualPath));
                return;
            }

            TerminalShaderReflectionResult reflectionResult =
                TerminalShaderD3D11ReflectionReader.TryRead(code.AsMemory(), pass);
            if (!HasReflectionPayload(reflectionResult.Reflection))
            {
                diagnostics.AddRange(reflectionResult.Diagnostics);
                reflectionResult = TerminalShaderHlslReflectionScanner.ScanPass(resolvedFiles, pass);
            }

            passes.Add(new TerminalShaderCompiledPass(
                pass.Name,
                pass.Stage,
                TerminalShaderCompiledCodeFormat.Dxbc,
                code.AsMemory().ToArray(),
                reflectionResult.Reflection));
            diagnostics.AddRange(reflectionResult.Diagnostics);
        }
        catch (SharpGenException ex)
        {
            diagnostics.Add(new TerminalShaderDiagnostic(
                TerminalShaderDiagnosticSeverity.Error,
                "RTSHADERD3DCOMPILER013",
                $"D3DCompiler failed while compiling pass '{pass.Name}': {ex.Message}",
                file.VirtualPath));
        }
        finally
        {
            code?.Dispose();
            errors?.Dispose();
        }
    }

    private static string GetTargetProfile(TerminalShaderPass pass)
    {
        if (pass.TargetProfile is not null)
        {
            return pass.TargetProfile.Name;
        }

        return pass.Stage == TerminalShaderStage.Compute ? "cs_5_0" : "ps_5_0";
    }

    private static string BuildCompilerFailureMessage(
        string passName,
        Result result,
        Blob? errors)
    {
        string errorText = errors?.AsString() ?? string.Empty;
        return string.IsNullOrWhiteSpace(errorText)
            ? $"D3DCompiler failed while compiling pass '{passName}' with result {result}."
            : $"D3DCompiler failed while compiling pass '{passName}': {errorText.Trim()}";
    }

    private static bool HasReflectionPayload(TerminalShaderReflection reflection)
    {
        return reflection.EntryPoints.Count > 0 || reflection.Resources.Count > 0;
    }

    private sealed class ResolvedInclude : Include
    {
        private readonly Dictionary<string, TerminalShaderFile> _files;

        public ResolvedInclude(Dictionary<string, TerminalShaderFile> files)
        {
            _files = files;
        }

        public Stream Open(IncludeType type, string fileName, Stream? parentStream)
        {
            _ = type;
            _ = parentStream;

            string normalized = NormalizeVirtualPath(fileName);
            if (!_files.TryGetValue(normalized, out TerminalShaderFile? file))
            {
                file = _files.Values.FirstOrDefault(candidate =>
                    candidate.VirtualPath.EndsWith("/" + normalized, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(candidate.VirtualPath, normalized, StringComparison.OrdinalIgnoreCase));
            }

            if (file is null)
            {
                throw new FileNotFoundException($"Resolved include '{fileName}' was not found.", fileName);
            }

            return new MemoryStream(Encoding.UTF8.GetBytes(file.Source), writable: false);
        }

        public void Close(Stream stream)
        {
            stream.Dispose();
        }

        public void Dispose()
        {
        }

        private static string NormalizeVirtualPath(string value)
        {
            return value
                .Replace('\\', '/')
                .Trim()
                .TrimStart('/');
        }
    }
}
