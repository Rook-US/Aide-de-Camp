"""Prepare a disposable 1.142 new-infantry stock/path import probe.

The copied path state is a test hypothesis from a game-written new infantry
record. It must not be used by ADC's production writer before a game check.
"""

from __future__ import annotations

import hashlib
import json
import math
import shutil
from pathlib import Path


CAMPAIGN = Path(r"G:\SteamLibrary\steamapps\common\Grand Tactician The Civil War (1861-1865)\Campaigns\001\G")
SOURCE = CAMPAIGN / "Save8_10_2026_19_59_24"
TARGET = CAMPAIGN / "ADC-TEST-11-NEW-INFANTRY-HALF-STOCK"
OLD_NAME = "ADC Test 1-1 Infantry"
NEW_NAME = "ADC Test New Half-Stock Infantry"
OLD_COMMANDER = 91
NEW_COMMANDER = 97  # Thomas W. Sweeny, verified unused Union officer in source.
LABEL = "ADC Test 11 - New Infantry Half Stock"


def read(path: Path) -> tuple[list[str], bytes, str]:
    raw = path.read_bytes()
    bom = b"\xef\xbb\xbf" if raw.startswith(b"\xef\xbb\xbf") else b""
    body = raw[len(bom):].decode("utf-8")
    separator = "\r\n" if "\r\n" in body else "\n"
    if "\n" in body.replace(separator, ""):
        raise ValueError(f"Mixed line endings in {path.name}")
    return body.splitlines(), bom, separator


def write(path: Path, lines: list[str], bom: bytes, separator: str) -> None:
    path.write_bytes(bom + (separator.join(lines) + separator).encode("utf-8"))


def records(lines: list[str], width: int, name: str) -> list[list[str]]:
    count = int(lines[0])
    if count < 0 or len(lines) != 1 + width * count:
        raise ValueError(f"Invalid {name} count or width")
    rows = [lines[1 + width * i:1 + width * (i + 1)] for i in range(count)]
    if [int(row[0]) for row in rows] != list(range(count)):
        raise ValueError(f"{name} IDs are not contiguous ordinals")
    return rows


def path_records(lines: list[str]) -> list[tuple[int, int, int | None]]:
    cursor = 1
    result: list[tuple[int, int, int | None]] = []

    def take() -> str:
        nonlocal cursor
        if cursor >= len(lines):
            raise ValueError("paths.dat ends inside a counted record")
        value = lines[cursor]
        cursor += 1
        return value

    def skip(count: int) -> None:
        nonlocal cursor
        if count < 0 or cursor + count > len(lines):
            raise ValueError("paths.dat variable section is out of bounds")
        cursor += count

    count = int(lines[0])
    for _ in range(count):
        start = cursor
        take(); take(); int(take()); int(take())
        skip(int(take()))  # cover history
        skip(1)  # rotation
        skip(4 * int(take()))  # movement paths
        skip(7 + 1 + 6 + 2 + 5 + 4)  # movement/battle, transfer/positions, campaign state
        skip(int(take()))  # active order types
        skip(4 + 1)  # advised source and order state
        for _ in range(int(take())):
            skip(4 + 7)
            skip(4 * int(take()))
            skip(4)
            skip(16 * int(take()))
        skip(4)  # upkeep, recruitment and retreat
        skip(int(take()))  # ammunition
        consumption = int(take())
        skip(3 * consumption)
        stock = None
        if consumption > 0:
            skip(5)
            stock = cursor
            for _ in range(4):
                if not math.isfinite(float(take())):
                    raise ValueError("Nonfinite path stock")
        skip(7 + 5 + 3 + 6 + 3 + 7)
        result.append((start, cursor, stock))
    if cursor != len(lines):
        raise ValueError("paths.dat has trailing or unconsumed lines")
    return result


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> None:
    if TARGET.exists():
        raise FileExistsError(f"Refusing to replace {TARGET}")
    if (SOURCE / "version.dat").read_text(encoding="utf-8-sig").strip() != "1.142":
        raise ValueError("Only the observed 1.142 format is supported by this probe")
    group_lines, _, _ = read(SOURCE / "groups.dat")
    groups = records(group_lines, 32, "groups.dat")
    unit_lines, unit_bom, unit_sep = read(SOURCE / "regiments.dat")
    units = records(unit_lines, 39, "regiments.dat")
    source = [row for row in units if row[1:3] == [OLD_NAME, OLD_NAME] and row[4:6] == ["0", str(OLD_COMMANDER)]]
    if len(source) != 1 or source[0][6] != "1000" or source[0][13] != "14" or source[0][29] != "17":
        raise ValueError("Unique validated game-created infantry source is missing")
    original = source[0]
    parent = [row for row in groups if row[0] == original[3]]
    if len(parent) != 1 or parent[0][1] != "1-1 INFBDE" or parent[0][4] != "2" or parent[0][17] != "14":
        raise ValueError("Tested infantry parent no longer has its saved identity")
    if any(row[1] == NEW_NAME for row in units):
        raise ValueError("New name already exists")
    if any(row[4] == str(NEW_COMMANDER) for row in groups) or any(row[5] == str(NEW_COMMANDER) for row in units):
        raise ValueError("New commander is assigned elsewhere")
    officer_lines, _, _ = read(SOURCE / "commanders.txt")
    officers = records(officer_lines, 66, "commanders.txt")
    officer = [row for row in officers if row[0] == str(NEW_COMMANDER)]
    if len(officer) != 1 or officer[0][3] != "Thomas W. Sweeny" or officer[0][4] != "0":
        raise ValueError("New commander is not a uniquely verified Union officer")

    path_lines, path_bom, path_sep = read(SOURCE / "paths.dat")
    spans = path_records(path_lines)
    identity = (OLD_NAME, OLD_NAME, "0", str(OLD_COMMANDER))
    matches = [(start, end, stock) for start, end, stock in spans if tuple(path_lines[start:start + 4]) == identity]
    if len(matches) != 1 or matches[0][2] is None:
        raise ValueError("Source infantry has no unique four-field path identity and stock")
    start, end, stock = matches[0]
    if end - start != 250 or stock is None or path_lines[stock - 5:stock] != ["1", "0", "0", "1", "0"]:
        raise ValueError("Source path is not the observed game-created no-order 250-line structure")
    if path_lines[start + 32] != "150" or path_lines[start + 4] != "0" or path_lines[start + 6] != "0":
        raise ValueError("Source path's variable sections changed")

    new_id = len(units)
    new_unit = original.copy()
    new_unit[0:3] = [str(new_id), NEW_NAME, NEW_NAME]
    new_unit[5] = str(NEW_COMMANDER)
    new_unit[31] = "Raised on July 11, 1861 for ADC half-stock creation test"
    new_path = path_lines[start:end].copy()
    new_path[0:4] = [NEW_NAME, NEW_NAME, "0", str(NEW_COMMANDER)]
    relative_stock = stock - start
    new_path[relative_stock:relative_stock + 4] = ["500", "1000", "500", "1000"]
    if tuple(new_path[:4]) == identity or len(new_path) != end - start:
        raise ValueError("New path identity or width is invalid")

    unit_lines[0] = str(new_id + 1)
    unit_lines.extend(new_unit)
    path_lines[0] = str(len(spans) + 1)
    path_lines.extend(new_path)
    scenario_lines, scenario_bom, scenario_sep = read(SOURCE / "scenario.dat")
    if scenario_lines[2:5] != ["11", "7", "1861"] or len(scenario_lines) < 26:
        raise ValueError("Unexpected campaign date")
    scenario_lines[24] = LABEL

    shutil.copytree(SOURCE, TARGET)
    write(TARGET / "regiments.dat", unit_lines, unit_bom, unit_sep)
    write(TARGET / "paths.dat", path_lines, path_bom, path_sep)
    write(TARGET / "scenario.dat", scenario_lines, scenario_bom, scenario_sep)
    changed = sorted(path.name for path in TARGET.iterdir() if path.is_file() and sha(path) != sha(SOURCE / path.name))
    if changed != ["paths.dat", "regiments.dat", "scenario.dat"]:
        raise ValueError(f"Unexpected changed files: {changed}")
    check_units, _, _ = read(TARGET / "regiments.dat")
    check_paths, _, _ = read(TARGET / "paths.dat")
    if records(check_units, 39, "regiments.dat")[-1] != new_unit or path_records(check_paths)[-1][1] != len(check_paths):
        raise ValueError("New records failed full reparse")
    print(json.dumps({"test_copy": str(TARGET), "label": LABEL, "version": "1.142",
                      "new_unit_id": new_id, "parent_id": original[3], "commander_id": NEW_COMMANDER,
                      "stock": new_path[relative_stock:relative_stock + 4],
                      "changed_files": changed,
                      "status": "Awaiting paused in-game load and manual resave"}, indent=2))


if __name__ == "__main__":
    main()
