> Update 9 October 2026: This document preserves earlier investigation findings. Current implementation, resolved reserved Boolean, confirmation policy and field tables are in [CREATE_TOOL.md](CREATE_TOOL.md). The production-backend game resave is documented in [CREATE_TOOL_GAME_TEST.md](CREATE_TOOL_GAME_TEST.md). Earlier blocked-status statements below describe the evidence available at that time.

# Land creation format gate (7 October 2026)

**The game acceptance gate passed for the narrow 1.142 insertion experiment
described below.** Six combat units and one artillery command loaded in the
game, were visually confirmed, and were resaved with complete game-written
companion records. ADC does not yet expose a Create operation. Broader Army,
Garrison, location, and version combinations remain unsupported until their
specific rules are verified. The installed game's sampled `version.dat` reads
`1.142`; its Windows executable does not supply a useful product/file version.

## Narrowed request: add a combat unit to an existing fort command

The player has limited the first Garrison flow to a fort and command that already exist. This removes the need to create a new `groups.dat` or `garrisonrefs.dat` record. ADC now has a **read-only** `GetExistingGarrisons` resolver: it parses the exact eight-line fort records, requires a finite saved world position, and matches a linked fort to one distinct root `groups.dat` command by the game's saved reference values (object name and commander), with saved tier 14 and observed runtime type 14 or 15. It rejects truncated, malformed, ambiguous, and unlinked references. The match uses the game's explicit fort reference, never words such as “fort” or “garrison” inside a command name. It does not infer a home state or town from the fort name or coordinates.

The resolver passes synthetic rejection checks and a read-only sweep of the local `Campaigns/001/G` saves. The game's `LoadAdditionalUnitData` reads its own counted, variable-length `paths.dat` entries and resolves each by object name, abbreviation, runtime type, and commander. The controlled test demonstrated that, for this 1.142 save, a new fixed command or combat record can load **without a corresponding path or army-group reference entry in the input**. On a game resave the writer generated seven new path records and the complete `armygrouprefs.dat` lists. Thus ADC must keep the input companion files parseable and unchanged, and recognize a temporary count mismatch as valid only for this tested import route. It must not synthesize guessed path values. The companion layouts and cross-file joins are mapped in [ADDITIONAL_UNIT_DATA_MAP.md](ADDITIONAL_UNIT_DATA_MAP.md).

For the tested Fort Monroe artillery unit, input `regiments.dat` Parent_ID `27`
joined the existing command identified through `garrisonrefs.dat`; its verified
fort world position was written to the transfer-position fields. The game
accepted the unit at Fort Monroe with the selected weapon, strength, commander,
and home state, then created its path and reference entries on resave. Five
other tested combat units and an artillery command likewise loaded beneath
explicit saved parent IDs. This is evidence for those types and placements,
not for a general location picker or arbitrary command tiers. No ADC Create
control or production write operation has been added yet.

The user subsequently authorized an **isolated insertion test**. See
[`CREATION_EXPERIMENT.md`](CREATION_EXPERIMENT.md) for the two local test-only
save copies, explicit record values, independent ADC checks, the game-written
resave, and the player's visual confirmation. This is evidence for a restricted
creation implementation, not approval to edit a live save or support other
versions.

A separate [Test 10](NEW_ROOT_COMMAND_EXPERIMENT.md) inserted a **new native-tier-16 independent HQ** at verified New York. The player saw the HQ, its assigned commander, and full readiness. ADC reloaded both a paused and an unpaused game resave and verified the saved New York deployment, exact root/tier, complete generated path record, and regenerated army-group references. This validates the narrow tested New York insertion. The player's visual map-position confirmation and a second town or a general game placement rule are still missing, so it does not lift the general town-picker write gate. Even the paused resave normalized the new HQ's morale and group-supply percentages; the input values are not validated initial defaults.

[Test 11](NEW_UNIT_STARTING_STOCK_EXPERIMENT.md) inserted a new infantry record with a complete, four-field-linked path record and half starting stock in its relevant categories. The game loaded and wrote a paused resave; ADC verified the same parent, branch, weapon, strength, home state, and four stock amounts, plus a complete generated reference list. The game renumbered the unit from ID 526 to 150, leaving its four-field path identity intact. This validates the narrow tested combination; general new-unit supply editing still requires production construction, prewrite validation, and game evidence for other combinations.

[Test 12](SECOND_TOWN_ROOT_EXPERIMENT.md) placed the same independent-HQ record at verified Philadelphia. The game's paused resave retained the root, its generated path and references, and the Philadelphia deployment. These two distinct town outcomes support the saved deployment rule; a production picker still needs current-save identity, ownership, playable-state, and position checks for every offered town.

[Test 14](EXPANDED_COMBINED_CREATION_EXPERIMENT.md) combined eight distinct 1.142 creation cases into one game load and paused resave. The game retained their explicit parents, stock arrays, Zouave I, Cold Steel I, and saved unit experience 50. The player saw both skills and confirmed that the experience unit displays two stars and “Battle Experienced” without an available perk choice. The game loads saved unit experience as `50 / 100 = 0.5` but perk selection uses the separate empty-slot `perkexp` threshold of `1`. These results broaden the **tested inputs**, not the set of production Create controls.

## Evidence and structure

The save sample inspected was `Campaigns/001/G-backup-pre-brigade-audit-2026-09-28/Save28_9_2026_11_40_9` under the local game installation. Its `groups.dat` begins with count 203 and then 32 lines per command; `regiments.dat` begins with count 380 and then 39 lines per unit. ADC's `GrandTacticianDataService.ParseGroups` and `ParseRegiments` use offsets `1 + index * 32` and `1 + index * 39`, respectively. The first sampled command begins `0 / French Expeditionary Corps / -1 / 3`; the first combat unit begins `0 / 1st Brigade / [blank] / 1 / 0`. This confirms parser alignment for these existing records, not new-record validity. Both files can contain trailing lines after the counted blocks; the writer preserves them when reordering.

The installed `Modding/ModdingTool_1.11.xlsm` supplies column labels for its **Groups**, **Unit list**, **Cities**, **Unit types**, and **Group override** sheets. Its example rows match the installed `Battlefields/005/A/groups.dat` and `regiments.dat`: the first Gettysburg command is Army of Northern Virginia and the first unit is Kershaw's Brigade, with matching IDs, names, parent IDs, morale, strength, and other exported values. This provides independent evidence for field names in **scenario templates**, but the tool's quick guide is version 1.08 and does not document running-save creation. The workbook stores some unit columns in a different order from the exported record; the table below follows the exported file. The game's scenario template is not a controlled before/after save of an in-game creation.

The tables use zero-based offsets inside each record. **Parsed** means ADC gives the field the listed interpretation. Workbook labels were checked against exported scenario records. The installed game's `ImportExportUnitData.CreateUnitFiles` method provides direct running-save writer evidence for unit field order and values. The third column records the **general creation gate**, which remains cautious beyond the exact values tested in [`CREATION_EXPERIMENT.md`](CREATION_EXPERIMENT.md). A field label or writer expression alone does not establish a valid new-record value for other choices.

### `groups.dat` — 32 lines per command

| Offset | Current evidence / ADC interpretation | New-record status |
| ---: | --- | --- |
| 0 | Parsed `Group_ID`; unique within `groups.dat` is enforced on load. | Allocation rule and cross-file references unknown. |
| 1 | Parsed name; existing-name edits are written here. | New-name validity beyond line safety unknown. |
| 2 | Parsed `Parent_ID`; `-1` appears for a root. Existing reparent edits write here. | Valid new hierarchy and parent rule unverified in game. |
| 3 | Parsed `Nation`. | Valid faction values for every creation context unverified. |
| 4 | Parsed `Commander_ID`, displayed via `commanders.txt`. | New assignment behavior and unassigned sentinel unverified. |
| 5 | Workbook: morale; game writer serializes `Regiment.fightingspirit × 100`. | Initial value and exact game effect unverified. |
| 6 | Workbook: infantry ammunition percentage; game writer serializes `groupsupplystate[0] × 100`. | Initial value unverified. |
| 7 | Workbook: artillery ammunition percentage; game writer serializes `groupsupplystate[1] × 100`. | Initial value unverified. |
| 8 | Workbook: provisions; game writer serializes `groupsupplystate[2] × 100`. | Initial supply rule unverified. |
| 9 | Workbook: forage / coal; game writer serializes `groupsupplystate[3] × 100`. | Initial supply rule unverified. |
| 10 | Workbook: condition of men; game writer serializes `conditionmen × 100`. | Initial value unverified. |
| 11 | Workbook: condition of horses; game writer serializes `conditionhorses × 100`. | Initial value unverified. |
| 12 | Workbook: condition of guns; game writer serializes `conditionguns × 100`. | Initial value unverified. |
| 13 | Workbook: unique flag ID; game writer serializes `battleflag`. | New flag reference rule unverified. |
| 14 | Workbook: unique icon ID; game writer writes `0` in inspected method. | Effect and version-specific validity unverified. |
| 15 | Workbook: coat color; game writer serializes `componentcolor[1]` as numeric color text. | Color mapping and initial value unverified; **not a position**. |
| 16 | Workbook: trousers color; game writer serializes `componentcolor[0]` as numeric color text. | Color mapping and initial value unverified; **not a position**. |
| 17 | Parsed native `Unit_Tier`; workbook calls it `Group Override (opt)`. | Valid new Army/Garrison tier combinations unverified. |
| 18 | Workbook: intelligence; game writer serializes `intel × 100`. | Initial value and effect unverified. |
| 19 | Workbook: campaign brigade rank; game writer serializes `rankofbrigadecampaign`. | Initial value and effect unverified. |
| 20 | Workbook: group perk 0 type. | Valid perk mapping unverified. |
| 21 | Workbook: group perk 0 level. | Valid range unverified. |
| 22 | Workbook: group perk 0 experience. | Valid range unverified. |
| 23 | Workbook: group perk 1 type. | Valid perk mapping unverified. |
| 24 | Workbook: group perk 1 level. | Valid range unverified. |
| 25 | Workbook: group perk 1 experience. | Valid range unverified. |
| 26 | Workbook: group perk 2 type. | Valid perk mapping unverified. |
| 27 | Workbook: group perk 2 level. | Valid range unverified. |
| 28 | Workbook: group perk 2 experience. | Valid range unverified. |
| 29 | Workbook: group perk 3 type. | Valid perk mapping unverified. |
| 30 | Workbook: group perk 3 level. | Valid range unverified. |
| 31 | Workbook: group perk 3 experience. | Valid range unverified. |

### `regiments.dat` — 39 lines per combat unit

| Offset | Current evidence / ADC interpretation | New-record status |
| ---: | --- | --- |
| 0 | Parsed `Unit_ID`. | Allocation and uniqueness across all save references unverified. |
| 1 | Parsed `Unit_Name`; existing edits write here. | New name behavior unverified. |
| 2 | Existing-name edits also write `Override_Name`; existing OOB/detail display divergence documented in writer. | Valid initial override state unverified. |
| 3 | Parsed `Parent_ID`; existing reparent edits write here. | New parent/reference rule unverified. |
| 4 | Parsed `Unit_type`. | New-unit type enum and creation behavior unverified. |
| 5 | Parsed `Commander_ID`. | New assignment behavior unverified. |
| 6 | Parsed `Total_Men`; game writer serializes `max(0, strength + sick)`. Existing strength edits write here. | New-unit strength initialization unverified. |
| 7 | Workbook: wounded; game writer serializes `losses[0]`. | New-unit initial value unverified. |
| 8 | Workbook: captured; game writer serializes `losses[3]`. | New-unit initial value unverified. |
| 9 | Parsed `SickRatio`; game writer serializes `sick × 100 / max(1, strength + sick)`. Existing casualty/unavailable edits write here. | New-unit initial value unverified. |
| 10 | Parsed experience; game writer serializes `experience × 100`. Existing edits write here. Test 14's new infantry retained saved `50`; the game loads `0.5` and displayed two stars / “Battle Experienced.” | Tested for this 1.142 infantry combination; other branches, values, and versions remain unverified. |
| 11 | Workbook: training (drill); game writer serializes `training × 100`. | New-unit initial value and effect unverified. |
| 12 | Workbook: first battle flag; game writer serializes `firstbattle`. | New-unit initial value and effect unverified. |
| 13 | Parsed `Weapon_ID`; existing edits write here. | New-unit weapon assignment behavior unverified. |
| 14 | Workbook: coat color; game writer serializes `componentcolor[1]` as numeric color text. | Color mapping unverified; **not a position**. |
| 15 | Workbook: trousers color; game writer serializes `componentcolor[0]` as numeric color text. | Color mapping unverified; **not a position**. |
| 16 | Workbook: uniform color noise; game writer serializes `componentcolor[3]` as numeric color text. | Initial value unverified; **not a position**. |
| 17 | Workbook: perk 1 type. | Valid perk mapping unverified. |
| 18 | Workbook: perk 1 level. | Valid range unverified. |
| 19 | Workbook: perk 1 experience. | Valid range unverified. |
| 20 | Workbook: unique flag ID; game writer reads `Regiment.battleflag`. | New reference rule unverified. |
| 21 | Workbook: unique icon ID; game writer writes `-1` in the inspected method. | Effect and version-specific validity unverified. |
| 22 | Workbook: unique march music ID; game writer writes `0` in the inspected method. | Effect and version-specific validity unverified. |
| 23 | Game writer reads `Regiment.ishorseartillery`. | Valid type/flag combinations for new units unverified. |
| 24 | Parsed native `Unit_Tier`; workbook calls it `Unit Override (opt)`. | New-unit tier validity unverified. |
| 25 | Workbook: campaign brigade rank. | New-unit value and effect unverified. |
| 26 | Parsed enlistment date; existing edits write here. | New-unit initial date unverified. |
| 27 | Parsed contract months; existing edits write here. | New-unit initial contract unverified. |
| 28 | Workbook: number of battles. | New-unit initial value and effect unverified. |
| 29 | Parsed `State_ID` (home state); existing edits write here. | Playable/recruitable-state eligibility for creation unverified. |
| 30 | Workbook: recruiting type. | Valid enum and creation effect unverified. |
| 31 | Service-history prose parsed for display only. | New-unit content/requirement unknown. |
| 32 | Workbook: uniform set. | Initial value and effect unverified. |
| 33 | Parsed transfer time; game writer reads `Regiment.transfertime`. Existing edits write here and, only with a uniquely matched, fully parsed `paths.dat` record, at that record's dynamically located transfer field. | New path record/link requirement unknown. |
| 34 | Game writer reads `Regiment.currenttransferposition.x`. | Initial transfer-position rule unverified. |
| 35 | Game writer reads `Regiment.currenttransferposition.y`. | Initial transfer-position rule unverified. |
| 36 | Game writer reads `Regiment.currenttransferposition.z`. | Initial transfer-position rule unverified. |
| 37 | Game writer reads `Regiment.lasttransferupdate`. | Initial timestamp/rule unverified. |
| 38 | Game writer reads `Regiment.morale`. | Initial value and effect unverified. |

## Ordering, references, and other save files

ADC currently patches existing blocks at their saved offsets. For reordering, it sorts regiment blocks by parent ID and editor order, while command blocks move only among existing slots with the same parent ID. The game's `CreateUnitFiles` builds separate live command and combat-unit lists by traversing top-level objects and their Unity child hierarchy. It writes each list's count, then writes each record ID as that list's **zero-based index**. It computes a saved parent ID with `GrabNextParentUnit` and `IndexOf` in the command list, or `-1` for a root. This explains why IDs shift between the starting campaign and a running save. The controlled 1.142 input appended one 32-line command block and six 39-line combat blocks after the original counted records, set each new ID to the next zero-based ordinal, increased the two count lines, and supplied explicit numeric parent IDs. The game loaded all seven and rebuilt IDs and parent references on resave. This establishes that insertion point and allocation rule for the tested 1.142 import route. It does not establish other record orders, versions, or combinations. A production writer must reject noncontiguous or duplicate IDs, invalid parent references, and trailing material whose insertion boundary it cannot prove.

`paths.dat` is now parsed as complete count-delimited records for save version `1.142`, and ADC links an existing regiment only by its saved name, abbreviated name, type, and commander. It uses record offsets only after that match inside the current buffer. Transfer time moves when cover history or movement paths are present. There is still no proved ID-based link or new-record construction rule. The tested 1.142 import omitted new path entries; the game generated seven complete entries on resave. The game's `SaveAdditionalUnitData` writes them from Unity's live `Regiment` components, starting each record with those four identity fields, followed by dynamic-length path arrays, live position, transfer fields, and an `isgarrisonbasicunit` flag. That flag identifies a **basic garrison unit**, not all garrison commands. `commanders.txt` supplies existing commander display names; the seven explicitly chosen, previously unused Union commander IDs were accepted for this test. `version.dat` is monitored for changes; ADC disables path writes for any version except the observed `1.142`. The displayed canonical `State_ID` names do not prove that each is a playable home state, a town identity, or a map position. The workbook's **Cities** sheet has columns for city name, owner, and *approximate* x/z position, but no complete playable town-position mapping. The existing `CommandClassificationService` uses name words such as “garrison” and “fort” to divide roster pages; this cannot be used in a creation path. Presentation tiers in regimental mode do not change saved native tiers.

### Additional files found in the running-save folder

The local 1.142 save also contains `garrisonrefs.dat`, `armygrouprefs.dat`, and `IIPsTowns.dat`. Their presence makes a two-file creation transaction unsafe. The game's `ImportExportUnitData.SaveGarrisonReferences` method provides independent field-level evidence for `garrisonrefs.dat`: a fort-building count followed by **eight lines per fort**, ordered as building name, world x, y, z, linked garrison object name, linked garrison abbreviated name, linked unit type, and linked commander ID. An empty link is serialized as `none`, `none`, `-1`, `-1`. The game obtains fort buildings from `Battlemap/3DMap/Buildings` and their live world positions. `LoadGarrisonReferences` resolves the regiment using **object name, abbreviated name, unit type, and commander**, and resolves the building using **building name and position** (the latter with a 0.5 tolerance). It then sets `CBuilding.garrisonunit` and `Regiment.garrisonreference`. There is **no stable unit or building ID in this record**. This is a game-side name-dependent matching mechanism, not a validated saved-data classification rule suitable for ADC creation under the requested constraint. It also does not prove how a new fort would be registered or located.

**Fort Monroe trace.** In `Campaigns/001/G/Save5_10_2026_21_55_19`, `garrisonrefs.dat` has `Fort Monroe` at `(1709.999, 489.074, -860.0001)` linked to `Ft. Monroe Garrison`, runtime unit type `15`, commander `1113`. `groups.dat` gives that exact command name Group ID `27`, parent `-1`, faction `0`, commander `1113`, saved native tier `14`. `regiments.dat` units `53` (`49th MD`) and `54` (`50th MD`) both have Parent ID `27`. The saved Group ID is the **internal command-to-combat-unit key**; the saved garrison name is a **fort-to-command reference value**, not a numeric ID. The game's loader also requires abbreviated name and runtime type, which are not represented by the group's numeric ID. The full reference is a composite match, so renaming only `groups.dat` offset 1 could break it; an in-game rename effect has not been tested.

**Starting campaign cross-check.** The installed `Campaigns/001/G/regiments.dat` already contains two infantry records under Fort Monroe command ID `27`: `Peirce's Command` (commander `1185`, saved unit ID `54`, type `0`, home state `17`) and `Duryee's Zouaves` (commander `163`, saved unit ID `59`, type `0`, home state `17`). The earliest sampled running save `Save28_9_2026_15_38_13` still has both names, commander IDs, parent `27`, types, and state IDs, though their saved unit IDs became `53` and `54`. Later saves rename them `49th MD` and `50th MD`. The user confirmed infantry were successfully added to Fort Monroe in the campaign setup, but the installed starting records show **existing scenario-template units**, not a controlled running-save recruit operation. Their moved numeric IDs also show why ordinal or line-position identity is unsafe. They validate that the game accepts those particular starting-template subordinates under the fort command, not how to initialize or insert a newly recruited record.

ADC now rejects a rename of an existing command when `garrisonrefs.dat` contains a reference with its saved name and commander. This is a save-integrity guard, not a category inference or a creation rule. It also monitors the reference file for external changes during an editing session. A synthetic test confirms rejection occurs before either file is written.

Across ten local 1.142 save directories, all 650 active fort references matched exactly one `groups.dat` command by exact saved name plus commander, and none matched a `regiments.dat` combat-unit name. This is strong evidence of the *existing-record* relationship, not proof that the name alone is globally unique or sufficient for creation. `garrisonrefs.dat` runtime type is `15` for most of these references but `14` for 50 references in the earliest inspected save, while matched groups still have saved tier `14`. Runtime unit type and saved native tier must remain distinct. The player authorized using the game's explicit saved reference to add combat units beneath an already existing linked fort command. ADC still must not use name keywords to classify a garrison or infer any other saved value.

`IIPsTowns.dat` contains dynamic-length IIP and town records. The game's `SaveIIPAndTownData`/`LoadIIPAndTownData` methods serialize each IIP name, type, and live x/y/z, followed by economy arrays and other values; town records include city name, live x/y/z, ownership, and economy fields. ADC now has a bounded read-only parser for the complete 1.142 file, including its economy tail. It resolved 103 saved town names and exact game world positions in the player's resave and rejected malformed files. A town's saved owner is not a home-state ID. The installed game's dated 1861 state texture now provides a separately verified state ID for all 103 towns, matched by saved name and world position; [`ADDITIONAL_UNIT_DATA_MAP.md`](ADDITIONAL_UNIT_DATA_MAP.md) documents the game-code trace, asset hashes, and date limits. The controlled additions used explicit home-state IDs 17 or 31, while the game placed them beneath their saved parents; this does not prove new-unit placement at an arbitrary selected town.

The installed game's `CreateUnitFiles` writer and all ten sampled saves establish the `armygrouprefs.dat` count and section layout; the Boolean paired with each group and unit ID is `Regiment.dlcw_isundercommandercampaign`, **not** a side flag. The tested import omitted the new references; the game wrote complete updated lists on resave and set the seven new flags `True`. `battledata.dat` adds a placement link: all 650 sampled linked fort commands have deployment coordinates matching their fort reference (with map z sign reversed), while none of the 660 direct combat subordinates has an independent deployment record. The game resave likewise contains no individual deployment record for the new command or units; the player confirmed their requested positions in game. This establishes the tested parent-placement outcome, not an arbitrary-location rule. The complete sample evidence and precise section formulas are in [ADDITIONAL_UNIT_DATA_MAP.md](ADDITIONAL_UNIT_DATA_MAP.md).

Ten local 1.142 save directories were inspected read-only. Their group/unit/path counts change across saves; in each inspected directory, the path count equals group count plus unit count, and the first `armygrouprefs.dat` count equals group count. One pair has one more regiment and path and two more garrison references, but the apparent new regiment ID 494 is the **same Fort Charlotte unit previously saved at ID 493** after the list was reordered. The pair is not a controlled creation diff; actions between saves are unknown. A saved ordinal changing is not evidence that a new unit was created.

## Required evidence before expanding creation

The 1.142 test validates the exact seven records and parent placements documented in [CREATION_EXPERIMENT.md](CREATION_EXPERIMENT.md). It does **not** validate a general Army or Garrison creation form. Before offering another combination, establish its initial value for every fixed-record field, parent and placement rule, playable home state or town identity and world position, compatible weapon and strength limits, and whether additional save files must change. Check a disposable copied save with independent parsing, then confirm the visible unit and its properties in game. Unsupported versions must remain read-only for creation. Read-only state filtering is mapped for the dated 1861 texture. [Test 3](TOWN_PLACEMENT_EXPERIMENT.md) validates placing **one existing independent HQ** at New York by editing its unique deployment x/z; the game preserved that position on resave. It also proves that doing so leaves independently deployed subordinate divisions behind. Placement for a **newly created** command remains untested. No production Create write flow is offered yet.

[Test 4](PERK_ASSIGNMENT_EXPERIMENT.md) is a copied-save perk-choice experiment for a previously empty infantry perk slot. The game offered a choice, the player chose Zouave I, and the game resaved its perk triple as `2,0,0`; ADC reloaded that save. Direct insertion of a chosen perk remains untested, so this does not establish a production perk default or write rule.

[Test 5](DIRECT_PERK_EXPERIMENT.md) directly inserted the game-written Zouave I triple into the same infantry slot. The player confirmed success in game, the game retained `2,0,0` on resave, and ADC reloaded it. This validates that exact 1.142 infantry combination; it does not establish other perk or new-unit slot rules.

**Unable to proceed** with the requested general creation form at these exact gates:

| Requested choice | Unverified saved field or rule | Evidence needed |
| --- | --- | --- |
| Editable supply at creation | Tests 11 and 14 game-resaved complete new `paths.dat` records with retained starting stock for infantry, cavalry, artillery, and Fort Monroe cavalry in the exact tested combinations. [Test 9](DIVISION_SUPPLY_STOCK_EXPERIMENT.md) separately validates existing-unit stock edits. HQ `supplystate` recalculates and is not a direct stock field. | Production construction of complete new path records from validated fields, current-save prewrite checks, backup/undo integration, and further evidence for other branch/parent/version combinations. |
| Verified starting skills and experience | Test 14's new infantry retained Zouave I and saved experience 50, and its new cavalry retained Cold Steel I; the player saw both skills and the 50-experience unit's two-star “Battle Experienced” rating. `regiments.dat` perk progress is separate from general unit experience. | Production eligibility rules and validation for these exact branch/perk combinations; other skills, levels, slots, HQ perks, and versions require separate evidence. |
| Selected-town placement for a new command | Tests 10 and 12 inserted a new native-tier-16 Union root at verified New York and Philadelphia; both paused game resaves retained the correct `battledata.dat` deployment and generated companion records. | A production picker that revalidates each selected town's current saved identity, ownership, playable state, and position, plus the transactional Create writer; other map intervals remain gated. |
| State-filtered towns outside the 1861 texture interval | `Town.state` is absent from `IIPsTowns.dat`; this build uses different dated textures before 2 March 1861 and from 20 June 1863. | A corresponding asset mapping and save-position cross-check for each offered interval. The 1861 interval's read-only filter is implemented. |
| Confederate or other native tiers | The controlled import exercised Union branches and native command tier 14 only. | Separate controlled game acceptance tests for each offered faction/tier/parent combination. |

These findings may also inform later Navy/commander work. They do not validate Navy creation, commander creation, bulk import, or portraits.
