# v0.1.0 Proof Status

The package represents the currently proven three-host workflow.

| Capability | Status |
|---|---|
| First-run Commit setup | Proven |
| Capture real engineering version | Proven |
| Repeat capture from same page | Proven |
| No-change / empty commit handling | Proven |
| Native Logix Compare launch | Proven |
| Historical Git source → temporary L5X | Proven |
| Current saved ACD → temporary L5X | Proven |
| Restore historical version → temporary ACD | Proven |
| Timestamped ACD safety backup | Proven |
| Replace same base-named ACD | Proven |
| Prepare existing Studio Open Project dialog | Proven |
| Automatic final Open click | Intentionally not used |
| Branch create/switch | Not included |
| One-host/one-port VCS shell | Planned later |
| CI/CD/test UI | Separate project/surface |

## Development proof environment

The workflow was proven with:

- Studio 5000 Logix Designer
- Rockwell Automation l5xgit
- Git
- Logix Designer Compare Tool
- Windows PowerShell / .NET 8 hosts

The package intentionally removes workstation-specific development paths and installs under `C:\ProgramData\LogixVcsTools` by default.
