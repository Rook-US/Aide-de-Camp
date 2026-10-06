# Tree card cache and latency: 0.8.12

Validated October 6, 2026 on Windows x64 using synthetic data only.

## Behavior

Save loading creates retained card controls and prepares compact and detailed footprints for both factions, including off-screen and collapsed formations. Preparation yields between cards while the save is loading. The application retains one current visual per node/template, current metric rows, and measurements keyed by content signature. Unchanged metric rows keep the same objects when a broader data refresh occurs.

Zoom changes detail visibility and recalculates positions/connectors from the cached footprints; it does not recreate the Tree cards or bindings. Individual unit edits refresh that unit and its ancestor summaries. Changed content can require new rows or measurements for the affected cards; unchanged cards remain alive. Refreshes, batches, undo/redo, dragging, and hierarchy visibility changes reuse eligible controls. Earlier footprint signatures remain in memory for undo/redo.

Loading another save clears these caches before parsing the new save. Closing clears them and aborts pending preparation. A generation check prevents an old preparation callback from populating a newer cache. Font/card/presentation settings deliberately invalidate geometry and controls, since they affect the entire layout. This trades additional save-load preparation and retained memory for fast interaction; cached data is not written to disk.

## Measured result

The same 331 visible cards (300 units, 31 HQs), plus two other-faction cards, were used with 0.8.11 commit `aefc994` and 0.8.12. The benchmark alternates 80% and 100% zoom four times in each direction. Timing includes UI layout and dispatcher settling after save preparation, excluding save parsing/preparation and GPU presentation. Values are local observations, not guaranteed timings on every machine.

| Direction | 0.8.11 median | 0.8.12 median | 0.8.12 range |
| --- | ---: | ---: | ---: |
| Expand details | 2,603 ms | 46.25 ms | 40.5–57.8 ms |
| Collapse details | 1,285.7 ms | 41.95 ms | 40.4–46.4 ms |

Every transition retains all 331 card instances; 0.8.11 retained zero. The first prepared expansion measured 57.8 ms. Allocations per transition fell from roughly 141–281 MB to about 8.5 MB. These are cumulative UI-thread allocations, not retained memory usage. Raw measurements: [0.8.11](benchmarks/tree-0.8.11.csv), [0.8.12](benchmarks/tree-0.8.12.csv).

## Verification

- Release solution build: zero warnings/errors.
- 152 synthetic service checks.
- 569 existing WPF checks.
- 255 Tree geometry, zoom, spacing, viewport, and Nudge checks.
- 15 cache checks: rendered positions before/after transitions, both factions prepared, both footprints cached, all cards reused across zoom, single edits update visible content without notifying unrelated HQs, old measurements retained, undo/redo and batch undo reuse controls, new-save invalidation, matching-ID isolation, and cache disposal on close.
- Actual rendered compact cards were visually inspected; the previous layout and essential content remain intact.
- Extracted 0.8.12 portable package startup passed using bundled .NET 8.0.30.

Run with isolated preferences:

```powershell
$env:AIDE_DE_CAMP_DATA = Join-Path $PWD 'artifacts/test-preferences'
dotnet run --project tests/UiVerification -c Release -- --tree-performance artifacts/tree-performance
```

CI runs the behavioral checks and records timing CSVs without a machine-dependent time limit. These checks do not replace running-campaign acceptance or testing an especially large real save on the user's machine.
