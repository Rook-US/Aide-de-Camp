# Validation

For 0.8.14, 160 service checks and 667 Windows UI checks passed. Coverage includes mixed infantry/artillery field and weapon targeting, strength/gun previews, one-step undo, invalid-paste atomicity, clipboard edits in unit/officer/weapon/ship rosters, conditional ship-field subsets, and save metadata labels. The extracted single-executable package started using bundled .NET 8.0.30 with no shared runtime configured; its root-file and source-file exclusion checks passed. In-game campaign acceptance testing remains outstanding.

For 0.8.12, see [Tree cache lifecycle and transition measurements](TREE_CACHE_VALIDATION.md).

For the current 0.8.11 Tree update, see [Tree checks and rendered before/after evidence](TREE_VALIDATION.md). The 0.8.10 record below is retained as release history.

## 0.8.10 validation

Validated locally on Windows x64 on October 5, 2026:

| Check | Result |
| --- | --- |
| Release solution build | Passed; zero warnings and errors |
| Synthetic service fixtures, without game files | 148 checks passed |
| Synthetic WPF fixtures, without game files | 569 checks passed |
| Service suite with local campaign/config inputs | 284 checks passed |
| WPF suite with local campaign/config inputs | 632 checks passed |
| Extracted portable ZIP startup | Passed; title Aide-de-Camp 0.8.10, x64, bundled .NET 8.0.30 |

The optional campaign checks read original files and perform edits against temporary copies. Private inputs and machine-specific logs are not included in this repository. The portable-package test verifies its bundled runtime path rather than relying solely on the developer machine's installed .NET runtime.

These checks do not establish that all campaign effects have been accepted by the running game. In-game acceptance and a broader clean-Windows compatibility matrix remain outstanding. The release is marked as an alpha for that reason.
