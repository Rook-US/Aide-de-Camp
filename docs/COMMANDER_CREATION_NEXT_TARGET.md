# Next target: create commanders in a running save

This follows the land-unit creation and starting-supply work. No commander insertion is enabled yet. Keep all work local until the user explicitly authorizes GitHub publication.

## Required format and game gate

The current 1.142 reader verifies `commanders.txt` as a count followed by exactly **66 lines per commander**, and rejects duplicate saved IDs. ADC can already edit selected existing fields: experience, fame, leadership, initiative, administration, cunning, veteran, West Point, political status, branch, and promotion-date triplets. The installed `ImportExportUnitData.SaveCommanders` delegates each record to `GameVars.Commander.Save` and also writes `attributes.dat`. A fresh IL trace maps all 66 line meanings below; **valid initial values**, ID allocation, auxiliary attribute records, portrait references, rank/date behavior, and links to commands or combat units still need a controlled game load/resave. An existing-record editor is not evidence that a new commander record is valid.

| Offset | `Commander.Save` source field |
| ---: | --- |
| 0 | Saved commander ID, passed by the containing writer. |
| 1–3 | `name`, `firstname`, `combinedname`. |
| 4 | `alliance` faction. |
| 5–10 | Experience, fame, leadership, initiative, administration, cunning; runtime fractions multiplied by 100 when saved. |
| 11–13 | Veteran, West Point, historical branch. |
| 14–16 | Birth day, month, year. |
| 17–43 | Nine promotion-date triplets, day/month/year for ranks 1–9. |
| 44–46 | Fame date day/month/year. |
| 47–49 | Defame date day/month/year. |
| 50–52 | Unavailability date day/month/year. |
| 53–55 | Use dummy photo (0/1), state ID, political flag. |
| 56–58 | Service history, bonus/malus experience, battle count. |
| 59–61 | Rank, status, latest rank to fit. |
| 62–65 | Portrait ID, portrait filename, frame ID, active-in-campaign flag. |

The record order above is from the installed game's writer rather than guesses from neighboring save records. It does not establish which saved values make a new officer eligible for selection or how `attributes.dat` and portrait data must accompany the new ID.

Before any Create control, document every 66-line field, all companion records and references, the exact insertion/count/ID rule, supported game and save versions, and game-observed behavior. Prepare one copied-save manual-add experiment with an unassigned officer, then a game resave. Reject malformed counts, duplicate IDs, unresolved portrait/attribute references, invalid faction/branch/rank/date combinations, and unsupported versions before writing. Integrate with ADC's backup and one-step undo path only after those checks pass.

## Planned user flows

1. **Add one commander:** choose faction, name, branch, rank/promotion dates, and validated initial stats; preview the full saved record and assignment eligibility. Assignment to a newly created or existing unit is a separate explicit choice.
2. **Batch add commanders:** table preview with editable rows, validation summary, duplicate detection against the save and within the batch, and one atomic save transaction. A failed row blocks the batch rather than leaving partial inserts.
3. **Generate generic commanders:** configurable count and independent min/max ranges for each verified numeric stat; optional branch, rank, and faction distributions; reproducible seed; preview, regenerate selected rows, and manual overrides. Name generation should use a documented period-appropriate first/middle/surname corpus with many combinations and exclusion of saved or generated duplicate full names. The generated name is presentation text and must not infer faction, rank, parent, location, or any other saved value.

Portrait management remains a separate follow-up unless the commander format gate proves that a verified no-portrait value loads and resaves safely. Supply, unit creation, and the existing save transaction remain the current implementation priority.
