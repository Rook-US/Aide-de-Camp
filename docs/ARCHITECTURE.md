# Architecture and save mapping

The application targets .NET 8 and WPF. `src/AideDeCamp` contains the application; `tests/Verification` contains service checks; `tests/UiVerification` exercises the WPF interface on Windows. Both verification programs can run with synthetic fixtures without distributing game data.

## Important code areas

| Area | Starting point |
| --- | --- |
| Window, navigation, management interaction | `MainWindow.xaml` and its partial C# files |
| Campaign parsing and saving | `Services/GrandTacticianDataService.cs` |
| Management records and writes | `Services/ManagementDocument.cs`, `ManagementSnapshot.cs` |
| Projects and policies | `Services/NationProgression.cs` |
| Recruitment projection | `Services/RecruitmentProjection.cs`, `CampaignRules.cs` |
| Batch planning | `Services/BatchEditPlanner.cs`, `ManagementBatchPlanner.cs` |
| Typed edits, validation, undo | `TypedUnitEdit.cs`, `EditValidationService.cs`, `EditSession.cs` |
| Settings and legacy migration | `Services/AppPaths.cs` and settings services |
| Layout and metrics | `Services/CardLayoutGeometry.cs`, `FormationMetrics.cs` |

## Save format principles

Save positions are discovered through counted sections and record structure. They are not universal line numbers: adding records changes later positions. Keep the original text buffer and replace only mapped values or explicitly planned section changes. Do not rewrite unrelated fields or normalize an entire file merely to update one value.

Mappings were investigated against game version 1.142. Validate section counts, IDs, ranges, and links before applying edits. Unknown or unsupported structures should remain untouched. Existing backup, review, undo, and conflict handling must remain intact.

Examples of current mappings (offsets are zero-based within their mapped record):

- Officer records have 66 fields. Experience, fame, leadership, initiative, administration, and cunning are at offsets 5–10. The editor accepts 0–100; finite legacy values outside the normal range are preserved until deliberately edited.
- Ship records have 23 fields. Construction and repair remaining at offsets 17 and 18 use fractions from 0 to 1. The UI converts construction to completed percentage and repair to remaining percentage. Condition is a separate percentage field.
- Treasury editing updates the mapped mirrored nation values together. National values are located through their enclosing counted structure.
- Repeated project IDs represent stages. Policy progress and previously enacted acts are distinct structures; near-complete progress lets the campaign run its own completion path.

The source and verification fixtures are the authority for exact field mappings. These examples are orientation, not a substitute for parser validation. Never add game binaries, decompiled game source, game assets, real saves, personal logs, or machine-specific evidence bundles to this repository.
