# Aide-de-Camp

A Windows save editor for **Grand Tactician: The Civil War (1861–1865)**. Edit armies, garrisons, ships, officers, weapons, and national resources through separate Union and Confederate views.

## Download and run

Download the `Aide-de-Camp-0.8.10-win-x64.zip` asset from [Releases](https://github.com/Rook-US/Aide-de-Camp/releases). Extract the entire ZIP, then open **Aide-de-Camp.exe** inside the extracted folder. Keep its companion files together. The .NET runtime is included; no separate .NET installation is required. GitHub's automatically generated “Source code” archives are for developers, not the runnable app.

Requires Windows 10/11 x64 and your own game installation. Version **0.8.10 is a preview**: automated save and UI checks pass, but campaign acceptance testing inside the game remains outstanding. Start with a copied campaign and keep your original saves.

## Features

- Army and garrison rosters, formation cards, naming rules, and retained batch selections.
- Ships in deployed fleets or port, including construction, repair, and condition editing.
- Officers with experience, fame, attributes, traits, branch, and promotion dates.
- Weapon stockpiles, standardization, and existing order editing.
- Nation views for eligible recruitment states, treasury, projects with stage boxes, subsidy funding, and policy progress.
- Checked-field batch edits for units, ships, officers, and weapons; review, backups, and undo support.
- Adjustable UI settings and application error logs.

See the [user guide](docs/USER_GUIDE.md) and [known limits](docs/KNOWN_LIMITS.md) before editing campaign progression.

## For developers

- [Build, verify, and package](docs/BUILDING.md)
- [Architecture and save mapping](docs/ARCHITECTURE.md)
- [Development history](docs/DEVELOPMENT_HISTORY.md)
- [Contributing](CONTRIBUTING.md)

The two original GTCW.OOBEditor starter commits remain in this repository's ancestry. Later local development is summarized honestly in the history document, rather than presented as reconstructed commits.

Source code is available under the [MIT license](LICENSE). This is an unofficial independent project. Game assets, campaign saves, and game binaries are not included.
