#!/usr/bin/env bash
# Two independent servers. Never replay original commands while rebuilding the second layout.
set -euo pipefail
cd "$(dirname "$0")/../.."; umask 077
d=$(mktemp -d); p=; cleanup(){ if [[ -n $p ]];then kill "$p" 2>/dev/null || :;wait "$p" 2>/dev/null || :;fi;rm -rf -- "$d";};trap cleanup EXIT
cli=(dotnet samples/RoyalTerminal.Multiplexing.Cli/bin/Release/net10.0/royalterminal-mux.dll)
start(){
  export ROYALTERMINAL_MUX_TOKEN="$(openssl rand -hex 32)" ROYALTERMINAL_MUX_SOCKET="$d/$1.sock"
  dotnet samples/RoyalTerminal.Multiplexing.Server/bin/Release/net10.0/RoyalTerminal.Multiplexing.Server.dll "--Kestrel:Endpoints:Local:Url=http://unix:$ROYALTERMINAL_MUX_SOCKET" >"$d/host.log" 2>&1 & p=$!
  for i in {1..150};do if [[ -S $ROYALTERMINAL_MUX_SOCKET ]] && curl -fsS --unix-socket "$ROYALTERMINAL_MUX_SOCKET" http://localhost/healthz >/dev/null;then return;fi;sleep .1;done;return 1
}
api(){ curl --max-time 8 -fsS --unix-socket "$ROYALTERMINAL_MUX_SOCKET" -H "Authorization: Bearer $ROYALTERMINAL_MUX_TOKEN" -H 'Content-Type: application/json' --data "$(jq -cn --arg m "$1" --argjson a "$2" '{method:$m,arguments:$a}')" http://localhost/api/call; }
start source
s=$(api session.create '{"label":"archive-test"}'|jq -er .id)
w=$(api session.new_window "{\"sessionId\":\"$s\",\"label\":\"Diagnostics\"}"|jq -er .id)
create(){ api block.create "$(jq -cn --arg s "$s" --arg msg "$1" '{sessionId:$s,block:{label:$msg,transport:"pipe",command:{fileName:"/usr/bin/printf",arguments:["%s",$msg]}}}')"|jq -er .id; }
a=$(create 'ALPHA-🦀'); b=$(create 'BETA-🪟'); c=$(create 'Floating'); f=$(create 'Hidden')
api block.place "$(jq -cn --arg s "$s" --arg w "$w" --arg b "$c" '{sessionId:$s,blockId:$b,windowId:$w,kind:"floating",bounds:{x:1000,y:2000,width:4200,height:5100}}')" >/dev/null
api block.place "$(jq -cn --arg s "$s" --arg w "$w" --arg b "$f" '{sessionId:$s,blockId:$b,windowId:$w,kind:"unplaced"}')" >/dev/null
for i in {1..80};do [[ $(api session.view "{\"sessionId\":\"$s\"}"|jq '[.blocks[]|select(.state=="Exited")]|length') == 4 ]] && break;sleep .1;done
observer=$(api share.create "{\"sessionId\":\"$s\",\"role\":\"observer\"}"|jq -er .token)
ROYALTERMINAL_MUX_TOKEN=$observer "${cli[@]}" archive save "$s" "$d/state.rtw"
[[ $(stat -c %a "$d/state.rtw") == 600 ]]
if "${cli[@]}" archive save "$s" "$d/state.rtw";then echo 'Clobbered existing destination';exit 1;fi
sha=$(sha256sum "$d/state.rtw");if ROYALTERMINAL_MUX_TOKEN=invalid "${cli[@]}" archive save "$s" "$d/state.rtw" --replace;then echo 'Invalid credential replaced an archive';exit 1;fi
[[ $(sha256sum "$d/state.rtw") == "$sha" ]]
kill "$p";wait "$p" || :;p=;unset ROYALTERMINAL_MUX_TOKEN ROYALTERMINAL_MUX_SOCKET
"${cli[@]}" archive list "$d/state.rtw"|jq -e --arg s "$s" '.id==$s and (.blocks|length)==4 and (.windows[0].layout.first.blockId!=null)' >/dev/null
"${cli[@]}" archive view "$d/state.rtw" "$a"|grep -F 'ALPHA-🦀'
"${cli[@]}" archive view "$d/state.rtw" "$b"|grep -F 'BETA-🪟'
"${cli[@]}" restore-layout "$d/state.rtw" --plan|jq -e '.freshShells==4 and .windows==2 and .commandsReplayed==false' >/dev/null
start target
# Four exited printf processes become four freshly started default PTY shells. No archived command, working directory, or input is executed.
"${cli[@]}" restore-layout "$d/state.rtw" Recovered --fresh-shells >"$d/map.json"
new=$(jq -er --arg old "$s" 'select(.sourceSession==$old and .sessionId!=$old and .commandsReplayed==false).sessionId' "$d/map.json");neww=$(jq -er --arg id "$w" '.windows[$id]' "$d/map.json");newa=$(jq -er --arg id "$a" '.blocks[$id]' "$d/map.json")
api session.view "{\"sessionId\":\"$new\"}"|jq -e --arg w "$neww" --arg old "$a" --arg a "$newa" '.label=="Recovered" and .windows[0].layout.first.blockId==$a and $a!=$old and .windows[1].id==$w and .windows[1].label=="Diagnostics" and ([.blocks[]|select(.state=="Running")]|length)==4 and ([.blocks[]|select(.placement=="Unplaced" and .windowId==$w)]|length)==1 and ([.blocks[]|select(.placement=="Floating" and .floatingBounds.width==4200 and .floatingBounds.y==2000 and .windowId==$w)]|length)==1' >/dev/null
printf x >> "$d/state.rtw";if "${cli[@]}" restore-layout "$d/state.rtw" Rejected --fresh-shells;then echo 'Corrupt file started processes';exit 1;fi
api session.list '{}'|jq -e '.sessions|length==1' >/dev/null
echo 'Verified four independent parser checkpoints, offline inspection, fresh-layout reconstruction, and rejection without failed-import side effects.'
