# Isolated unit insertion experiment — 7 October 2026

**Status: the requested 1.142 test set loaded in game and was resaved
successfully.** The ADC Create command remains disabled while the validated
record rules are integrated and tested. Keep the test copies separate from a
normal campaign save.

The source is a byte-for-byte copy of the installed `Campaigns/001/G/Save`
folder, stored under `artifacts/creation-experiment-2026-10-07/source-save`.
Its saved version is `1.142`. The reproducible builder is
[`Build-UnitCreationExperiment.py`](../scripts/Build-UnitCreationExperiment.py).
The two test copies in the game campaign folder are:

| Game folder | In-game save label | Purpose |
| --- | --- | --- |
| `Campaigns/001/G/ADC-TEST-1-FORT-MONROE` | `ADC Test 1 - Fort Monroe Artillery` | One artillery combat unit under the verified Fort Monroe command. |
| `Campaigns/001/G/ADC-TEST-2-ALL-UNITS` | `ADC Test 2 - All Requested Units` | All six requested combat units plus the new artillery command. |

The original `Save` folder was not changed. The game copies were checked against
the corresponding experiment copies by SHA-256 for the modified files and all
four companion files.

## Saved targets and explicit test choices

The following relationships are read from **numeric parent IDs in this save**,
not inferred from names. Existing command `27` is uniquely joined to the Fort
Monroe building by `garrisonrefs.dat`'s exact saved link and position. Command
`71` is a child of Union `1st Infantry Division` `87`, which is a child of I
Corps `70`. The other `1st Infantry Division` records have different parents and
factions. A pre-existing `Division Artillery 2` is command `284` under division
`281`; the new command uses the requested same displayed name with distinct
commander and parent IDs.

| New ID | New item | Parent ID | Saved type/tier | Commander ID | Weapon ID | Strength | Home state ID |
| ---: | --- | ---: | --- | ---: | ---: | ---: | ---: |
| 520 | ADC Test Fort Monroe Artillery | 27 | artillery / 11 | 38, Henry J. Hunt | 41, 24-pounder Howitzer | 60 | 17, Maryland |
| 521 | ADC Test 1-1 Infantry | 71 | infantry / 13 | 91, Abraham M. Hare | 14, Springfield M1861 | 1,000 | 17, Maryland |
| 522 | ADC Test Division Cavalry | 87 | cavalry / 13 | 292, Thomas C. Devin | 25, Sharps Carbine | 600 | 31, Pennsylvania |
| 287 | Division Artillery 2 | 87 | command / 14 | 46, James H. Carlisle | — | — | — |
| 523 | ADC Test 1st Artillery Battalion | 287 | artillery / 11 | 48, William H. Reynolds | 4, 3-inch Ordnance Rifle | 60 | 31, Pennsylvania |
| 524 | ADC Test 2nd Artillery Battalion | 287 | artillery / 11 | 49, Richard Arnold | 4, 3-inch Ordnance Rifle | 60 | 31, Pennsylvania |
| 525 | ADC Test 3rd Artillery Battalion | 287 | artillery / 11 | 50, Charles Brookwood | 4, 3-inch Ordnance Rifle | 60 | 31, Pennsylvania |

The chosen home states are explicit experiment inputs. They are **not** inferred
from a fort, town name, or parent. The selected commanders are Union officers in
`commanders.txt` and are unused by any original `groups.dat` or `regiments.dat`
record. The four weapon IDs and branch compatibility come from the installed
`Config/weapons.txt`. Strengths are below the campaign's configured maxima;
the exact requested initial strengths were not specified by the player.

## What the experiment writes

Stage 1 changes only `regiments.dat` and the save label in `scenario.dat`.
Stage 2 changes those two files and `groups.dat`. New fixed records are appended
after the original complete counted blocks. The existing blocks stay byte
identical at the line level. New IDs are contiguous list ordinals. Original
counts `287` groups and `520` regiments become `287/521` in stage 1 and
`288/526` in stage 2. All new parent IDs refer to existing or newly appended
commands.

The builder fills each of the 39 combat-unit fields and 32 command fields from
the installed game's writer field mapping in
[`CREATION_FORMAT_GATE.md`](CREATION_FORMAT_GATE.md). New-unit values such as
zero casualties, experience, training, transfer time, and battle count,
`firstbattle=True`, `morale=1`, unset perks/flags, and contract 12 follow the
observed `CreateUnitDefault` call arguments or existing game-written starting
records. Union uniform colors come from existing saved records `460` (fort
artillery), `145` (infantry), `157` (cavalry), `513` (field artillery), and
group `284`, after confirming those fields encode coat, trousers, and variation
colors. Fort Monroe's saved world position comes from its exact fort
reference; this value is written to the new unit's **transfer-position** fields,
whose effect on first placement was checked in this experiment. The builder
records source record IDs for reproducibility. These values remain validated
only for the tested 1.142 combinations and placements.

In the *test input*, `paths.dat`, `armygrouprefs.dat`, `battledata.dat`, and `garrisonrefs.dat` remain
byte identical to the source. The installed game's `LoadAdditionalUnitData`
reads the existing path record count and resolves each entry by name,
abbreviation, type, and commander; it does not compare the count with the new
`groups.dat` and `regiments.dat` counts in the inspected method. That makes
missing new path entries a **testable import route**, whose success is recorded
below. It is not proof of support for other versions or hierarchies.
`BattleUnits.import_units` likewise iterates the saved counts in
`armygrouprefs.dat`, applies each ordinal only if it is within the loaded list,
and does not require those counts to equal the expanded lists in the inspected
1.142 method. The new records therefore retain runtime defaults for any
unlisted reference fields until the game writes them on resave. ADC reports
each new unit's path link as missing before that resave. The new command has no
individual battle deployment record in the input.

## Observed game resave

The player loaded the full test copy in game, confirmed all requested parents,
map positions, equipment, and gun counts visually, and made the new manual save
`Campaigns/001/G/Save7_10_2026_22_35_17` with label `ADC Test 2 - Success`.
Its version is still `1.142`. The resave has 288 command records, 526 combat
records, and **814 complete path records**: exactly one for each command and
combat unit. All six new units have unique four-value path identities matching
the game-written regiment fields; the new command has its own path identity
with runtime type `14` and commander `46`. The game wrote 288 group and 526
combat-unit entries into `armygrouprefs.dat`; the new records' observed
`dlcw_isundercommandercampaign` flag is `True`. The game did not create an
individual `battledata.dat` deployment entry for the new command or its
combat subordinates; the parent division and Fort Monroe retain their known
deployment entries. The player's visual check confirms the tested units were
placed correctly despite those absent individual entries.

The game rebuilt the live list order, moving the new command from input ID
`287` to `75`, the Union division from `87` to `88`, and the new combat units
from input IDs `520–525` to `55`, `149`, `163`, and `158–160` respectively.
Every new saved Parent_ID changed to the corresponding **new command ID**.
This demonstrates why ADC must remap by saved identity after game resave and
must never treat a text line or an old numeric ID as a persistent key.

Across all six new combat records, the game retained every input field except
the reallocated ID, changed parent ordinal where applicable, and its runtime
updates to training (offset 11) and morale (offset 38). For the new artillery
command it retained every input field except ID, parent ordinal, morale
(offset 5), artillery ammunition supply (offset 7), and intelligence (offset
18). This is a controlled observation of accepted initial values for these
specific types and placements, not a universal default rule for other game
versions or untested locations.

The game resave passes 18 additional independent ADC checks using actual
`groups.dat`, `regiments.dat`, `paths.dat`, `garrisonrefs.dat`,
`armygrouprefs.dat`, and the installed weapon definitions. User-visible map
placement and gun counts were confirmed separately in game. This satisfies the
game acceptance gate for the tested combinations. The remaining implementation
work is a restricted 1.142 creation transaction and UI with explicit input
validation, rejection tests, and post-save navigation; this experiment does
not make other command tiers, locations, versions, or naming schemes valid.

## Pre-game checks

The isolated copies pass ADC reload checks for fort linkage, saved hierarchy,
record widths and counts, weapon compatibility, configured maximum strength,
known home-state IDs, and uniqueness of new four-field identities. All
unlisted companion files are byte identical to the source. Stage 1 passes 11
experiment checks; stage 2 passes 26. These pre-game parser checks were
followed by the game load and resave described above.
