# Aide-de-Camp

A Windows save editor for **Grand Tactician: The Civil War (1861–1865)**. Edit armies, garrisons, ships, officers, weapons, and national resources through separate Union and Confederate views.

## Download and run

Download the `Aide-de-Camp-0.8.16-win-x64.zip` asset from [Unit Creation Test Build](https://github.com/Rook-US/Aide-de-Camp/releases/tag/v0.8.16). Extract the ZIP and open **Aide-de-Camp.exe**. The release root contains only the executable and this README as files; guides and license notices are in `docs` and `licenses`. The .NET runtime is built into the executable; no separate .NET installation is required. GitHub's automatically generated “Source code” archives are for developers, not the runnable app.

Requires Windows 10/11 x64 and your own game installation. Version **0.8.16 — Unit Creation Test Build** is an alpha prerelease. Creation supports save version **1.142**. Combined in-game tests confirmed specific Union formations, perks and supplies; this does not establish every possible combination. Start with a copied campaign and keep your original saves. See [creation test results](docs/CREATE_TOOL_GAME_TEST.md) and [release notes](docs/RELEASE_NOTES.md).

## Features

- Create land HQs and infantry, cavalry or artillery, including combat units under existing fort commands; review choices, undo the complete creation and save with a backup.
- Searchable verified town placement for independent HQs, filtered equipment, training, experience, separate HQ/combat perks and configurable starting stocks.
- Army and garrison rosters, formation cards, naming rules, and retained batch selections.
- Ships in deployed fleets or port, including construction, repair, and condition editing.
- Officers with experience, fame, attributes, traits, branch, and promotion dates.
- Weapon stockpiles editable per nation, but standardization and order editing need work.
- Nation views for eligible recruitment states, treasury, projects with stage boxes, subsidy funding, and policy progress.
- Full-field selection editing, with separate infantry/cavalry/artillery groups, filtered weapons, strength/gun previews, and one undoable commit.
- Cell copy/paste across roster selections, plus persistent campaign/save/date/player information.
- Compact Tree cards with readable command/strength/alerts, responsive edge gaps, and preserved Nudge offsets.
- Adjustable UI settings and application error logs.

See the [user guide](docs/USER_GUIDE.md) and [known limits](docs/KNOWN_LIMITS.md) before editing campaign progression.

## For developers

- [Build, verify, and package](docs/BUILDING.md)
- [Architecture and save mapping](docs/ARCHITECTURE.md)
- [Development history](docs/DEVELOPMENT_HISTORY.md)
- [Contributing](CONTRIBUTING.md)

The two original GTCW.OOBEditor starter commits remain in this repository's ancestry. Later local development is summarized honestly in the history document, rather than presented as reconstructed commits.

Source code is available under the [MIT license](LICENSE). This is an unofficial independent project. Game assets, campaign saves, and game binaries are not included.
