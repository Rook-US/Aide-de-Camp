# Test 4: choose a perk in game

Prepared 8 October 2026, local only. This is a controlled **game-side experiment** and does not enable an ADC perk write control. The copied 1.142 save is `Campaigns/001/G/ADC-TEST-4-INFANTRY-PERK-CHOICE`, sourced from the player's confirmed game resave `Save7_10_2026_22_35_17`. The source remains unchanged.

The target is the unique infantry record named `ADC Test 1-1 Infantry`, commander ID 91, under `1-1 INFBDE`. Its first perk slot in the game-written source is ID `-1`, level `0`, experience `0`. The experiment changes **only that slot's experience** to `1` in `regiments.dat`. The installed game's `Regiment.ChoosePerk` tests for an unused slot with at least 1 experience before recording a selected perk ID, level 0, and experience 0. `scenario.dat` label changes to **ADC Test 4 - Choose Zouave I**. No unit identity, parent, type, equipment, strength, supply, path, or location field is changed.

[`Build-PerkAssignmentExperiment.py`](../scripts/Build-PerkAssignmentExperiment.py) refuses to overwrite the copy, checks version 1.142, count and width of all combat records, the target's unique identity and blank slot, and the exact one-line regiment diff. The copied save passed ADC's 18 game-resave checks and nine town checks. These parse checks do not prove that the game offers a perk choice or accepts Zouave I for this unit.

## Game result

The player loaded the test, confirmed that the unit had a perk choice, and chose **Zouave I**. The game's manual save is `Campaigns/001/G/Save8_10_2026_12_55_51`, labeled **ADC Test 4 - Zouave I 1-1BDE**. The read-only [`Check-PerkAssignmentResave.py`](../scripts/Check-PerkAssignmentResave.py) resolved the same infantry record by name, type, and commander and found the game-written first slot `2,0,0`, compared with `-1,0,1` in the test input and `-1,0,0` in the original source. The installed `PerkBattleTooltips.txt` maps perk ID 2, level 0 to **Zouave I**. The player's UI observation and the game's resave agree. ADC reloaded the game save: all 18 creation-resave checks and nine town checks passed.

This validates the **game's choice route** for one 1.142 infantry unit with an earned point in an empty slot. It does not prove that a freshly inserted perk `2,0,0` in `regiments.dat` is accepted without the game's choice action, nor does it prove eligibility for another branch, command tier, or perk level. A direct insertion test and visible in-game result are still required before ADC offers editable starting perks. No production write control was added.
