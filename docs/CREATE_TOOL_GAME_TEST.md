# Production Create tool: combined game test 15

9 October 2026, save version 1.142. Created through the production backend in a
separate G-campaign copy, labeled **ADC Test 15 - Create Tool Batch**.
User's game-written result: `G/Save9_10_2026_10_41_24`.
The source and returned save were read only during this audit.

The user confirmed all Union units looked correct. Zouave II showed approximately
25% progress and Cold Steel II approximately 35–45%; exact saved progress is 0.25
and 0.35. Confederate units were not visible because the player was Union with fog
of war enabled. No Confederate visual verification is claimed.

| Creation | Result in game-written save |
| --- | --- |
| Union HQ | Philadelphia retained; Siege Train I and second empty slot at progress 1 retained |
| Division | Union HQ parent retained |
| Draft Infantry | 1,000 men, weapon 14, training/experience 60, 24-month draft contract, Zouave II at 0.25; stock 500 in each slot |
| Veteran Cavalry | 600 men, weapon 25, training/experience 80, Cold Steel II at 0.35; stock 300 in each slot |
| Horse Artillery | 60 men, weapon 4, horse artillery true, training/experience 40, perk 14 level I at 0.5; stock 30 in each slot |
| Fort Howitzers | Fort Monroe parent retained, 60 men, weapon 41, stock 60 in each slot |
| Confederate HQ | Richmond, native tier 15 and Field Telegraph I retained; commander changed from Leonidas Polk to Wade Hampton |
| Confederate Infantry | Correct parent, 1,000 men, weapon 14, training/experience 20, 36-month volunteer contract retained; commander changed from Wade Hampton to Lloyd J. Beall; empty eligible perk became ID 0, level I, progress 0 |

All Union commanders were retained. Opposing commander replacement and perk choice
are observed game behavior; the precise trigger has not been established. Creation
does not promise to freeze assignments on an AI-controlled side.

The resulting save contains 291 groups, 539 combat units and 830 complete paths.
Both Union HQs recalculated all four supply ratios to 50, agreeing with their
subordinate stock. The game renumbered combat records, including Fort Howitzers
537 → 59, cleared its finished transfer position and recalculated its supply-flow
value. These changes did not alter its four stock amounts or explicit parent.

Reproduce the read-only audit with the Verification project's `--creation-resave`
argument followed by the test input, game resave and configuration directory.
It compares the manifest, parsed current records and resolved parent identities,
never persistent line numbers. Complete output and file hashes are stored locally
in `artifacts/creation-mapping/test15-resave.log`.

This extends evidence to the combinations listed above, not every possible town,
HQ tier, perk, commander or version. Mapped combinations remain reviewable choices;
unknown layouts cannot be written.
