// Copyright (c) Royal Apps. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.
// RoyalTerminal.Terminal - Opt-in shell integration bootstrap script generation.

namespace RoyalTerminal.Terminal;

/// <summary>
/// Supported shell families for opt-in shell integration bootstrap scripts.
/// </summary>
public enum TerminalShellIntegrationBootstrapShell
{
    /// <summary>
    /// POSIX bash shell.
    /// </summary>
    Bash,

    /// <summary>
    /// Z shell.
    /// </summary>
    Zsh,

    /// <summary>
    /// Fish shell.
    /// </summary>
    Fish,

    /// <summary>
    /// PowerShell.
    /// </summary>
    PowerShell,
}

/// <summary>
/// Options used to generate a shell integration bootstrap script.
/// </summary>
public sealed record TerminalShellIntegrationBootstrapOptions
{
    /// <summary>
    /// Creates shell integration bootstrap options.
    /// </summary>
    /// <param name="shell">Shell family that will evaluate the generated script.</param>
    public TerminalShellIntegrationBootstrapOptions(TerminalShellIntegrationBootstrapShell shell)
    {
        Shell = shell;
    }

    /// <summary>
    /// Gets the shell family that will evaluate the generated script.
    /// </summary>
    public TerminalShellIntegrationBootstrapShell Shell { get; init; }

    /// <summary>
    /// Gets a value indicating whether OSC 7 current-working-directory reports should be emitted.
    /// </summary>
    public bool EmitWorkingDirectory { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether OSC 133 prompt and command markers should be emitted.
    /// </summary>
    public bool EmitSemanticPrompt { get; init; } = true;
}

/// <summary>
/// Builds opt-in shell integration bootstrap scripts without editing user dotfiles.
/// </summary>
public static class TerminalShellIntegrationBootstrapBuilder
{
    /// <summary>
    /// Builds a shell integration bootstrap script for the requested shell.
    /// </summary>
    /// <param name="options">Bootstrap options.</param>
    /// <returns>A script, or <see langword="null"/> when no events were requested.</returns>
    public static string? Build(TerminalShellIntegrationBootstrapOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.EmitWorkingDirectory && !options.EmitSemanticPrompt)
        {
            return null;
        }

        return options.Shell switch
        {
            TerminalShellIntegrationBootstrapShell.Bash => BuildBash(options),
            TerminalShellIntegrationBootstrapShell.Zsh => BuildZsh(options),
            TerminalShellIntegrationBootstrapShell.Fish => BuildFish(options),
            TerminalShellIntegrationBootstrapShell.PowerShell => BuildPowerShell(options),
            _ => throw new InvalidOperationException($"Unsupported shell integration shell '{options.Shell}'."),
        };
    }

    private static string BuildBash(TerminalShellIntegrationBootstrapOptions options)
    {
        List<string> lines =
        [
            "__royalterminal_urlencode() {",
            "  local LC_ALL=C input=\"$1\" output=\"\" i c hex",
            "  for (( i=0; i<${#input}; i++ )); do",
            "    c=\"${input:i:1}\"",
            "    case \"$c\" in",
            "      [a-zA-Z0-9.~_/-]) output+=\"$c\" ;;",
            "      *) printf -v hex '%%%02X' \"'$c\"; output+=\"$hex\" ;;",
            "    esac",
            "  done",
            "  printf '%s' \"$output\"",
            "}",
        ];

        if (options.EmitSemanticPrompt)
        {
            lines.AddRange(
            [
                "__royalterminal_preexec() {",
                "  if [ \"${__ROYALTERMINAL_COMMAND_ACTIVE:-0}\" = \"1\" ]; then return; fi",
                "  local history_line history_id cmd",
                "  history_line=\"$(HISTTIMEFORMAT= history 1 2>/dev/null)\" || history_line=\"\"",
                "  history_line=\"${history_line#\"${history_line%%[![:space:]]*}\"}\"",
                "  history_id=\"${history_line%%[[:space:]]*}\"",
                "  cmd=\"${history_line#\"$history_id\"}\"",
                "  cmd=\"${cmd#\"${cmd%%[![:space:]]*}\"}\"",
                "  if [ -z \"$cmd\" ] || [ \"$cmd\" = \"$history_line\" ]; then cmd=\"${BASH_COMMAND:-}\"; fi",
                "  case \"$cmd\" in __royalterminal_prompt_command*|__royalterminal_preexec*|history\\ *) return ;; esac",
                "  if [ -n \"$history_id\" ] && [ \"${__ROYALTERMINAL_LAST_HISTORY_ID:-}\" = \"$history_id\" ]; then return; fi",
                "  __ROYALTERMINAL_LAST_HISTORY_ID=\"$history_id\"",
                "  __ROYALTERMINAL_COMMAND_ACTIVE=1",
                "  printf '\\033]133;C;cmdline_url=%s\\007' \"$(__royalterminal_urlencode \"$cmd\")\"",
                "}",
            ]);
        }

        lines.Add("__royalterminal_prompt_command() {");
        lines.Add("  local exit_code=$?");
        if (options.EmitSemanticPrompt)
        {
            lines.Add("  if [ \"${__ROYALTERMINAL_COMMAND_ACTIVE:-0}\" = \"1\" ]; then printf '\\033]133;D;%s\\007' \"$exit_code\"; __ROYALTERMINAL_COMMAND_ACTIVE=0; fi");
        }

        if (options.EmitWorkingDirectory)
        {
            lines.Add("  printf '\\033]7;file://%s%s\\007' \"${HOSTNAME:-localhost}\" \"$(__royalterminal_urlencode \"$PWD\")\"");
        }

        if (options.EmitSemanticPrompt)
        {
            lines.Add("  printf '\\033]133;A\\007'");
        }

        lines.Add("  return \"$exit_code\"");
        lines.Add("}");
        if (options.EmitSemanticPrompt)
        {
            lines.Add("trap '__royalterminal_preexec' DEBUG");
        }

        lines.Add("case \";${PROMPT_COMMAND:-};\" in *__royalterminal_prompt_command*) ;; *) PROMPT_COMMAND=\"__royalterminal_prompt_command${PROMPT_COMMAND:+;$PROMPT_COMMAND}\" ;; esac");
        return string.Join('\n', lines);
    }

    private static string BuildZsh(TerminalShellIntegrationBootstrapOptions options)
    {
        List<string> lines =
        [
            "__royalterminal_urlencode() {",
            "  emulate -L zsh",
            "  local LC_ALL=C input=\"$1\" output=\"\" i c hex",
            "  for (( i = 1; i <= ${#input}; i++ )); do",
            "    c=\"${input[i]}\"",
            "    case \"$c\" in",
            "      [a-zA-Z0-9.~_/-]) output+=\"$c\" ;;",
            "      *) printf -v hex '%%%02X' \"'$c\"; output+=\"$hex\" ;;",
            "    esac",
            "  done",
            "  print -rn -- \"$output\"",
            "}",
            "__royalterminal_precmd() {",
            "  local exit_code=$?",
        ];

        if (options.EmitSemanticPrompt)
        {
            lines.Add("  if [[ \"${__ROYALTERMINAL_COMMAND_ACTIVE:-0}\" == \"1\" ]]; then printf '\\033]133;D;%s\\007' \"$exit_code\"; __ROYALTERMINAL_COMMAND_ACTIVE=0; fi");
        }

        if (options.EmitWorkingDirectory)
        {
            lines.Add("  printf '\\033]7;file://%s%s\\007' \"${HOST:-localhost}\" \"$(__royalterminal_urlencode \"$PWD\")\"");
        }

        if (options.EmitSemanticPrompt)
        {
            lines.Add("  printf '\\033]133;A\\007'");
        }

        lines.Add("}");
        if (options.EmitSemanticPrompt)
        {
            lines.AddRange(
            [
                "__royalterminal_preexec() {",
                "  local cmd=\"$1\"",
                "  __ROYALTERMINAL_COMMAND_ACTIVE=1",
                "  printf '\\033]133;C;cmdline_url=%s\\007' \"$(__royalterminal_urlencode \"$cmd\")\"",
                "}",
            ]);
        }

        lines.Add("autoload -Uz add-zsh-hook");
        lines.Add("add-zsh-hook precmd __royalterminal_precmd");
        if (options.EmitSemanticPrompt)
        {
            lines.Add("add-zsh-hook preexec __royalterminal_preexec");
        }

        return string.Join('\n', lines);
    }

    private static string BuildFish(TerminalShellIntegrationBootstrapOptions options)
    {
        List<string> lines = [];
        if (options.EmitSemanticPrompt)
        {
            lines.AddRange(
            [
                "function __royalterminal_preexec --on-event fish_preexec",
                "  set -g __ROYALTERMINAL_COMMAND_ACTIVE 1",
                "  printf '\\033]133;C;cmdline_url=%s\\007' (string escape --style=url -- $argv)",
                "end",
            ]);
        }

        lines.Add("function __royalterminal_prompt --on-event fish_prompt");
        lines.Add("  set -l exit_code $status");
        if (options.EmitSemanticPrompt)
        {
            lines.Add("  if set -q __ROYALTERMINAL_COMMAND_ACTIVE; printf '\\033]133;D;%s\\007' $exit_code; set -e __ROYALTERMINAL_COMMAND_ACTIVE; end");
        }

        if (options.EmitWorkingDirectory)
        {
            lines.Add("  printf '\\033]7;file://%s%s\\007' (hostname) (string escape --style=url -- $PWD)");
        }

        if (options.EmitSemanticPrompt)
        {
            lines.Add("  printf '\\033]133;A\\007'");
        }

        lines.Add("end");
        return string.Join('\n', lines);
    }

    private static string BuildPowerShell(TerminalShellIntegrationBootstrapOptions options)
    {
        List<string> lines =
        [
            "if (Test-Path function:\\prompt) { Copy-Item function:\\prompt function:\\__RoyalTerminalOriginalPrompt -Force }",
        ];

        if (options.EmitSemanticPrompt)
        {
            lines.AddRange(
            [
                "if (Get-Module -Name PSReadLine) {",
                "  function global:PSConsoleHostReadLine {",
                "    $rtLine = [Microsoft.PowerShell.PSConsoleReadLine]::ReadLine($host.Runspace, $ExecutionContext)",
                "    if (-not [string]::IsNullOrWhiteSpace($rtLine)) {",
                "      $rtCommand = [Uri]::EscapeDataString($rtLine)",
                "      [Console]::Write(\"`e]133;C;cmdline_url=$rtCommand`a\")",
                "    }",
                "    $rtLine",
                "  }",
                "}",
            ]);
        }

        lines.Add("function global:prompt {");
        lines.Add("  $rtCommandSucceeded = $?");
        lines.Add("  $rtLastExitCode = $global:LASTEXITCODE");
        lines.Add("  $rtExitCode = if ($rtCommandSucceeded) { 0 } elseif ($rtLastExitCode -is [int]) { $rtLastExitCode } else { 1 }");
        lines.Add("  if (-not $rtCommandSucceeded) {");
        lines.Add("    Write-Error -Message __RoyalTerminalRestoreStatus -ErrorAction Ignore");
        lines.Add("    $rtPrompt = if (Test-Path function:\\__RoyalTerminalOriginalPrompt) { & __RoyalTerminalOriginalPrompt } else { \"PS $($executionContext.SessionState.Path.CurrentLocation)> \" }");
        lines.Add("  } else {");
        lines.Add("    $rtPrompt = if (Test-Path function:\\__RoyalTerminalOriginalPrompt) { & __RoyalTerminalOriginalPrompt } else { \"PS $($executionContext.SessionState.Path.CurrentLocation)> \" }");
        lines.Add("  }");
        if (options.EmitSemanticPrompt)
        {
            lines.Add("  [Console]::Write(\"`e]133;D;$rtExitCode`a\")");
        }

        if (options.EmitWorkingDirectory)
        {
            lines.Add("  $rtPath = [Uri]::EscapeDataString((Get-Location).ProviderPath).Replace('%2F', '/')");
            lines.Add("  [Console]::Write(\"`e]7;file://$env:COMPUTERNAME/$rtPath`a\")");
        }

        if (options.EmitSemanticPrompt)
        {
            lines.Add("  [Console]::Write(\"`e]133;A`a\")");
        }

        lines.Add("  $rtPrompt");
        lines.Add("}");
        return string.Join('\n', lines);
    }
}
