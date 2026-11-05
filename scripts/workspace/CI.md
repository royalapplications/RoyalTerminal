# Workspace CI budget

A matching pull request allocates **one additional Ubuntu runner**. It runs Node browser unit tests without installing a browser, seven .NET suites (including desktop and phone headless interaction tests), and a small real-PTY scenario through the CLI and both Lua hosts. Superseded PR runs are cancelled. An ordinary branch push does not duplicate the PR check. The repository's pre-existing CI, Docs and Release workflows are unchanged.

## Explicit acceptance

To test one specialized profile on this draft branch, push a lightweight tag at the exact reviewed commit:

```sh
git push origin HEAD:refs/tags/workspace-ci/mobile/review-01
```

Use a new suffix for each run. The tag triggers one experiment workflow and does not match the repository's release pattern. The profiles are `matrix` (three desktop operating systems plus real Chromium/PTY), `mobile` (Android emulator and iOS simulator), `archive`, `account-login`, `tailnet`, `lua`, `protocol`, `personal`, `desktop`, and `signing`. Once its workflow exists on the default branch, a profile can also be dispatched manually for an explicit ref. A skipped, cancelled or unrequested suite is not a pass. Local fake credentials and simulators are not production signing, a live provider, or physical-device acceptance.
