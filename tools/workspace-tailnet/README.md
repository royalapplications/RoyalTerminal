# Workspace tailnet ingress and discovery

Optional standard-library Go helper. Discovery operates on a local `tailscale status --json` snapshot. Linux `serve` is a private Unix reverse proxy behind **Tailscale Serve**, not a replacement for tailscaled and not Tailscale Funnel. No tailnet token is requested or stored here.

```sh
cd tools/workspace-tailnet
go test -race ./...
go build -trimpath -o workspace-tailnet .
tailscale status --json | ./workspace-tailnet discover
tailscale status --json | ./workspace-tailnet discover -probe
```

Passive output contains only eligible online hostnames/endpoints and marks them unverified. `-probe` dials the peer's addresses from a local status snapshot (not arbitrary DNS), verifies its MagicDNS certificate, disallows proxy variables and redirects, and asks for `/.well-known/royalterminal-workspace`. It sends no workspace credential. At most 128 candidates, six concurrent probes, 4 KiB per manifest and a 30-second overall deadline. A successful probe discovers a service; it does not authorize a terminal operation.

## Linux ingress

Use the personal-service publisher/installer in `scripts/workspace` to obtain a *running* Unix-socket-only backend. Replace the hostname and login below with your own. Run the gateway as the ordinary owner of the personal workspace, not root. Install a supported, patched tailscaled with HTTP-over-Unix Serve targets; do not use a TCP forwarder in place of HTTP Serve.

```sh
install -d -m 0700 "$XDG_RUNTIME_DIR/royalterminal-gateway"
./workspace-tailnet serve \
  -listen "$XDG_RUNTIME_DIR/royalterminal-gateway/serve.sock" \
  -backend "$XDG_RUNTIME_DIR/royalterminal-mux/host.sock" \
  -host workstation.example-tailnet.ts.net \
  -allow 'alice@example.com'
# In another shell, with permission to configure this node's Tailscale Serve:
tailscale serve --bg --https=443 "unix:$XDG_RUNTIME_DIR/royalterminal-gateway/serve.sock"
tailscale serve status
```

Review existing Serve mounts before changing them. This example uses the root of the HTTPS virtual host. The HTTP manifest is deliberately nonsecret, but still requires an allowed identity. All broker API and WebSocket requests retain their original workspace capability. Origin validation happens against the pinned HTTPS host before proxy rewriting. Requests are never given an administrator token by the gateway. Incoming login headers are trusted only when the immediate Linux socket peer is UID 0. The backend socket must belong to the same OS user as the gateway. Local root is in the trust boundary; an arbitrary UID-0 process is not cryptographically distinguished from tailscaled. Multiple or absent login headers, foreign origins, symlinked or unsafe directories, and an unexpected backend owner are rejected. Do not expose this socket with an unrelated identity-injecting proxy, or expose the broker on an unauthenticated TCP listener.

CI uses actual Linux peer credentials, HTTP over Unix and a real .NET broker. It does not enroll a node in your tailnet or attest to its ACLs, DNS, certificates, daemon confinement or device approval. Confirm acceptance from an allowed and a denied node, including a real WebSocket, before a real deployment. To undo this example remove the specific Serve mount (`tailscale serve --https=443 off` only when you own that listener), then stop the gateway. Do not reset all of a shared node's Serve configuration.

References: [Serve identity headers](https://tailscale.com/docs/features/tailscale-serve), [CLI syntax](https://tailscale.com/docs/reference/tailscale-cli/serve), [upstream Unix target support](https://github.com/tailscale/tailscale/blob/main/ipn/serve.go), [vendor security bulletins](https://tailscale.com/security-bulletins).
