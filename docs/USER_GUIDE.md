# Using Aide-de-Camp

Extract the entire Windows ZIP and run Aide-de-Camp.exe. Select your game installation and open a copied campaign save. Changes are staged until you choose **Save changes**. Use **Review data** to inspect pending edits and the available undo controls to reverse edits.

Choose Union or Confederacy, then Armies, Garrisons, Navy, Officers, Weapons, or Nation. Double-click management rows to open their editor. The treasury edit control is beside the displayed balance.

## Batch editing

Use Ctrl/Shift to select multiple entries, then Batch Edit. Check each field you want to change. Only checked fields are applied; mixed values appear blank. Retained selections let you perform another batch operation without selecting the same entries again. Available fields depend on the selected entry type.

## Ships

Condition describes the ship's health, from 0 to 100 percent. Construction completed describes progress toward readiness: 100 completes active construction. Repair remaining describes unfinished repair work: 0 completes active repairs. Setting condition to 99 alone does not mean construction is 99 percent complete.

Construction and repair progress changes also adjust condition by the work delta, within 0–100, unless you explicitly edit condition. Only active construction or repair work can be advanced; these controls do not restart inactive work.

## Nation

States lists eligible Union/Confederate recruitment regions, excluding foreign regions. Recruitment adjustments use mapped population data and a projection; the campaign recalculates actual availability under its recruitment rules.

Projects & Funding groups projects under their subsidy category and shows the current funding balance rounded to whole dollars. Completed stage boxes are green. Double-click a stage to complete that stage and earlier stages; double-click a project row to complete its next stage. Direct save edits do not deduct a normal in-game purchase price.

Policies show a progress percentage and bar. Completion requests use near-complete progress (99.999%) where the game's own completion processing is needed. Resume the campaign to let it apply effects. Respect prerequisites and the restrictions shown in the editor.

## Files and troubleshooting

Preferences are stored under `%LOCALAPPDATA%\Aide-de-Camp`. Existing preferences from the former `GTCW.OOBEditor` location are read as a fallback; new settings use the new location. Save backups use `Aide-de-Camp_Backups`; older backup folders are excluded from recursive backup copies.

Application errors are written to the application's `Logs` folder when that location is writable. When reporting an issue, include the app version, the action that failed, and the relevant error text. Review logs before sharing them because they can contain local paths. Do not post game files or personal campaign saves in public issues.

The preview package is unsigned. See [known limits](https://github.com/Rook-US/Aide-de-Camp/blob/master/docs/KNOWN_LIMITS.md) for current validation boundaries.
