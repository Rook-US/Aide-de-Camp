# Test 5: directly saved Zouave I

Prepared 8 October 2026, local only. This disposable 1.142 copy tests whether the game accepts a selected perk **already written in the record**, without using the in-game choice action. It does not enable an ADC perk write control.

[`Build-DirectPerkExperiment.py`](../scripts/Build-DirectPerkExperiment.py) copies the confirmed game save `Save7_10_2026_22_35_17` to `ADC-TEST-5-DIRECT-ZOUAVE-I`. It resolves the unique `ADC Test 1-1 Infantry` record by name, type, and commander and changes its previously empty first perk slot from `-1,0,0` to `2,0,0`, the exact game-written Zouave I triple observed after the player's choice in [Test 4](PERK_ASSIGNMENT_EXPERIMENT.md). The only changed regiment line is that slot's perk ID; level and experience were already zero. The other changed file is `scenario.dat` for the visible label **ADC Test 5 - Direct Zouave I**. The original save remains unchanged, and the builder refuses to overwrite the test copy.

ADC reload checks passed on the prepared input: 18 creation-resave checks and nine town checks.

## Game result

The player loaded the copy, confirmed success in game, and manually saved as `Campaigns/001/G/Save8_10_2026_13_0_48`, labeled **ADC Test 5 - Success**. The read-only [`Check-DirectPerkResave.py`](../scripts/Check-DirectPerkResave.py) resolves the unit by saved name, faction, type, and commander rather than record position; it found that the game retained `2,0,0` in the first perk slot. ADC reloaded the game save: 18 creation-resave checks and nine town checks passed. This validates direct insertion of **Zouave I in the first slot of this 1.142 infantry unit**.

The result does not establish eligibility or saved effects for other perk IDs, unit branches, command perks, or starting slot availability on newly created units. A production perk selector must allow only independently validated combinations and reject unsupported versions. No production write control was added by this experiment.
