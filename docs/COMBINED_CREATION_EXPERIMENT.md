# Test 13: combined creation check (superseded by Test 14)

Prepared locally on 8 October 2026 for the installed 1.142 game/save. This is a disposable test copy, **not** an ADC Create operation. [`Build-CombinedCreationExperiment.py`](../scripts/Build-CombinedCreationExperiment.py) starts from the paused `Save8_10_2026_19_59_24`, refuses to overwrite its output, and creates `Campaigns/001/G/ADC-TEST-13-COMBINED-CREATION`, labeled **ADC Test 13 - Combined Creation**. Only counted `regiments.dat`, counted `paths.dat`, and the visible `scenario.dat` label change. Existing files and records stay byte-identical to the source.

Each case has a distinct saved name and existing unused Union commander, so the game-written resave can be checked by its four-field unit identity even if the game renumbers ordinal IDs. The parents come from explicit saved group IDs; Fort Monroe is resolved from its unique saved `garrisonrefs.dat` name/commander/world-position link. No name-based garrison classification determines parentage or placement.

| Case | Test hypothesis | New input path |
| --- | --- | --- |
| `ADC Batch Fort Infantry` | Infantry, Springfield M1861, 1,000 men, Maryland, directly under saved Fort Monroe group 27. | Omitted; game import should generate it as in Test 2. |
| `ADC Batch Fort Cavalry` | Cavalry, Sharps Carbine, 600 men, Pennsylvania, directly under group 27. | Omitted. |
| `ADC Batch Cavalry Stock` | Cavalry under tested 1st Infantry Division group 88, stock `(300,600,300,300)`. | Complete 250-line no-order record based on the uniquely matched game-written Test 2 cavalry path; identity and stock are changed. |
| `ADC Batch Artillery Stock` | Artillery under tested Division Artillery 2 group 75, stock `(60,30,30,60)`. | Complete 250-line no-order record based on the uniquely matched game-written Test 2 battalion path; identity and stock are changed. |
| `ADC Batch New Zouave` | New infantry under tested 1-1 INFBDE group 71 with first perk triple `(2,0,0)` for Zouave I. | Omitted; perk-on-creation acceptance is unverified. |

The builder validates version, complete fixed and variable record counts, contiguous IDs, unique four-field identities, unused officers, exact parent records, explicit fort reference, and file hashes. ADC's independent input check passed **20 checks**, including full parseability, compatible configured weapons, explicit parents, path presence/absence, stock, and perk triple. A separate resave check is compiled and ready to compare every case independently after **one game load and one paused manual save**.

Game acceptance and visible hierarchy are pending. [Test 14](EXPANDED_COMBINED_CREATION_EXPERIMENT.md) includes all five cases plus three more, and is the single requested game load. If the game rejects the expanded input, the exact failing case must be isolated before promoting any new rule. A successful combined load will validate the observed combinations separately, not all garrisons, stock levels, perk choices, towns, versions, or a production writer. Until then: **Unable to proceed** with these new creation combinations in ADC.
