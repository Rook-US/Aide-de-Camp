"""Read-only comparison of 1st US Cavalry across the G campaign and ADC tests."""

from pathlib import Path
import hashlib

ROOT = Path(r"G:\SteamLibrary\steamapps\common\Grand Tactician The Civil War (1861-1865)\Campaigns\001\G")
EXPERIMENT = Path(__file__).resolve().parents[1] / "artifacts" / "creation-experiment-2026-10-07"
SAVES = [
    ("Initial campaign G", ROOT),
    ("Pre-test save", ROOT / "Save5_10_2026_21_55_19"),
    ("ADC source copy", EXPERIMENT / "source-save"),
    ("ADC stage 1 local", EXPERIMENT / "stage-1-fort-artillery"),
    ("ADC stage 2 local", EXPERIMENT / "stage-2-all-requested"),
    ("ADC test 1 game copy", ROOT / "ADC-TEST-1-FORT-MONROE"),
    ("ADC test 2 game copy", ROOT / "ADC-TEST-2-ALL-UNITS"),
    ("Latest game resave", ROOT / "Save7_10_2026_22_35_17"),
]


def records(folder):
    lines = (folder / "regiments.dat").read_text(encoding="utf-8-sig").splitlines()
    count = int(lines[0])
    if len(lines) != 1 + count * 39:
        raise ValueError(f"Malformed regiment count: {folder}")
    return [lines[1 + i * 39 : 1 + (i + 1) * 39] for i in range(count)]


baseline = None
for label, folder in SAVES:
    if not (folder / "regiments.dat").exists():
        print(label, "MISSING")
        continue
    found = [r for r in records(folder) if r[1] == "1st US Cavalry" and r[4] == "1"]
    if len(found) != 1:
        print(label, "MATCHES", len(found))
        continue
    r = found[0]
    if baseline is None:
        baseline = r
    differences = [(i, baseline[i], r[i]) for i in range(39) if baseline[i] != r[i]]
    digest = hashlib.sha256("\n".join(r).encode()).hexdigest()[:16]
    print(label, "id", r[0], "parent", r[3], "commander", r[5],
          "perk", r[17:20], "record_sha256_prefix", digest,
          "differences_from_initial", differences)

source = records(EXPERIMENT / "source-save")
for label in ("stage-1-fort-artillery", "stage-2-all-requested"):
    stage = records(EXPERIMENT / label)
    print(label, "existing_records_identical_to_source", stage[:len(source)] == source,
          "appended_records", len(stage) - len(source))
