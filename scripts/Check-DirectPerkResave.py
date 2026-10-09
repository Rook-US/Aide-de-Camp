"""Check a game-written Test 5 resave without relying on unit IDs or line numbers."""

from pathlib import Path
import sys

INPUT = Path(r"G:\SteamLibrary\steamapps\common\Grand Tactician The Civil War (1861-1865)\Campaigns\001\G\ADC-TEST-5-DIRECT-ZOUAVE-I")
GAME_SAVE = Path(sys.argv[1])


def target(folder):
    if (folder / "version.dat").read_text().strip() != "1.142":
        raise ValueError(f"Unsupported save version: {folder}")
    values = (folder / "regiments.dat").read_text(encoding="utf-8-sig").splitlines()
    count = int(values[0])
    if len(values) != 1 + 39 * count:
        raise ValueError(f"Malformed regiment count: {folder}")
    records = [values[1 + 39 * i:1 + 39 * (i + 1)] for i in range(count)]
    matches = [r for r in records if r[1] == "ADC Test 1-1 Infantry"
               and r[4] == "0" and r[5] == "91"]
    if len(matches) != 1:
        raise ValueError(f"Target unit missing or ambiguous: {folder}")
    return matches[0]


if GAME_SAVE.resolve() == INPUT.resolve():
    raise ValueError("A separate game-written save is required")
before = target(INPUT)
after = target(GAME_SAVE)
if before[17:20] != ["2", "0", "0"]:
    raise ValueError("Test input is not the prepared direct Zouave I record")
if [before[i] for i in (1, 3, 4, 5)] != [after[i] for i in (1, 3, 4, 5)]:
    raise ValueError("Target identity changed")
print("game_save", GAME_SAVE)
print("target", after[1], "input_id", before[0], "game_id", after[0])
print("input_perk", before[17:20], "game_perk", after[17:20])
if after[17:20] != ["2", "0", "0"]:
    raise ValueError("The game did not retain the directly saved Zouave I triple")
print("PASS Game retained the directly saved Zouave I triple")
