# Local Create Unit implementation and evidence

Updated 9 October 2026. Included in 0.8.16 — Unit Creation Test Build, published with user authorization.

## Scope and confirmation policy

Create Unit is a movable, resizable WPF window shared by the toolbar, tree cards,
tree empty space and roster context menu. Combat units attach to an explicit HQ;
independent HQs select a verified town. Native tiers are kept separate from the
regimental presentation scale. Union and Confederate creation use the same mapped
1.142 layout. Navy, new commanders, portraits and new forts remain outside scope.

The user's revised policy permits **mapped but untested combinations after explicit
confirmation**. This includes Confederate combinations, additional HQ tiers, training,
drafts, contracts, other compatible equipment, other perks/levels and non-recruitable
but mapped home-state IDs. Confirmation does not invent a layout, commander, parent,
town position or field meaning. Unknown save versions, corrupt records, missing
references, duplicate assignments, impossible hierarchy and incompatible weapons are
rejected before mutation. Unknown-layout messages say **Unable to proceed**.

This is a working local editor implementation, not a claim that all combinations have
been accepted by the game. Test 15 now provides a game-written resave and Union visual
confirmation for the production backend. See `CREATE_TOOL_GAME_TEST.md` for retained
values and the opposing faction's commander/perk changes.

## User flow

1. Open Create Unit from the active faction's tree/roster or a card's context menu.
2. Choose HQ, infantry, cavalry or artillery and its displayed/native tier.
3. Choose a parent HQ, or a town for an independent HQ. Existing fort commands are
   resolved through `garrisonrefs.dat`, never their name's wording.
4. Set name, existing unused commander, appearance, and relevant combat inputs.
5. Open the experience/perk section as needed. Any section is accessible immediately.
6. Review the exact choices and confirmation notes in a scrollable review window.
7. Creation becomes one undo step in the working OOB. Save changes uses the existing
   full-save backup and transactional replacement path. Undo also works after saving.

Advanced mode expands the sections and permits browsing all weapons and mapped
combat perks. An incompatible weapon must be corrected. An off-branch perk has an
explicit confirmation note. Headquarters use a separate perk catalog and four slots;
combat units have one. A name suggestion only changes the name.

## Evidence and version boundary

* Save version: **1.142**. Installed `Assembly-CSharp.dll` SHA-256:
  `AE15E3E479F4D79B70E40CED42EB93A953569B8185E075B3873C7410462AE8C4`.
* Tests 1–2: user-confirmed Fort Monroe artillery, brigade infantry, division cavalry,
  subordinate artillery HQ and three artillery battalions. Game resave:
  `G/Save7_10_2026_22_35_17`.
* Tests 10/12: independently deployed native-tier-16 Union HQs at New York and
  Philadelphia. Game resaves preserved deployment, commander and readiness.
* Tests 11/14: complete new combat paths retained requested stock. Test 14's paused
  `G/Save8_10_2026_21_28_59` retained eight units, parent references, Zouave I,
  Cold Steel I and infantry experience 50. The user saw two stars / Battle Experienced.
* `CreationChecks` compares the new 250-field path with the actual game-written Test
  14 experience unit. All defaults agree except intentionally supplied identity,
  stock/history, initial morale 1 and campaign-stat update time 0. This is independent
  game-output evidence, not merely a roundtrip through the same writer.
* Local IL extracts are in `artifacts/creation-mapping/`. Reproduce them with
  `scripts/Inspect-GameMethodIL.ps1`; it now accepts `.ctor` and `.cctor`.

### Additional mappings established in this pass

* `GameVars..cctor` IL 5753–5775: recruiting type **0 Volunteers, 1 Drafts**.
  `CampaignMainPanel.UpdateUnitInfoPanel` indexes this table with the saved field.
  `BattleUnits.RecruitUnit` checks type 1 before applying draft support effects.
  ADC inserts a recruited unit; it does **not** execute recruitment economy costs.
* `GameVars..cctor` IL 4819–4873: experience labels are Green, Inexperienced,
  Battle Experienced, Veterans, Elite, Crack. The UI indexes them by
  `floor(Experience() * 5)`. The stored percentage is loaded divided by 100.
* `PerkRow..cctor`: three levels per perk. Static offered combat IDs are infantry
  **0–7**, cavalry **8–13**, artillery **14–18**. `PerkRow.GetPerksShown` and
  `AIBattle.CheckPerkSelection` use these branch arrays. HQ catalog IDs 0–17 are
  a separate namespace; this does not establish every HQ perk's tier eligibility.
* `LoadAdditionalUnitData` IL 3539–3560: the formerly unknown Boolean after
  `missingavg` is read with `Boolean.Parse` and immediately **discarded (`pop`)**.
  It is a reserved/obsolete serialized Boolean in this loader, not hidden supply state.
* `Regiment.Initialize` allocates four stock values, each equal to strength, without
  a branch condition. Existing pre-ADC A-campaign saves also have nonzero inactive
  ammunition slots. Those values were not introduced solely by ADC's experiments.
* `Regiment.Initialize` creates 150 false active-order flags, zero order state,
  null advised source, order delay -1, timed movement -1 and roads enabled.
  `Regiment..ctor` initializes supply speed 1, reinforcement priority 2, combat-zone
  count -1 and assigned army-group commander -1. Other no-order values are matched
  against the game-written Test 14 record, not copied from an arbitrary neighbor.

## Exact write structures

Offsets below are **relative to one parsed record**, not persistent identities.
Cross-file identity is always resolved by saved IDs or the game's composite key.

### `regiments.dat`: count + 39 lines per record

New records append after the complete counted record section. Allocation is the next
ID in a contiguous set 0..count-1; existing IDs are not renumbered. Parent is an
existing/currently staged numeric Group_ID in the same faction. Reordering preserves
the IDs. The game may renumber all IDs on its next resave; reload resolves them anew.

The game writer is `ImportExportUnitData.CreateUnitFiles`; loader construction is
`BattleUnits.create_regiment`. ADC constructs `CreationRecords.Regiment`, parses it
through `ParseRegiments`, and patches later edits through `PatchRegiment`.

| Offset | Saved meaning | Initial source/value |
|---:|---|---|
| 0 | Unit_ID | Next contiguous current-save combat ID |
| 1 | Object/unit name | Explicit name |
| 2 | Abbreviated/override name | Same explicit name; path key agrees |
| 3 | Parent Group_ID | Explicit validated HQ |
| 4 | Combat branch | 0 infantry / 1 cavalry / 2 artillery |
| 5 | Commander_ID | Existing same-faction, unused officer |
| 6 | Strength plus sick men | Requested size; initially no sick |
| 7 | Wounded losses | 0 |
| 8 | Captured losses | 0 |
| 9 | Sick percentage | 0 |
| 10 | Combat experience ×100 | Requested 0–100 |
| 11 | Training ×100 | Requested 0–100 |
| 12 | First battle flag | True, as `CreateUnitDefault` initializes |
| 13 | Weapon_ID | Configured matching branch |
| 14 | Coat RGB text (`componentcolor[1]`) | Explicit existing faction color style |
| 15 | Trousers RGB (`componentcolor[0]`) | Same style |
| 16 | Color variation RGB (`componentcolor[3]`) | Same style; not spatial coordinates |
| 17 | Combat perk ID | -1 empty or chosen combat ID |
| 18 | Perk level | 0/1/2 = I/II/III |
| 19 | Perk experience | User percentage /100; separate from offset 10 |
| 20 | Battle flag ID | -1, no unique flag selected |
| 21 | Unique icon field | -1, the inspected writer's value |
| 22 | Unique march-music field | 0, the inspected writer's value |
| 23 | Horse artillery | Explicit Boolean; artillery only |
| 24 | Native override tier | Explicit combat tier 10–13; display scale is not saved |
| 25 | Campaign brigade rank | 0, new formation |
| 26 | Enlistment date | Current parsed campaign date, dd.MM.yyyy |
| 27 | Contract months | User value; UI default 12 |
| 28 | Number of battles | 0 |
| 29 | Home State_ID | Selected saved state record |
| 30 | Recruiting type | 0 volunteers / 1 drafts |
| 31 | Service history | Presentation text “Raised on …”; never a data source |
| 32 | Uniform set | 0, tested initial set; colors above are explicit |
| 33 | Transfer time | 0, fully recruited/attached |
| 34–36 | Current transfer world x/y/z | 0 for field attachments; exact linked fort coordinates for direct fort units |
| 37 | Last transfer update | 0, no pending transfer |
| 38 | Morale | 1; game recalculates campaign morale |

The lower-tier override range, arbitrary contract lengths, uniforms other than the
tested examples, training, drafts and other equipment are mapped but require game
confirmation. Maximum size comes from the resolved campaign `unitprefs.txt`, not
fallback constants. Artillery gun estimates use ADC's existing configuration rule.

### `groups.dat`: count + 32 lines per record

Append/ID allocation is as above in its own ID namespace. Native tiers 14–16 are
land HQs; subordinate HQ tier must be less than the parent's. New fort HQs are not
created. `CreateUnitFiles`/`create_regiment` trace the following fields; ADC writes
`CreationRecords.Group` and later `PatchGroup` edits.

| Offset | Meaning | Initial value/source |
|---:|---|---|
| 0 | Group_ID | Next contiguous group ID |
| 1 | Command name | User name |
| 2 | Parent Group_ID | Explicit lower-tier attachment, or -1 independent |
| 3 | Faction | 0 Union / 1 Confederacy |
| 4 | Commander_ID | Existing, unused, same faction |
| 5 | Fighting spirit ×100 | 100 from accepted HQ experiment; game resave normalized to 1 |
| 6–9 | HQ supply-state ratios ×100: small arms, artillery, provisions, forage | 100 initially; game recalculates (Test 15: 50 for both Union HQs) |
| 10–12 | Men, horses and guns condition ×100 | 100 from accepted HQ experiment |
| 13 | Unique battle flag | -1 |
| 14 | Icon value | 0 |
| 15–16 | Coat/trouser RGB | Explicit faction appearance |
| 17 | Native tier | 14, 15 or 16 |
| 18 | Intelligence ×100 | 0; recalculated by game |
| 19 | Campaign command rank | 0 |
| 20,23,26,29 | HQ perk ID for slots 0–3 | -1 or chosen **HQ** catalog ID |
| 21,24,27,30 | Each perk's level | 0–2 |
| 22,25,28,31 | Each perk's experience | User percentage /100 |

These HQ aggregate fields are not a starting combat-stock input. Training, combat
experience, weapon, combat size, contract and home state have no corresponding HQ
fields and are disabled with an explanation. HQ perk assignment is offered with
confirmation; no completed in-game HQ perk test is claimed.

### `paths.dat`: counted variable records; new combat records have 250 lines

Identity is exact `(name, abbreviation, runtime branch, commander)`. New combat paths
append after the fully parsed last record and increment the count. The strict parser
validates scalar types and all count-delimited nested orders/couriers before save.

The reference reader/writer are `LoadAdditionalUnitData` and `SaveAdditionalUnitData`.
ADC writes `CreationRecords.Path` and reads `PathRecordParser`. Existing dynamic paths
remain intact. These relative offsets apply only to the new **zero-path, zero-order**
shape; they are never used to locate an arbitrary saved record.

| Offset(s) | Meaning and initial value |
|---:|---|
| 0–3 | Name, abbreviation, runtime combat branch, commander: exact regiment key |
| 4 | Cover/road history count 0 |
| 5 | Rotation 0; placement follows parent |
| 6 | Movement segment count 0 |
| 7–9 | Timed movement -1; order delay -1; arrival 0 |
| 10–13 | Battle-start, battle-calling, participating, in-battle: False |
| 14 | Transfer time 0 |
| 15–17 | Transfer position 0/0/0; no active path transfer |
| 18–20 | Patrol origin 0/0/0; no patrol |
| 21 | Basic-garrison-unit flag False; not equivalent to a fort attachment |
| 22 | Blockade efficiency 0 |
| 23–27 | Stance, ordered stance, cavalry orders, ordered cavalry orders, fleet orders: 0 |
| 28–31 | Not-moving time 0; entrenchment 0; last campaign-stat update 0; morale 1 |
| 32 | Active-order Boolean count 150 |
| 33–182 | Every one of the 150 active-order flags False |
| 183–186 | No advised source: empty name, empty abbreviation, type -1, commander -1 |
| 187 | Order state 0 |
| 188 | Order queue count 0 |
| 189–192 | Cached upkeep 0, cached recruitment cost 0, retreat False, withdrawal False |
| 193 | Tactical ammunition count 3 |
| 194–196 | Three ammunition fractions 1/1/1 |
| 197 | Supply-consumption count 4 |
| 198–209 | Four triples: consumption, depot supply, local supply; all 0 |
| 210–214 | Supply speed 1; latest reinforcements 0; needed reinforcements 0; supply-state cache 1; retreat angle 0 |
| 215–218 | Small arms, artillery ammo, provisions, forage stock; each requested percentage × strength /100 |
| 219–225 | Recruitment finished True; battle flag -1; reset stance False; missing average 0; discarded/reserved Boolean False; in-battle-until 0; prior wounded 0 |
| 226–230 | Sea, sea-only, rivers, rails False; roads True |
| 231–233 | Theater position 0/0/0 |
| 234–236 | Reinforcement priority 2; reform time 0; reinforcement-type selector -1 (game-written tested no-order value) |
| 237 | Service history, same presentation text as regiment |
| 238–239 | Prestige casualties 0; last combat-zone count -1 |
| 240–242 | No assigned army group: empty name, commander -1; assigned army-group commander -1 |
| 243–249 | Embarkation start 0, duration 0, nearby enemy fleet False, update 0, rout-recovered False, commander-campaign flag True, coordination remaining 0 |

The saved reinforcement selector -1 accepts both recruiting types:
`Regiment.UpdateIIPSupplyLink` IL 5851–5881 accepts a matching recruiting type,
selector 2, or selector -1. `UpdateReinforcementButtons` also includes -1 for both
pools. This game-written default is not a separate user-facing reinforcement option.
All four stock slots exist regardless of branch;
inactive ammunition slots are not physical rounds or additional usable ammunition.

New HQ paths are deliberately left to the mapped game-load regeneration route used
in Tests 2, 10 and 12, instead of guessing the HQ's runtime type/abbreviation. The
existing paths file remains fully parseable, with its count equal to actual records.
Therefore path count may temporarily be smaller than group+unit count by the number
of staged new HQs. The review explains this; the next game resave must regenerate them.

### `armygrouprefs.dat`

For G groups and U combat units, layout is `G`, G army-group commander references,
`G`, G `(Group_ID, dlcw_isundercommandercampaign)` pairs, `U`, U `(Unit_ID, flag)`
pairs. Total `3 + 3G + 2U` lines. Every section count and ID is checked. Additions
append -1 to the first section for a new HQ and `(new ID, True)` to the relevant
pair list. `True` is the commander-campaign flag, not a faction. These are the
game-generated values observed for accepted new formations. Existing values persist.

### `battledata.dat`

43-line header; deployment count at header offset 42. Counted 15-line deployment
records; then an economy-cycle count and two lines per cycle. New independent HQ
deployment inserts immediately before that tail, preserving it exactly. Attached
commands/combat units get no independent deployment. Record fields:

`Group_ID, formation=1, town.worldX, -town.worldZ, facing=0, entryAngle=0,
reinforcementDelay=0, AIgroup=-1, AIstance=0, entrenchment=0,
objectiveZ=0, orderActivation=0, trigger=0, isGroup=True, readiness=100`.

These are the tested New York/Philadelphia deployment initializers. Town world Y
is part of its verified identity, not written into the 2D deployment fields.

### Read-only reference files

`garrisonrefs.dat`: count + eight-field fort records; exact name/commander linkage
and root native tier 14 resolve Group_ID. Fort identity includes saved building name
and world x/y/z. No name keyword classifier participates in creation. Neither fort
records nor town records are written.

`IIPsTowns.dat`: full dynamic parsing, then exact town name+world-position joins to
the dated texture-derived State_ID map. The 103 mapped entries include foreign
locations: Create filters them using State_ID and owner, not spelling. Only domestic
state IDs with Union/Confederate saved ownership are offered. Mapping applies from
2 March 1861 through 19 June 1863. Other dated maps still need tracing.

`nations.dat`: existing bounded parser supplies State_ID and the saved recruitable
flag. Guided home-state options require that flag. Advanced exposes other mapped
domestic states with confirmation. No population/stock/recruit deductions are made.

`commanders.txt`: existing 66-field records supply ID, name, faction, branch, rank,
status and active flag. Cross-OOB assignment uniqueness is enforced. Rank, status and
branch limitations appear in review; their complete game eligibility rules remain
unverified. No commander/attributes record is created or altered.

## Transaction and future Army Builder

`UnitBlueprint` contains only reusable design values, not offsets or live UI objects.
`CreationPlacement` is separate: a template must resolve its parent/town against the
current save at application time. `CreationRequest` combines them for assessment.
The window only selects values; the service owns validation, record construction and
one-step undo/redo. File snapshots preserve structural changes across Save so undoing
a saved creation removes its companion records too. Failed memory mutations restore
all involved buffers. Disk transactions keep the existing full backup/hash/replacement
and rollback mechanism.

Future **Army Builder** should compose multiple blueprints with local template-node
keys, resolve keys to newly allocated current-save IDs, assess the complete graph,
and apply one multi-record transaction. Template persistence needs a schema version,
configuration compatibility metadata, import validation and explicit remapping of
commander/weapon/state references. Compact NATO cards, drag/drop template palette,
tree/roster views and Back to Quick Create can share this backend. Those future UI
features are not presented as implemented in this pass.

## Verification

* Service baseline: 178 checks passed.
* Creation writer: 36 checks passed, including comparison with actual game-written
  path defaults, strict malformed-Boolean rejection, invalid hierarchy/equipment,
  unknown version, unresolved town, copied-save reload, backup, byte-exact undo after
  save, and saved-rename/creation undo/redo ordering.
* Creation window: 18 checks passed; domestic town filter, faction/parent defaults,
  configured maximum, guided/advanced equipment and perk lists, separate HQ slots,
  search, modal confirmation, and creation through the production service.
* Initial catalog loading measured 482–535 ms; 25 in-memory branch switches 65–89 ms
  total on this machine. Read-only town loading avoids the per-line newline tracking
  required by writable text buffers; the cache is cleared on save reload.
* The broad WPF regression run was interrupted twice by Windows clipboard access
  failure (0x800401D0). It is not counted as a passed full suite. A separate existing
  mixed-ship test was corrected to actually select differing condition values.
* Test 15 game resave: all eight creations retained, complete paths regenerated,
  all five combat units retained their requested four stock values. Union choices
  retained; opposing commanders and its available perk choice were changed by the game.
  Confederate visual confirmation remains unavailable because of fog of war.
