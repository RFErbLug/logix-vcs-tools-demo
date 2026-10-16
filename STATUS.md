# v0.1.0 Demo Proof Status

> **SHOWCASE / DEMO ONLY — NOT A PRODUCT**
>
> This repository is an unofficial engineering showcase demonstrating possible Studio 5000 Logix Designer version-control workflows. It is **not** a Rockwell Automation product, official sample, supported offering, validated reference architecture, customer standard, or production deployment framework.
>
> The original material in this repository is **not offered as open-source software**, and no open-source license is granted for that original material. Public and third-party components used or referenced by the demo remain governed by their own licenses. References to Rockwell Automation products describe technologies used in the demonstration and do not imply official product status, endorsement, or support.

This demo snapshot records the three-host workflow observed in the development environment. These observations are demonstration evidence only; they are not product qualification or a compatibility/support commitment.

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
