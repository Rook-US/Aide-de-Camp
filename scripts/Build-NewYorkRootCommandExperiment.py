"""Prepare a disposable 1.142 test of a new independent HQ at New York.

This is a game-load probe, not a production creation writer. It refuses to
replace an existing test and leaves every source file unchanged.
"""

from __future__ import annotations

import csv
import hashlib
import json
import shutil
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
CAMPAIGN = Path(r"G:\SteamLibrary\steamapps\common\Grand Tactician The Civil War (1861-1865)\Campaigns\001\G")
SOURCE = CAMPAIGN / "Save8_10_2026_19_59_24"
TARGET = CAMPAIGN / "ADC-TEST-10-NEW-YORK-ROOT-HQ"
NAME = "ADC Test New York HQ"
COMMANDER_ID = 96  # John McArthur: existing Union officer, unused in source OOB.
LABEL = "ADC Test 10 - New York Root HQ"
TOWN_NAME = "New York"
TOWN_STATE_ID = "27"
EXPECTED_WORLD = (1777.0, 489.2545, -475.0)


def read(path: Path) -> tuple[list[str], bytes, str]:
    raw = path.read_bytes()
    bom = b"\xef\xbb\xbf" if raw.startswith(b"\xef\xbb\xbf") else b""
    body = raw[len(bom):].decode("utf-8")
    separator = "\r\n" if "\r\n" in body else "\n"
    if "\n" in body.replace(separator, ""):
        raise ValueError(f"Mixed line endings: {path.name}")
    return body.splitlines(), bom, separator


def write(path: Path, lines: list[str], bom: bytes, separator: str) -> None:
    path.write_bytes(bom + (separator.join(lines) + separator).encode("utf-8"))


def fixed(lines: list[str], width: int, file_name: str) -> list[list[str]]:
    count = int(lines[0])
    if count < 0 or len(lines) != 1 + count * width:
        raise ValueError(f"{file_name}: invalid fixed record count or width")
    rows = [lines[1 + width * i:1 + width * (i + 1)] for i in range(count)]
    if [int(row[0]) for row in rows] != list(range(count)):
        raise ValueError(f"{file_name}: IDs are not contiguous saved ordinals")
    return rows


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> None:
    if TARGET.exists():
        raise FileExistsError(f"Refusing to replace existing test: {TARGET}")
    if (SOURCE / "version.dat").read_text(encoding="utf-8-sig").strip() != "1.142":
        raise ValueError("Source save is not version 1.142")

    group_lines, group_bom, group_sep = read(SOURCE / "groups.dat")
    groups = fixed(group_lines, 32, "groups.dat")
    regiment_lines, _, _ = read(SOURCE / "regiments.dat")
    regiments = fixed(regiment_lines, 39, "regiments.dat")
    if any(row[1] == NAME for row in groups):
        raise ValueError("The test HQ name already exists")
    if any(row[4] == str(COMMANDER_ID) for row in groups) or any(row[5] == str(COMMANDER_ID) for row in regiments):
        raise ValueError("The selected commander is already assigned")
    commander_lines, _, _ = read(SOURCE / "commanders.txt")
    commanders = fixed(commander_lines, 66, "commanders.txt")
    officer = [row for row in commanders if row[0] == str(COMMANDER_ID)]
    if len(officer) != 1 or officer[0][3] != "John McArthur" or officer[0][4] != "0" or int(officer[0][59]) < 6:
        raise ValueError("The chosen existing Union officer is not uniquely verified")

    with (ROOT / "src" / "AideDeCamp" / "Data" / "TownStates1861.csv").open(newline="", encoding="utf-8") as stream:
        towns = [row for row in csv.DictReader(stream) if row["town"] == TOWN_NAME]
    if len(towns) != 1 or towns[0]["texture_state_id"] != TOWN_STATE_ID:
        raise ValueError(f"{TOWN_NAME}'s verified playable-state mapping is missing")
    town = towns[0]
    x, y, z = (float(town[key]) for key in ("world_x", "world_y", "world_z"))
    if (x, y, z) != EXPECTED_WORLD:
        raise ValueError(f"{TOWN_NAME}'s verified world position changed")
    town_lines, _, _ = read(SOURCE / "IIPsTowns.dat")
    matches = [i for i in range(len(town_lines) - 6)
               if town_lines[i] == TOWN_NAME and town_lines[i + 5] == "0"
               and town_lines[i + 4] in ("True", "False")
               and all(abs(float(town_lines[i + j + 1]) - v) < 0.001 for j, v in enumerate((x, y, z)))]
    if len(matches) != 1:
        raise ValueError(f"The current save does not have one Union-owned {TOWN_NAME} town at the verified position")

    battle_lines, battle_bom, battle_sep = read(SOURCE / "battledata.dat")
    deployments = int(battle_lines[42])
    tail = 43 + deployments * 15
    if tail >= len(battle_lines) or len(battle_lines) != tail + 1 + 2 * int(battle_lines[tail]):
        raise ValueError("battledata.dat count or economy tail is invalid")
    if any(battle_lines[43 + i * 15] == str(len(groups)) and battle_lines[43 + i * 15 + 13] == "True"
           for i in range(deployments)):
        raise ValueError("New command ID is already deployed")
    scenario_lines, scenario_bom, scenario_sep = read(SOURCE / "scenario.dat")
    if scenario_lines[2:5] != ["11", "7", "1861"] or len(scenario_lines) < 26:
        raise ValueError("Source is not the confirmed July 1861 campaign date")

    new_id = len(groups)
    # Hypothesis based on CreateUnitDefault plus the game's 32-field writer and
    # the accepted Test 2 command. Test 10 must validate native tier 16.
    command = [
        str(new_id), NAME, "-1", "0", str(COMMANDER_ID), "100",
        "100", "100", "100", "100", "100", "100", "100", "-1", "0",
        "000-049-083", "135-206-235", "16", "0", "0",
        "-1", "0", "0", "-1", "0", "0", "-1", "0", "0", "-1", "0", "0",
    ]
    if len(command) != 32:
        raise AssertionError("Incorrect command width")
    # Deployment offsets: ID, formation, map x/z, facing/entry angles,
    # reinforcement delay, AI group/stance, entrenchment, objective z,
    # order activation time, trigger, isGroup, readiness.
    deployment = [str(new_id), "1", str(x), str(-z), "0", "0", "0", "-1", "0", "0", "0", "0", "0", "True", "100"]
    if len(deployment) != 15:
        raise AssertionError("Incorrect deployment width")
    group_lines[0] = str(new_id + 1)
    group_lines.extend(command)
    battle_lines[42] = str(deployments + 1)
    battle_lines[tail:tail] = deployment
    scenario_lines[24] = LABEL

    shutil.copytree(SOURCE, TARGET)
    write(TARGET / "groups.dat", group_lines, group_bom, group_sep)
    write(TARGET / "battledata.dat", battle_lines, battle_bom, battle_sep)
    write(TARGET / "scenario.dat", scenario_lines, scenario_bom, scenario_sep)
    changed = sorted(path.name for path in TARGET.iterdir() if path.is_file() and digest(path) != digest(SOURCE / path.name))
    if changed != ["battledata.dat", "groups.dat", "scenario.dat"]:
        raise ValueError(f"Unexpected file changes: {changed}")
    target_groups, _, _ = read(TARGET / "groups.dat")
    target_battle, _, _ = read(TARGET / "battledata.dat")
    if fixed(target_groups, 32, "groups.dat")[-1] != command or target_battle[43 + deployments * 15:43 + (deployments + 1) * 15] != deployment:
        raise ValueError("Written records did not reparse exactly")
    if len(target_battle) != len(battle_lines) or len(target_battle) != 43 + (deployments + 1) * 15 + 1 + 2 * int(target_battle[43 + (deployments + 1) * 15]):
        raise ValueError("Written battledata tail did not reparse")
    print(json.dumps({"test_copy": str(TARGET), "version": "1.142", "label": LABEL,
                      "command_id": new_id, "commander_id": COMMANDER_ID,
                      "town": TOWN_NAME, "world_position": [x, y, z],
                      "changed_files": changed,
                      "status": "Awaiting in-game load, position check, and manual resave"}, indent=2))


if __name__ == "__main__":
    main()
