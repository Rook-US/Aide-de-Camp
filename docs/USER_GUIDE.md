# Using Aide-de-Camp

Extract the entire Windows ZIP and run Aide-de-Camp.exe. Select your game installation and open a copied campaign save. Changes are staged until you choose **Save changes**. Use **Review data** to inspect pending edits and the available undo controls to reverse edits.

Choose Union or Confederacy, then Armies, Garrisons, Navy, Officers, Weapons, or Nation. Double-click management rows to open their editor. The treasury edit control is beside the displayed balance.

## Tree view

Compact cards show the unit/command name, ranked commander, immediate parent, assigned manpower, casualties, and strength/transfer/contract alerts. HQ alerts include subordinate formations and missing commanders. Guns remain separate from manpower; assigned strength includes units in transfer. Zoom to 95% for supplemental metrics, weapons, contracts, and unit experience, or set your own detail threshold in UI settings. Details collapse below that threshold, releasing their layout space.

Tree cards for both factions are prepared while a save loads. Zoom and individual edits reuse the cards; only changed content and affected summaries update. Undo/redo keeps earlier measurements available. A new save or closing the app clears this in-memory cache. Changing font/card settings rebuilds geometry to match the new settings.

Start around 80% zoom for readable compact cards. Text scale defaults to 1.15. Card and text changes reflow the layout while retaining a nearby card's screen position. Very distant overview zoom still reduces text size.

Spacing controls use world pixels at 100% zoom. A base horizontal gutter adds to the HQ or combat column gap; mixed boundaries average the two column gaps. Parent-row gaps add the base row gutter. A shared row clears its tallest card and largest applicable tier gap, so another branch can determine the minimum row separation. Root gaps and combat stack gaps are direct edge gaps. Zero allows touching footprints; a small positive gutter keeps connectors easier to follow. Subtree clearance can require more space than a local gap.

Saved preferences are retained. To adopt the new defaults from an older installation, reset the **Unit and HQ cards**, **Formation spacing**, and **Zoom detail** sections in UI settings. Nudge continues to apply intentional offsets after automatic layout; Save changes retains those offsets in display metadata. Manual offsets can intentionally overlap cards. Reset nudges removes them. Hierarchy edit undo/redo retains presentation nudges.

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
