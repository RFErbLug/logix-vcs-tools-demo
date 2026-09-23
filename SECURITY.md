# Security

## Local-only web hosts

The v0.1 hosts bind to `127.0.0.1` by default:

- Commit: 5187
- Compare: 5191
- Restore: 5192

They are designed for a local engineering workstation.

Do not bind these services to all interfaces or expose them to a LAN/WAN without adding an explicit authentication/authorization and network-security design.

## Restore is a file-changing operation

Restore Working Project can replace the base-named ACD after the engineer closes it in Studio 5000.

Safety controls include:

- reconstruct historical ACD before touching the working file;
- wait until the working ACD is unlocked;
- create a timestamped safety backup immediately before replacement;
- leave the final Studio 5000 Open action to the engineer.

Use a test project first when validating a new installation.

## CustomToolsMenu.xml

Installation modifies Studio 5000's `CustomToolsMenu.xml`.

The installer creates a timestamped backup before changing it. The uninstaller restores that backup.

## Native command output

Some successful native commands write progress to stderr. The toolkit does not treat stderr alone as proof of failure; exit code and operation result are authoritative.
