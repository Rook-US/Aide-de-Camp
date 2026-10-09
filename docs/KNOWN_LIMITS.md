# Known limits

- Version 0.8.16 is the **Unit Creation Test Build**, an alpha prerelease. Start with copies of saves. Specific game-tested combinations are documented in CREATE_TOOL_GAME_TEST.md; broader combinations require confirmation.
- Creation supports version 1.142 only. Unknown layouts, unresolved references and incompatible weapons cannot be bypassed by confirmation. Independent HQ placement uses the verified town mapping for 2 March 1861 through 19 June 1863.
- The game changed opposing-faction commanders and selected an available perk in Test 15. Confederate records survived, but fog of war prevented visual confirmation.
- Creation UI/writer checks pass. The broader WPF suite was interrupted by Windows clipboard access failure and is not counted as passed.
- New forts, Navy creation, new commanders, reusable templates and the Army Builder interface are not included.
- Mappings were investigated against game version 1.142. Other versions or DLC-specific structures may differ.
- Recruitment availability is projected from mapped population and campaign rules. The game determines the final usable pool; increasing population is not a guarantee of an identical immediate volunteer increase.
- HQ experience and abilities remain incompletely mapped. Do not infer them from troop or officer experience.
- Project IDs 91–94, 103, 110, 112, 116, and 117 have DLC/prewar restrictions or unresolved applicability. The editor retains restrictions rather than assuming they are ordinary universally available projects.
- Policy requests can set progress to 99.999% so the game can apply completion effects. Verify the result after the campaign advances.
- Weapon order editing concerns existing mapped orders. Delivery and effects still follow campaign processing.
- Portable builds target Windows x64 and are unsigned. Keep all extracted runtime files beside the executable.
