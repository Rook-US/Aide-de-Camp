"""Read-only perk level/experience audit across the G campaign saves."""

from collections import defaultdict
from pathlib import Path

ROOT = Path(r"G:\SteamLibrary\steamapps\common\Grand Tactician The Civil War (1861-1865)\Campaigns\001\G")
FOLDERS = [ROOT] + sorted(
    (p for p in ROOT.iterdir() if p.is_dir() and p.name.startswith("Save")
     and (p / "regiments.dat").exists()), key=lambda p: p.name)


def read(folder):
    lines = (folder / "regiments.dat").read_text(encoding="utf-8-sig").splitlines()
    count = int(lines[0])
    if len(lines) != 1 + count * 39:
        raise ValueError(f"Bad record count: {folder}")
    return [lines[1 + i * 39 : 1 + (i + 1) * 39] for i in range(count)]


for folder in FOLDERS:
    results = defaultdict(list)
    for record in read(folder):
        if record[17] in ("2", "9"):
            results[(int(record[17]), int(record[18]))].append((record[1], float(record[19])))
    print("SAVE", folder.name)
    for (perk, level), units in sorted(results.items()):
        values = [exp for _, exp in units]
        print(" ", "Zouave" if perk == 2 else "Cold Steel", "level", level,
              "units", len(values), "xp_range", min(values), max(values),
              "nonzero", sum(value > 0 for value in values))
        if folder.name == "Save7_10_2026_22_35_17":
            print("   ", units)
