#!/usr/bin/env bash
# Save a real server-owned workspace as an observer, then kill the server before reading any archived screen.
set -euo pipefail
cd "$(dirname "$0")/../.."; umask 077
d=$(mktemp -d); p=; cleanup(){ if [[ -n $p ]];then kill "$p" 2>/dev/null || :;wait "$p" 2>/dev/null || :;fi;rm -rf -- "$d";};trap cleanup EXIT
export ROYALTERMINAL_MUX_TOKEN="$(openssl rand -hex 32)" ROYALTERMINAL_MUX_SOCKET="$d/host.sock"
cli=(dotnet samples/RoyalTerminal.Multiplexing.Cli/bin/Release/net10.0/royalterminal-mux.dll)
dotnet samples/RoyalTerminal.Multiplexing.Server/bin/Release/net10.0/RoyalTerminal.Multiplexing.Server.dll "--Kestrel:Endpoints:Local:Url=http://unix:$d/host.sock" >"$d/host.log" 2>&1 & p=$!
for i in {1..150};do if [[ -S $d/host.sock ]] && curl -fsS --unix-socket "$d/host.sock" http://localhost/healthz >/dev/null;then break;fi;sleep .1;done
api(){ curl --max-time 8 -fsS --unix-socket "$d/host.sock" -H "Authorization: Bearer $ROYALTERMINAL_MUX_TOKEN" -H 'Content-Type: application/json' --data "$(jq -cn --arg m "$1" --argjson a "$2" '{method:$m,arguments:$a}')" http://localhost/api/call; }
s=$(api session.create '{"label":"archive-test"}'|jq -er .id)
create(){ api block.create "$(jq -cn --arg s "$s" --arg msg "$1" '{sessionId:$s,block:{label:$msg,transport:"pipe",command:{fileName:"/usr/bin/printf",arguments:["%s",$msg]}}}')"|jq -er .id; }
a=$(create 'ALPHA-🦀'); b=$(create 'BETA-🪟')
for i in {1..80};do [[ $(api session.view "{\"sessionId\":\"$s\"}"|jq '[.blocks[]|select(.state=="Exited")]|length') == 2 ]] && break;sleep .1;done
observer=$(api share.create "{\"sessionId\":\"$s\",\"role\":\"observer\"}"|jq -er .token)
ROYALTERMINAL_MUX_TOKEN=$observer "${cli[@]}" archive save "$s" "$d/state.rtw"
[[ $(stat -c %a "$d/state.rtw") == 600 ]]
if "${cli[@]}" archive save "$s" "$d/state.rtw";then echo 'Clobbered existing destination';exit 1;fi
sha=$(sha256sum "$d/state.rtw");if ROYALTERMINAL_MUX_TOKEN=invalid "${cli[@]}" archive save "$s" "$d/state.rtw" --replace;then echo 'Invalid credential replaced an archive';exit 1;fi
[[ $(sha256sum "$d/state.rtw") == "$sha" ]]
kill "$p";wait "$p" || :;p=;unset ROYALTERMINAL_MUX_TOKEN ROYALTERMINAL_MUX_SOCKET
"${cli[@]}" archive list "$d/state.rtw"|jq -e --arg s "$s" '.id==$s and (.blocks|length)==2 and (.windows[0].layout.first.blockId!=null)' >/dev/null
"${cli[@]}" archive view "$d/state.rtw" "$a"|grep -F 'ALPHA-🦀'
"${cli[@]}" archive view "$d/state.rtw" "$b"|grep -F 'BETA-🪟'
printf x >> "$d/state.rtw";if "${cli[@]}" archive view "$d/state.rtw" "$a";then echo 'Corrupted archive accepted';exit 1;fi
echo 'Verified two parser checkpoints and split layout survive actual server shutdown without credentials or process replay.'
