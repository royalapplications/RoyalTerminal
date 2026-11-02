# Workspace implementation review · 2 November 2026

Baseline `c4653b9`. This review covers an experimental RoyalTerminal implementation, not an audit of Rex's source or a claim of wire or pixel compatibility.

| Area | Finding and change | Regression evidence |
| --- | --- | --- |
| Terminal input | A controller could start an HTTP request while authorized, lose its grant while the body was being read, and still reach input dispatch. Revalidate the original grant and cancellation after the read. | `InputAdmissionTests`: revoke inside the request stream; neither revoked controllers nor readers reach dispatch. This does not retract bytes already admitted. |
| Credential transport | The shared managed endpoint accepted plaintext HTTP to remote DNS names and LAN addresses. Enforce HTTPS outside literal loopback and localhost before building HTTP, checkpoint or WebSocket requests. Unix socket synthetic origins still work. | `CredentialTransportTests` and existing local broker integrations. |
| Peer routes | A full 515-byte read could be trimmed and accepted even when more bytes followed. Accept only one 32–512 byte printable credential with optional LF/CRLF; reject oversized files before trimming, clear scratch bytes and report a sanitized error. | `PeerCredentialFileTests`, including valid maximum length and trailing payload. |
| Status aggregation | One status scan materialized the entire session for each terminal. Look up a block by identity inside its session instead. | `ScopedLookupTests` covers foreign, moved and closed blocks; no benchmark or percent speedup claimed. |
| Browser navigation | Polling replaced tab and session controls. Reconcile controls by identity, keep the current tab in the tab order, support arrows/Home/End. Show status of unfocused windows from one scoped summary request, not one poll per pane. Previously visited session badges are cached, not live fleet monitoring. | `shell-navigation.test.mjs`, `workspace-review.spec.mjs` with actual OSC 7501 approval and a polling boundary. |
| Operations | Shelf restores captured an outdated revision; follow-up shell creation could consult the new selection instead of its intended window. Capture launch IDs and take the shelf revision when the user invokes Restore. | Real-shell navigation, frame and placement flows. |
| Browser sessions | A 401 on polling left the old workspace displayed. Dispose cached terminals, dismiss dialogs, clear sensitive fields and return to connection entry. Transient network failures do not sign out a user. | Browser review revokes a live observer while a dialog is open. |
| Interaction | Compact host identity, window tabs and pane controls; distinguish local zoom from shared zoom in command matching; support keyboard navigation and a no-results state. Never show destructive pane controls to an observer. Native desktop connection/ID fields are collapsible; mobile status has its own line and buttons have a 44-DIP minimum height. | Browser screenshots, bounds and interaction checks; existing desktop/phone headless suites. |

## Product references

Rex's [program-status documentation](https://www.superlogical.com/rex/docs/build/program-status) describes background tab indicators. Its [keyboard documentation](https://www.superlogical.com/rex/docs/customize/keyboard-shortcuts) describes configured bindings taking precedence over defaults. The [published interface inventory](rex-product-research.md#24-visual-evidence) documents host identity, the tab strip and controls attached to the focused pane. Colors, typography, permission indicators and browser interaction here remain RoyalTerminal's own design.

## Reproduce

Run the routine `Multiplexing experiment` check. An optional `[workspace-ui]` in the **tip commit subject** exercises a real browser and a local broker on the same Linux runner. It uses the image's Chrome and the pinned streaming frontend, without installing a browser or starting the full OS/Wasm matrix. Screenshots and layout measurements are under `workspace-review/` in the test artifact. Locally: `npm ci && npm run build:stream-only`, build the server in Release, then `npx playwright test workspace-review.spec.mjs` from `samples/RoyalTerminal.Multiplexing.Server`; set `WORKSPACE_REVIEW_EVIDENCE` to retain screenshots.

The review inventoried the PR and traversed high-risk code paths in the broker, transport, API, auth, recovery/archive, automation, deployment and clients. It is not an independent line-by-line security certification. No physical iOS/Android handset, live tailnet or push provider, or production signing identity is exercised by these checks.
