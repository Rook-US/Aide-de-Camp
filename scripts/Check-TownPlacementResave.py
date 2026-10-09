"""Check a game-written resave of ADC Test 3 against the town placement target."""

import csv
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SAVE = Path(sys.argv[1])
if (SAVE / "version.dat").read_text().strip() != "1.142":
    raise ValueError("The game resave is not version 1.142")


def fixed_records(path, width):
    lines = path.read_text(encoding="utf-8-sig").splitlines()
    count = int(lines[0])
    if len(lines) != 1 + count * width:
        raise ValueError(f"Invalid count/width: {path.name}")
    return [lines[1 + i * width:1 + (i + 1) * width] for i in range(count)]


groups = fixed_records(SAVE / "groups.dat", 32)
commands = [r for r in groups if r[1] == "I. CORPS" and r[4] == "0"
            and r[2] == "-1" and r[3] == "0" and r[17] == "16"]
if len(commands) != 1:
    raise ValueError("The independent I. CORPS command is missing or ambiguous")
command = commands[0]

battle = (SAVE / "battledata.dat").read_text(encoding="utf-8-sig").splitlines()
count = int(battle[42])
tail = 43 + 15 * count
if tail >= len(battle) or len(battle) != tail + 1 + 2 * int(battle[tail]):
    raise ValueError("battledata.dat count/tail is malformed")
deployments = [battle[43 + 15 * i:43 + 15 * (i + 1)] for i in range(count)
               if battle[43 + 15 * i] == command[0] and battle[43 + 15 * i + 13] == "True"]
if len(deployments) != 1:
    raise ValueError("I. CORPS has no unique game-written deployment")

with (ROOT / "src" / "AideDeCamp" / "Data" / "TownStates1861.csv").open(newline="", encoding="utf-8") as f:
    town = [row for row in csv.DictReader(f) if row["town"] == "New York"]
if len(town) != 1 or town[0]["texture_state_id"] != "27":
    raise ValueError("Verified New York mapping is missing")
expected_x = float(town[0]["world_x"])
expected_map_z = -float(town[0]["world_z"])
actual_x, actual_map_z = map(float, deployments[0][2:4])
print("checked_save", SAVE)
print("command", command[1], "current_save_id", command[0], "parent", command[2], "native_tier", command[17])
print("deployment_map_x_z", actual_x, actual_map_z)
print("verified_town_map_x_z", expected_x, expected_map_z)
if abs(actual_x - expected_x) > 0.02 or abs(actual_map_z - expected_map_z) > 0.02:
    raise ValueError("Game-written I. CORPS deployment does not match New York")
print("PASS I. CORPS deployment matches verified New York coordinates")
