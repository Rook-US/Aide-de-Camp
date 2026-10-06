# Aide-de-Camp 0.8.12 preview

Save loading prepares compact and detailed Tree cards for both factions. Zoom reuses those controls and cached measurements instead of rebuilding the entire tree. Individual edits refresh changed cards and ancestor summaries; unchanged metric rows remain alive. Previous measurements remain available for undo/redo. Loading another save or closing clears the cache.

A 331-card synthetic tree measured 40–70 ms transitions after preparation, compared with roughly 1–3 seconds in 0.8.11 on the same machine. Loading does more preparation and retains more UI data in memory; font/card settings changes deliberately invalidate their geometry. See [cache and performance validation](TREE_CACHE_VALIDATION.md).

# Aide-de-Camp 0.8.11 preview

Tree view uses actual card widths and additive edge gaps, with compact defaults and larger text. Compact HQ and combat cards retain parent command, manpower, casualties, and meaningful alerts; HQ alerts include subordinates. Supplemental details expand only at the configured zoom threshold and release their space when hidden. Nudge offsets and hierarchy undo/redo remain intact, and font/detail reflow preserves a nearby card's screen position.

Existing preferences remain intact: reset the card, spacing, and zoom sections to adopt the new defaults. See [Tree validation and before/after images](TREE_VALIDATION.md) for measured results and boundaries.

# Aide-de-Camp 0.8.10 preview

The editor now uses the Aide-de-Camp name and ships as a Windows x64 ZIP with its .NET runtime included. Extract the entire ZIP and run Aide-de-Camp.exe. No separate .NET installation is required.

This first dedicated Aide-de-Camp release includes army and garrison editing, ships and readiness controls, officer attributes and fame, weapon management, retained batch selections, and Nation views for treasury, eligible recruitment states, projects, subsidies, and policies. Existing preferences from GTCW.OOBEditor are read as a fallback.

The repository includes MIT-licensed source, retained starter history, a development history, architecture notes, build instructions, and automated verification. Runtime license notices are included in the downloadable package.

This is a preview: automated checks cover save handling, WPF interaction, and packaged startup; acceptance testing of edited effects inside a running campaign remains outstanding. Use a copied campaign. See the repository's known-limits document for mapping and progression restrictions.
