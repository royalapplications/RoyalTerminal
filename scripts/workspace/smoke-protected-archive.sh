#!/usr/bin/env bash
# Real age binaries and a real broker; no enrollment, production keys or outside network.
set -euo pipefail
umask 077
cd "$(dirname "$0")/../.."
d=$(mktemp -d); pid=''
cleanup(){ if [[ -n $pid ]]; then kill "$pid" 2>/dev/null || :; wait "$pid" || :; fi; rm -rf -- "$d"; }; trap cleanup EXIT
for n in a b stranger; do age-keygen -o "$d/$n.key" 2>/dev/null; done
a=$(age-keygen -y "$d/a.key"); b=$(age-keygen -y "$d/b.key")
cli=(dotnet samples/RoyalTerminal.Multiplexing.Cli/bin/Release/net10.0/royalterminal-mux.dll)
export ROYALTERMINAL_MUX_TOKEN=$(openssl rand -hex 32) ROYALTERMINAL_MUX_SOCKET="$d/host.sock" ROYALTERMINAL_MUX_AGE=$(command -v age)
dotnet samples/RoyalTerminal.Multiplexing.Server/bin/Release/net10.0/RoyalTerminal.Multiplexing.Server.dll "--Kestrel:Endpoints:Private:Url=http://unix:$ROYALTERMINAL_MUX_SOCKET" >"$d/broker.log" 2>&1 & pid=$!
for i in {1..150}; do [[ -S $ROYALTERMINAL_MUX_SOCKET ]] && break;kill -0 "$pid" || exit 1;sleep .05;done
api(){ curl -fsS --max-time 8 --unix-socket "$ROYALTERMINAL_MUX_SOCKET" -H "Authorization: Bearer $ROYALTERMINAL_MUX_TOKEN" -H 'Content-Type: application/json' --data "$(jq -cn --arg m "$1" --argjson a "$2" '{method:$m,arguments:$a}')" http://localhost/api/call; }
s=$(api session.create '{"label":"SENSITIVE-LABEL-71"}'|jq -er .id)
id=$(api block.create "$(jq -cn --arg s "$s" '{sessionId:$s,block:{transport:"pipe",label:"receipt",command:{fileName:"/usr/bin/printf",arguments:["%s","zażółć-🦀"]}}}')"|jq -er .id)
for i in {1..100};do [[ $(api session.view "{\"sessionId\":\"$s\"}"|jq -r '.blocks[0].state') == Exited ]] && break;sleep .05;done
f=$d/work.rtw.age
"${cli[@]}" archive save "$s" "$f" --recipient "$a" --recipient "$b"
[[ $(stat -c %a "$f") == 600 && $(head -n1 "$f") == age-encryption.org/v1 ]]; ! grep -aqF 'SENSITIVE-LABEL-71' "$f" || exit 1
# Interoperability works in both directions, not just encryption/decryption by the wrapper.
age -d -i "$d/a.key" -o "$d/interop.rtw" "$f";"${cli[@]}" archive list "$d/interop.rtw"|jq -e --arg s "$s" '.id==$s' >/dev/null
age -r "$b" -o "$d/external.age" "$d/interop.rtw"
for identity in a b; do "${cli[@]}" archive view "$f" "$id" --identity "$d/$identity.key" | grep -F 'zażółć-🦀';done
"${cli[@]}" archive list "$d/external.age" --identity "$d/b.key"|jq -e '.label=="SENSITIVE-LABEL-71"' >/dev/null
before=$(sha256sum "$f")
if "${cli[@]}" archive save "$s" "$f" --replace;then exit 1;fi
if ROYALTERMINAL_MUX_AGE=/does/not/exist "${cli[@]}" archive save "$s" "$f" --recipient "$a" --replace;then exit 1;fi
[[ $(sha256sum "$f") == "$before" && -z $(find "$d" -name '.royalworkspace-*.tmp' -print -quit) ]]
reject(){ if "$@" > "$d/rejected.out" 2>"$d/rejected.err";then echo 'Unexpected success' >&2;exit 1;fi;test ! -s "$d/rejected.out"; }
reject "${cli[@]}" archive list "$f" --identity "$d/stranger.key"
reject "${cli[@]}" archive list "$d/interop.rtw" --identity "$d/a.key"
head -c -1 "$f" > "$d/truncated.age";cp "$f" "$d/trailing.age";printf x >> "$d/trailing.age"
python3 - "$f" "$d/tampered.age" <<'PY'
import pathlib,sys
b=bytearray(pathlib.Path(sys.argv[1]).read_bytes());b[-32]^=1;pathlib.Path(sys.argv[2]).write_bytes(b)
PY
for n in truncated trailing tampered;do reject "${cli[@]}" restore-layout "$d/$n.age" Untrusted --fresh-shells --identity "$d/a.key";done
# A valid decrypted RTMUX stream alone is not sufficient: simulate a helper discovering a failure at EOF.
cat > "$d/bad-filter" <<SH
#!/bin/sh
if [ -n "\${ROYALTERMINAL_MUX_TOKEN:-}" ];then exit 0;fi
/bin/cat >/dev/null
/bin/cat '$d/interop.rtw'
exit 9
SH
chmod 700 "$d/bad-filter"
reject env ROYALTERMINAL_MUX_AGE="$d/bad-filter" "${cli[@]}" restore-layout "$f" Untrusted --fresh-shells --identity "$d/a.key"
api session.list '{}' | jq -e '.sessions|length==1' >/dev/null
"${cli[@]}" restore-layout "$f" Restored --fresh-shells --identity "$d/a.key"|jq -e '.commandsReplayed==false and (.blocks|length)==1' >/dev/null
kill "$pid";wait "$pid" || :;pid='';unset ROYALTERMINAL_MUX_TOKEN ROYALTERMINAL_MUX_SOCKET
"${cli[@]}" restore-layout "$f" --plan --identity "$d/a.key"|jq -e '.freshShells==1' >/dev/null
printf '%s\n' 'Verified two recipients, external age interoperability, Unicode screen recovery, no plaintext fallback, late-failure rollback and offline restore planning.'
