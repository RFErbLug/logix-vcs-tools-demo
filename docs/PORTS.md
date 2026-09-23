# Ports and local hosts

v0.1 intentionally uses three local-only HTTP hosts.

| Component | Address | Purpose |
|---|---|---|
| Commit | `http://127.0.0.1:5187` | Capture saved ACD versions and publish to a configured Git remote |
| Compare | `http://127.0.0.1:5191` | Select a Git version and open native Logix Designer Compare |
| Restore | `http://127.0.0.1:5192` | Restore a selected version into the working ACD |

## Why separate ports?

These workflows were proofed independently. Keeping them separate in v0.1 minimizes coupling and makes failures easier to diagnose.

## Security boundary

The hosts bind to `127.0.0.1` by default.

Do not change them to `0.0.0.0` or otherwise expose them to a LAN/WAN without adding authentication, authorization, and a deliberate network security design.

## Planned later work

A future branch may consolidate the VCS functions into one host:

```text
/commit
/compare
/restore
```

That is not part of this release.
