#!/bin/bash
set -euo pipefail
[[ ${GITHUB_ACTIONS:-} == true && ${RUNNER_ENVIRONMENT:-} == github-hosted ]] || { echo 'Use a disposable hosted runner.' >&2;exit 2; }
out="$RUNNER_TEMP/personal-mac";WORKSPACE_CODESIGN_IDENTITY=- bash deploy/macos/package.sh "$out"
app="$out/RoyalTerminal Workspace.app";entry="$app/Contents/MacOS/Workspace";trap '"$entry" stop >/dev/null 2>&1 || :' EXIT
"$entry" start;"$entry" status
s=$("$entry" cli new local-proof|jq -er .id)
b=$("$entry" cli run "$s" -- /bin/sh -c 'echo READY; read x; echo "ALIVE:$x"; exit 37'|jq -er .id)
# Close a real GUI process; launching the client must not own or stop its broker.
"$entry" open >"$RUNNER_TEMP/gui.log" 2>&1 & gui=$!;sleep 3;kill -0 "$gui";kill -TERM "$gui";wait "$gui" || :
"$entry" cli send "$s" "$b" $'after-detach\r' >/dev/null;status=0;"$entry" cli wait "$s" "$b" >/dev/null || status=$?;[[ $status == 37 ]]
"$entry" cli capture "$s" "$b" | jq -e '.content|contains("ALIVE:after-detach")' >/dev/null
codesign --verify --deep --strict "$app";printf 'tampered' >>"$app/Contents/Resources/personal.sh";if codesign --verify --deep --strict "$app" >/dev/null 2>&1;then exit 1;fi
# The exported ZIP was made before the intentional tamper test. Dispose of the mutated test bundle.
echo 'Verified self-contained launch, private broker, actual PTY exit after GUI shutdown, and rejection of changed bundle contents.'
