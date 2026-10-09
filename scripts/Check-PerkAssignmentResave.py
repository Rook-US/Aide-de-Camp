"""Read-only check of the controlled Test 4 infantry perk assignment."""

from pathlib import Path
import sys

SOURCE = Path(r"G:\SteamLibrary\steamapps\common\Grand Tactician The Civil War (1861-1865)\Campaigns\001\G\Save7_10_2026_22_35_17")
SAVE = Path(sys.argv[1])


def records(folder):
    if (folder / "version.dat").read_text().strip() != "1.142":
        raise ValueError(f"Unsupported version: {folder}")
    lines = (folder / "regiments.dat").read_text(encoding="utf-8-sig").splitlines()
    count = int(lines[0])
    if len(lines) != 1 + 39 * count:
        raise ValueError(f"Malformed regiment count: {folder}")
    return [lines[1 + 39 * i:1 + 39 * (i + 1)] for i in range(count)]


def target(folder):
    matches = [r for r in records(folder) if r[1] == "ADC Test 1-1 Infantry"
               and r[4] == "0" and r[5] == "91"]
    if len(matches) != 1:
        raise ValueError(f"Target unit missing or ambiguous: {folder}")
    return matches[0]


original = target(SOURCE)
current = target(SAVE)
if original[17:20] != ["-1", "0", "0"]:
    raise ValueError("Source slot is not empty")
if [original[i] for i in (1, 3, 4, 5)] != [current[i] for i in (1, 3, 4, 5)]:
    raise ValueError("Target identity changed")

print("game_save", SAVE)
print("target", current[1], "current_id", current[0], "unit_type", current[4], "commander", current[5])
print("source_perk", original[17:20], "game_perk", current[17:20])
if current[17:20] == ["2", "0", "0"]:
    print("PASS Game saved Zouave I in the previously empty slot")
elif current[17:20] == ["-1", "0", "1"]:
    print("PENDING The earned point remains unspent; game assignment is not established")
else:
    print("UNEXPECTED The game saved a different perk state; inspect before use")
