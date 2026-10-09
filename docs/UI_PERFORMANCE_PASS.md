# UI performance pass — 8 October 2026

Scope: local ADC build against the player's version-1.142 paused G-campaign save `Save8_10_2026_19_59_24`. The new `--roster-performance` UI harness loads the save read-only, displays each view, and measures the control action plus a completed WPF layout. Times below are observations on this machine, not a guaranteed latency target.

| View or action | Observed time | Visible rows after action |
| --- | ---: | ---: |
| Collapse all commands | 161 ms | 11 |
| Uncollapse all commands | 396 ms | 372 |
| Collapse native Corps | 245 ms | 40 |
| Uncollapse native Corps | 311 ms | 372 |
| Collapse native Divisions | 275 ms | 155 |
| Uncollapse native Divisions | 390 ms | 372 |
| Officers first view | 220 ms | 1,640 |
| Weapons first view | 115 ms | 87 |
| Navy first view | 145 ms | 69 |
| Economy first view | 147 ms | 37 |

Roster collapse formerly cleared and re-added every row to an observable collection, issuing one grid notification per row. The roster now prepares a complete row list and issues one reset. A direct comparison on 372 rows measured 374 ms with 373 individual notifications and 403 ms with one reset; this **does not demonstrate a redraw speedup** in that case. It does bound notification work and avoids intermediate grid states. Full WPF grid redraw remains the main observed cost. The roster and management grids now explicitly use recycling row and column virtualization. The roster no longer offers a blank add row.

Search previously rescanned subtrees during roster traversal; it now computes subtree matches once per refresh. Readiness alerts also refresh with one notification. Officers view previously scanned every command and unit for every officer; it now builds commander assignment and naval-branch lookups once per view refresh. Roster edits that require a Tree relayout now defer that hidden work until Tree is opened. These reduce repeated computation, but this pass has no isolated before/after timing for those changes.

The existing Tree view caches card controls and measured layouts, and its performance harness covers zoom, edit, undo, and redo. This pass kept that cache intact. The large Nation project/policy boards and naming preview were inspected for rebuild paths but were not separately profiled with representative user input. A later profile should target those interactions if users report delay there. The roster's 0.3–0.4 second expansion is still visible on this save; further gains require measuring WPF cell creation and binding cost, not assuming more model caching will help.

The new roster controls target saved native tier 15 and tier 14 groups. Their labels and tooltips follow the selected presentation: Corps/Division at brigade scale, Division/Brigade at regimental scale. The all and tier controls are available in Listing mode with headquarters visible; they are disabled in Flat mode or when headquarters are filtered out. A change in roster expansion is applied to the Tree view when that view is next opened.

Verification: the Windows UI suite passed **599** checks, including collapse/uncollapse, one roster collection notification per action, scale-aware wording, disabled states, and deferred Tree layout. The service suite passed **178** checks. The existing Tree performance harness passed its cache, geometry, edit, undo/redo, and cache-release checks. No save was written by the performance harness.
