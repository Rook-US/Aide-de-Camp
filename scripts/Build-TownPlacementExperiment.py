"""Prepare one reversible 1.142 I. CORPS -> New York placement probe.

This is an isolated game-load experiment, not a production ADC placement writer.
It copies the confirmed game save and changes only battledata.dat deployment x/z
plus the scenario's visible test label. It never edits the source save.
"""

from __future__ import annotations

import csv
import hashlib
import json
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CAMPAIGN = Path(r"G:\SteamLibrary\steamapps\common\Grand Tactician The Civil War (1861-1865)\Campaigns\001\G")
SOURCE = CAMPAIGN / "Save7_10_2026_22_35_17"
TARGET = CAMPAIGN / "ADC-TEST-3-I-CORPS-NEW-YORK"
GROUP_NAME = "I. CORPS"
GROUP_COMMANDER = "0"
TOWN_NAME = "New York"
LABEL = "ADC Test 3 - I Corps to New York"


def read_text(path: Path) -> tuple[list[str], bytes, str]:
    raw = path.read_bytes()
    bom = b"\xef\xbb\xbf" if raw.startswith(b"\xef\xbb\xbf") else b""
    body = raw[len(bom):].decode("utf-8")
    separator = "\r\n" if "\r\n" in body else "\n"
    if body.replace(separator, "").find("\n") >= 0:
        raise ValueError(f"Mixed line endings in {path.name}")
    return body.splitlines(), bom, separator


def write_text(path: Path, lines: list[str], bom: bytes, separator: str) -> None:
    path.write_bytes(bom + (separator.join(lines) + separator).encode("utf-8"))


def fixed_records(path: Path, width: int) -> list[list[str]]:
    lines, _, _ = read_text(path)
    count = int(lines[0])
    if len(lines) != 1 + count * width:
        raise ValueError(f"Invalid {path.name} count or record width")
    return [lines[1 + i * width: 1 + (i + 1) * width] for i in range(count)]


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> None:
    if TARGET.exists():
        raise FileExistsError(f"Refusing to overwrite {TARGET}")
    if (SOURCE / "version.dat").read_text().strip() != "1.142":
        raise ValueError("Source is not the tested save version 1.142")
    groups = fixed_records(SOURCE / "groups.dat", 32)
    commands = [r for r in groups if r[1] == GROUP_NAME and r[4] == GROUP_COMMANDER]
    if len(commands) != 1:
        raise ValueError("Independent command identity is missing or ambiguous")
    command = commands[0]
    if command[2] != "-1" or command[3] != "0" or command[17] != "16":
        raise ValueError("Selected command is no longer a root Union native-tier-16 command")
    group_id = int(command[0])
    garrison_lines, _, _ = read_text(SOURCE / "garrisonrefs.dat")
    garrison_count = int(garrison_lines[0])
    if len(garrison_lines) != 1 + garrison_count * 8:
        raise ValueError("The source fort-reference count is malformed")
    if any(garrison_lines[1 + i * 8 + 4] == GROUP_NAME and
           garrison_lines[1 + i * 8 + 7] == GROUP_COMMANDER
           for i in range(garrison_count)):
        raise ValueError("Selected command is linked to a fort")

    with (ROOT / "src" / "AideDeCamp" / "Data" / "TownStates1861.csv").open(newline="", encoding="utf-8") as f:
        verified = [row for row in csv.DictReader(f) if row["town"] == TOWN_NAME]
    if len(verified) != 1 or verified[0]["texture_state_id"] != "27":
        raise ValueError("New York has no unique verified 1861 state/position identity")
    town = verified[0]
    x, y, z = (float(town[key]) for key in ("world_x", "world_y", "world_z"))
    town_lines, _, _ = read_text(SOURCE / "IIPsTowns.dat")
    exact_town_matches = [i for i in range(len(town_lines) - 6)
                          if town_lines[i] == TOWN_NAME
                          and town_lines[i + 4] in ("True", "False")
                          and town_lines[i + 5] == "0"
                          and all(abs(float(town_lines[i + 1 + j]) - v) < 0.001
                                  for j, v in enumerate((x, y, z)))]
    if len(exact_town_matches) != 1:
        raise ValueError("New York owner and position are not unique in the source save")

    battle_lines, battle_bom, battle_separator = read_text(SOURCE / "battledata.dat")
    deployment_count = int(battle_lines[42])
    tail = 43 + 15 * deployment_count
    if tail >= len(battle_lines) or len(battle_lines) != tail + 1 + 2 * int(battle_lines[tail]):
        raise ValueError("battledata.dat does not consume its counted sections")
    deployments = [43 + 15 * i for i in range(deployment_count)
                   if battle_lines[43 + 15 * i] == str(group_id)
                   and battle_lines[43 + 15 * i + 13] == "True"]
    if len(deployments) != 1:
        raise ValueError("Selected command has no unique group deployment")
    start = deployments[0]
    previous = (battle_lines[start + 2], battle_lines[start + 3])
    if previous == (str(x), str(-z)):
        raise ValueError("Selected command is already at New York")
    original_battle = battle_lines.copy()
    battle_lines[start + 2] = str(x)
    battle_lines[start + 3] = str(-z)
    changed = [i for i, (before, after) in enumerate(zip(original_battle, battle_lines)) if before != after]
    if changed != [start + 2, start + 3]:
        raise ValueError("The placement probe would change unexpected battledata fields")

    scenario_lines, scenario_bom, scenario_separator = read_text(SOURCE / "scenario.dat")
    if len(scenario_lines) < 26 or scenario_lines[2:5] != ["11", "7", "1861"]:
        raise ValueError("The source is not the confirmed July 1861 test save")
    scenario_lines[24] = LABEL

    shutil.copytree(SOURCE, TARGET)
    write_text(TARGET / "battledata.dat", battle_lines, battle_bom, battle_separator)
    write_text(TARGET / "scenario.dat", scenario_lines, scenario_bom, scenario_separator)
    changed_files = sorted(path.name for path in TARGET.iterdir() if sha(path) != sha(SOURCE / path.name))
    if changed_files != ["battledata.dat", "scenario.dat"]:
        raise ValueError(f"Unexpected changed files: {changed_files}")
    if fixed_records(TARGET / "groups.dat", 32) != groups:
        raise ValueError("The source command list changed")
    if (TARGET / "regiments.dat").read_bytes() != (SOURCE / "regiments.dat").read_bytes():
        raise ValueError("The source combat list changed")

    result = {
        "source": str(SOURCE), "test_save": str(TARGET), "save_version": "1.142",
        "command": GROUP_NAME, "command_commander": GROUP_COMMANDER,
        "source_group_id": group_id, "source_parent_id": -1, "saved_native_tier": 16,
        "town": TOWN_NAME, "town_state_id": 27, "town_owner": 0,
        "town_world_xyz": [x, y, z], "map_x_z_before": list(previous),
        "map_x_z_after": [battle_lines[start + 2], battle_lines[start + 3]],
        "changed_battledata_record": "unique group deployment for this save's command ID",
        "changed_files": changed_files,
        "remaining_check": "Load in game; confirm I. CORPS appears at New York, then manually resave for a cross-file diff."
    }
    manifest = ROOT / "artifacts" / "town-placement-experiment-2026-10-08.json"
    manifest.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
