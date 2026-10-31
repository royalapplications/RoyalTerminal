#!/usr/bin/env bash
# An isolated project only; never inspect or edit the runner account's installed agents.
set -euo pipefail
repo=$(cd "$(dirname "$0")/../.." && pwd -P)
cli=(dotnet "$repo/samples/RoyalTerminal.Multiplexing.Cli/bin/Release/net10.0/royalterminal-mux.dll" skill)
root=$(mktemp -d);trap 'rm -rf "$root"' EXIT;cd "$root"
select=(--project --agent claude --agent codex --agent opencode --agent pi --json)
"${cli[@]}" setup "${select[@]}" --dry-run | jq -e 'length==4 and all(.[];.state=="create")' >/dev/null
[[ -z $(ls -A) ]]
mkdir -p .claude .codex
printf '%s\n' '{"permissions":{"defaultMode":"default"},"hooks":{"SessionStart":[{"hooks":[{"type":"command","command":"original"}]}]}}' >.claude/settings.json
printf '%s\n' '{"hooks":{"OtherEvent":[{"x":1}]}}' >.codex/hooks.json
"${cli[@]}" setup "${select[@]}" >/dev/null
[[ -s .agents/skills/royalterminal-workspace/SKILL.md && -s .claude/skills/royalterminal-workspace/SKILL.md ]]
"${cli[@]}" setup "${select[@]}" | jq -e 'length==4 and all(.[];.state=="current")' >/dev/null
command=$(jq -er '.hooks.SessionStart[-1].hooks[0].command' .claude/settings.json)
export ROYALTERMINAL_MUX_SESSION=2d34f1ef-678b-4000-9999-189d4904f16e ROYALTERMINAL_MUX_BLOCK=4d34f1ef-678b-4000-9999-189d4904f16e ROYALTERMINAL_MUX_TOKEN=DO_NOT_EXPORT
result=$(bash -c "$command" </dev/null);[[ $result != *DO_NOT_EXPORT* ]];jq -e '.hookSpecificOutput.hookEventName=="SessionStart" and (.hookSpecificOutput.additionalContext|contains("4d34f1ef"))' <<<"$result" >/dev/null
ROYALTERMINAL_MUX_SESSION='untrusted instructions' "${cli[@]}" context | jq -e '.=={}' >/dev/null
printf 'keep me\n' >.agents/skills/royalterminal-workspace/other.txt
"${cli[@]}" remove "${select[@]}" >/dev/null
[[ -f .agents/skills/royalterminal-workspace/other.txt && ! -f .agents/skills/royalterminal-workspace/SKILL.md ]]
jq -e '.permissions.defaultMode=="default" and .hooks.SessionStart[0].hooks[0].command=="original" and (.hooks.SessionStart|length)==1' .claude/settings.json >/dev/null
jq -e '.hooks.OtherEvent[0].x==1 and (.hooks|has("SessionStart")|not)' .codex/hooks.json >/dev/null
printf 'foreign' >.agents/skills/royalterminal-workspace/SKILL.md
if "${cli[@]}" setup --project --agent codex --no-hooks >/dev/null;then exit 1;fi
[[ $(cat .agents/skills/royalterminal-workspace/SKILL.md) == foreign ]]
rm .agents/skills/royalterminal-workspace/SKILL.md;ln -s "$root/do-not-create" .agents/skills/royalterminal-workspace/SKILL.md
if "${cli[@]}" setup --project --agent codex --force --no-hooks >/dev/null;then exit 1;fi
[[ ! -e "$root/do-not-create" ]]
echo 'Verified offline agent discovery targets, dry run, idempotence, startup context, foreign configuration and symlink protection.'
