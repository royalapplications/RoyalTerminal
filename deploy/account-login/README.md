# Experimental account-separated Linux service

This profile is for a disposable Debian/Ubuntu test machine with systemd, Linux Unix-socket ACLs, a local passwd/shadow account, Debian-style PAM include files, Python 3, `acl`, and the .NET 10 ASP.NET Core runtime at `/usr/bin/dotnet`. It is **not** an internet password server. It does not modify an existing system authentication policy or log users in interactively. See [systemd.exec](https://www.freedesktop.org/software/systemd/man/latest/systemd.exec.html), [systemd.socket](https://www.freedesktop.org/software/systemd/man/latest/systemd.socket.html), and [the Rex login-server reference](https://www.superlogical.com/rex/docs/linux/login-server).

## Install and authorize one account

```sh
# Build from the reviewed source as a regular user. This installer only accepts trusted publish output.
npm --prefix samples/RoyalTerminal.Multiplexing.Server ci
npm --prefix samples/RoyalTerminal.Multiplexing.Server run build
dotnet publish samples/RoyalTerminal.Multiplexing.Server -c Release -o "$PWD/account-publish"
sudo deploy/account-login/install-debian.sh "$PWD/account-publish"
```

Installation refuses to overwrite an existing profile, leaves the root-owned account mapping empty and starts no units. Review all installed files before enabling a listener. Configure `/etc/royalterminal-login/accounts.json`, owned by root and mode 0600. For example, for an existing target login named `alice` that should accept *only* its own local UID, use `id -u alice`, then write `{"version":1,"accounts":{"alice":[1001]}}` with the actual numeric UID in place of 1001. An allowed UID grants the ability to connect, **not** access to a terminal without a second workspace credential.

```sh
sudo /usr/bin/python3 -I /usr/local/libexec/royalterminal-account/access.py check
sudo systemctl enable --now royalterminal-login@alice.socket
# As an acl-authorized local user:
curl --unix-socket /run/royalterminal-login/alice.sock http://localhost/healthz
# As alice, authenticate locally. Do not copy this administrator token to other users.
export ROYALTERMINAL_MUX_TOKEN="$(cat /var/lib/royalterminal-account-alice/token)"
royalterminal-mux --socket /run/royalterminal-login/alice.sock sessions
```

A cold client activates `royalterminal-login@alice.service`. The proxy requires `royalterminal-account@alice.service`. The admission helper refuses disabled mappings, root/system/locked/passwordless targets and unsuitable shells. systemd starts the broker as the selected OS account with the `royalterminal-workspace` PAM account/session stack. The proxy waits for `/healthz` on the private socket before forwarding. The per-account state directory and token are not shared. Neither the proxy nor the HTTP parser runs as root. The root helper provisions and checks policy only; there is no client-supplied OS account name passed to a shell. This profile deliberately refuses UID 65534 and accounts below UID 1000.

## Revocation, inspection and removal

After atomically replacing the policy, `sudo /usr/bin/python3 -I /usr/local/libexec/royalterminal-account/access.py acl alice` replaces the socket's entire access ACL. Removing an account mapping and applying the ACL blocks **new** connects. Removing a filesystem ACL does not disconnect already-open streams; stop/restart the entry proxy and revoke workspace shares to remove live access. Root and the target OS account themselves are not security adversaries of their own broker. An expired account is checked at cold startup, not on every frame of a pre-existing session.

`systemctl restart royalterminal-login@alice.service` replaces the entry proxy without deliberately stopping the account host or PTYs. To disable the profile: disable/stop each `royalterminal-login@NAME.socket`, stop the corresponding proxy, then explicitly stop `royalterminal-account@NAME.service` **only when its terminal processes may be terminated**. Remove only the new unit templates, `/etc/pam.d/royalterminal-workspace`, `/usr/local/libexec/royalterminal-account`, `/opt/royalterminal-account`, and the new policy directory after reviewing them. Preserve or deliberately destroy private `/var/lib/royalterminal-account-NAME` state separately; `systemctl daemon-reload` refreshes the unit definitions. Never delete unrelated distro PAM files.

`ci.sh` is destructive and guarded for a disposable hosted VM; do not run it on a workstation. It creates two test users, exercises separate credentials and child UIDs, restarts the entry proxy, removes an ACL, expires one account and asserts cold activation fails. FreeBSD, directory-backed accounts, and interactive PAM conversations are outside this profile.
