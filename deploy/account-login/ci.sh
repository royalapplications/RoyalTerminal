#!/usr/bin/env bash
# Run only on a disposable GitHub-hosted VM; creates two OS users and installs a test-only profile.
set -euo pipefail
[[ $EUID == 0 && ${ROYALTERMINAL_DISPOSABLE_CI:-} == github-hosted && -d /run/systemd/system && $# == 1 ]] || { echo 'Disposable systemd runner required.' >&2; exit 2; }
base=$(cd "$(dirname "$0")" && pwd -P)
for n in rtworkalpha rtworkbeta; do ! getent passwd "$n" >/dev/null || exit 1; done
"$base/install-debian.sh" "$1"
cleanup(){ for n in rtworkalpha rtworkbeta; do systemctl stop "royalterminal-login@$n.socket" "royalterminal-login@$n.service" "royalterminal-account@$n.service" >/dev/null 2>&1 || :; done; }
trap cleanup EXIT
for n in rtworkalpha rtworkbeta; do useradd -m -s /bin/bash "$n"; printf '%s:%s\n' "$n" "$(openssl rand -hex 32)" | chpasswd; done
a=rtworkalpha; b=rtworkbeta; ua=$(id -u "$a"); ub=$(id -u "$b"); policy=/etc/royalterminal-login/accounts.json; addr=/run/royalterminal-login
printf '{"version":1,"accounts":{"%s":[%s],"%s":[%s]}}\n' "$a" "$ua" "$b" "$ub" >"$policy"
/usr/bin/python3 -I /usr/local/libexec/royalterminal-account/access.py check
systemctl start "royalterminal-login@$a.socket" "royalterminal-login@$b.socket"
! systemctl is-active --quiet "royalterminal-account@$a.service" || { echo 'Not cold' >&2; exit 1; }
if runuser -u "$b" -- curl -fsS --max-time 2 --unix-socket "$addr/$a.sock" http://localhost/healthz >/dev/null 2>&1; then echo 'Foreign principal reached socket'; exit 1; fi
for n in "$a" "$b"; do
  runuser -u "$n" -- curl -fsS --max-time 40 --unix-socket "$addr/$n.sock" http://localhost/healthz | jq -e . >/dev/null || { journalctl --no-pager -u "royalterminal-account@$n.service" -n 30; exit 1; }
  pid=$(systemctl show -p MainPID --value "royalterminal-account@$n.service"); [[ $(awk '/^Uid:/{print $2}' "/proc/$pid/status") == "$(id -u "$n")" ]]
done
at=$(cat "/var/lib/royalterminal-account-$a/token");bt=$(cat "/var/lib/royalterminal-account-$b/token");[[ $at != "$bt" ]]
req(){ local n=$1 auth=$2 path=$3;shift 3;runuser -u "$n" -- curl -fsS --max-time 8 --unix-socket "$addr/$n.sock" -H "Authorization: Bearer $auth" -H 'Content-Type: application/json' "$@" "http://localhost/$path"; }
s=$(req "$a" "$at" api/sessions --data '{"label":"owned-by-alpha"}' | jq -er .id)
req "$b" "$bt" api/sessions | jq -e '.sessions|length==0' >/dev/null
if req "$b" "$at" api/context >/dev/null 2>&1; then echo 'Foreign bearer was accepted';exit 1;fi
block=$(req "$a" "$at" "api/sessions/$s/blocks" --data '{"label":"identity","transport":"pipe","command":{"fileName":"/usr/bin/id","arguments":["-u"]}}'|jq -er .id)
found=0;for i in {1..40};do received=$(req "$a" "$at" "api/sessions/$s/blocks/$block/capture"|jq -r .content);if grep -Eq "^[[:space:]]*$ua[[:space:]]*$" <<<"$received";then found=1;break;fi;sleep .1;done
[[ $found == 1 ]] || { echo 'Wrong child identity' >&2; exit 1; }
pid=$(systemctl show -p MainPID --value "royalterminal-account@$a.service");systemctl restart "royalterminal-login@$a.service";[[ $(systemctl show -p MainPID --value "royalterminal-account@$a.service") == "$pid" ]]
req "$a" "$at" "api/sessions/$s"|jq -e --arg s "$s" '.id==$s' >/dev/null
printf '{"version":1,"accounts":{"%s":[%s]}}\n' "$b" "$ub" >"$policy"; /usr/bin/python3 -I /usr/local/libexec/royalterminal-account/access.py acl "$a"
if req "$a" "$at" api/context >/dev/null 2>&1; then echo 'Revoked principal retained new socket access';exit 1;fi
systemctl stop "royalterminal-login@$b.service" "royalterminal-account@$b.service";chage -E 2000-01-01 "$b"
if req "$b" "$bt" api/context >/dev/null 2>&1;then echo 'Expired target started';exit 1;fi
if systemctl is-active --quiet "royalterminal-account@$b.service";then echo 'Expired account host is active';exit 1;fi
echo 'Two real local users, distinct capabilities, child UID, proxy restart, ACL revocation, and expired-account denial verified.'
