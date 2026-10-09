"""Build isolated 1.142 import experiments; never edits the installed campaign.

These are format probes, not ADC creation support. The first copy changes only
regiments.dat. The second adds the requested command and five further units.
The game must load and resave each copy before any rule is promoted to support.
"""

from __future__ import annotations

import json
import shutil
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT / "artifacts" / "creation-experiment-2026-10-07"
SOURCE = BASE / "source-save"


def read_records(folder: Path, name: str, width: int) -> tuple[list[list[str]], bytes, str]:
    raw = (folder / name).read_bytes()
    bom = b"\xef\xbb\xbf" if raw.startswith(b"\xef\xbb\xbf") else b""
    body = raw[len(bom) :].decode("utf-8")
    newline = "\r\n" if "\r\n" in body else "\n"
    lines = body.splitlines()
    count = int(lines[0])
    if len(lines) != 1 + count * width:
        raise ValueError(f"{name}: record count does not consume the whole file")
    return [lines[1 + i * width : 1 + (i + 1) * width] for i in range(count)], bom, newline


def write_records(folder: Path, name: str, records: list[list[str]], bom: bytes, newline: str) -> None:
    width = {"groups.dat": 32, "regiments.dat": 39}[name]
    if any(len(record) != width for record in records):
        raise ValueError(f"{name}: wrong record width")
    lines = [str(len(records))] + [field for record in records for field in record]
    (folder / name).write_bytes(bom + (newline.join(lines) + newline).encode("utf-8"))


def set_save_label(folder: Path, label: str) -> None:
    path = folder / "scenario.dat"
    raw = path.read_bytes()
    bom = b"\xef\xbb\xbf" if raw.startswith(b"\xef\xbb\xbf") else b""
    body = raw[len(bom) :].decode("utf-8")
    newline = "\r\n" if "\r\n" in body else "\n"
    lines = body.splitlines()
    if len(lines) < 26:
        raise ValueError("scenario.dat has no saved display-name field")
    lines[24] = label
    path.write_bytes(bom + (newline.join(lines) + newline).encode("utf-8"))


def clone_record(records: list[list[str]], record_id: int) -> list[str]:
    matched = [r for r in records if int(r[0]) == record_id]
    if len(matched) != 1:
        raise ValueError(f"expected one source record with saved ID {record_id}")
    return matched[0].copy()


def unit(
    source: list[list[str]], source_id: int, new_id: int, name: str, parent: int,
    kind: int, commander: int, strength: int, weapon: int, home_state: int,
    location: tuple[str, str, str] = ("0", "0", "0"),
) -> list[str]:
    # Every copied source field is overwritten below. Source records supply the
    # installed build's known Union uniform colors only (offsets 14-16).
    template = clone_record(source, source_id)
    coat, trousers, variation = template[14:17]
    result = [
        str(new_id), name, name, str(parent), str(kind), str(commander), str(strength),
        "0", "0", "0", "0", "0", "True", str(weapon),
        coat, trousers, variation, "-1", "0", "0", "-1", "-1", "0", "False",
        "11" if kind == 2 else "13", "0", "11.07.1861", "12", "0",
        str(home_state), "0", "Raised on July 11, 1861 for ADC creation test",
        "0", "0", *location, "0", "1",
    ]
    if len(result) != 39:
        raise ValueError("new regiment is not 39 fields")
    return result


def artillery_command(source: list[list[str]]) -> list[str]:
    template = clone_record(source, 284)
    coat, trousers = template[15:17]
    result = [
        "287", "Division Artillery 2", "87", "0", "46", "100",
        "100", "100", "100", "100", "100", "100", "100", "-1", "0",
        coat, trousers, "14", "0", "0",
        "-1", "0", "0", "-1", "0", "0", "-1", "0", "0", "-1", "0", "0",
    ]
    if len(result) != 32:
        raise ValueError("new command is not 32 fields")
    return result


def build(label: str, include_all: bool) -> dict:
    if not SOURCE.is_dir() or (SOURCE / "version.dat").read_text().strip() != "1.142":
        raise ValueError("source copy must be a complete 1.142 save")
    target = BASE / label
    if target.exists():
        raise FileExistsError(f"refusing to replace existing experiment: {target}")
    shutil.copytree(SOURCE, target)
    set_save_label(target, "ADC Test 2 - All Requested Units" if include_all else "ADC Test 1 - Fort Monroe Artillery")
    groups, group_bom, group_newline = read_records(target, "groups.dat", 32)
    regiments, unit_bom, unit_newline = read_records(target, "regiments.dat", 39)
    if len(groups) != 287 or len(regiments) != 520:
        raise ValueError("source save has changed; fixed experiment IDs are invalid")
    if [r[1] for r in groups if r[0] in {"27", "71", "87"}] != [
        "Ft. Monroe Garrison", "1-1 INFBDE", "1st Infantry Division"
    ]:
        raise ValueError("requested parents do not match saved identities")
    additions = [
        unit(regiments, 460, 520, "ADC Test Fort Monroe Artillery", 27, 2, 38,
             60, 41, 17, ("1709.999", "489.074", "-860.0001")),
    ]
    if include_all:
        groups.append(artillery_command(groups))
        additions.extend([
            unit(regiments, 145, 521, "ADC Test 1-1 Infantry", 71, 0, 91, 1000, 14, 17),
            unit(regiments, 157, 522, "ADC Test Division Cavalry", 87, 1, 292, 600, 25, 31),
            unit(regiments, 513, 523, "ADC Test 1st Artillery Battalion", 287, 2, 48, 60, 4, 31),
            unit(regiments, 513, 524, "ADC Test 2nd Artillery Battalion", 287, 2, 49, 60, 4, 31),
            unit(regiments, 513, 525, "ADC Test 3rd Artillery Battalion", 287, 2, 50, 60, 4, 31),
        ])
    regiments.extend(additions)
    ids = {int(group[0]) for group in groups}
    if len(ids) != len(groups) or any(int(unit_record[3]) not in ids for unit_record in regiments):
        raise ValueError("group IDs or parent references are invalid")
    if sorted(int(r[0]) for r in regiments) != list(range(len(regiments))):
        raise ValueError("regiment IDs are not unique contiguous list ordinals")
    if sorted(ids) != list(range(len(groups))):
        raise ValueError("group IDs are not unique contiguous list ordinals")
    identities = [(r[1], r[2], int(r[4]), int(r[5])) for r in regiments]
    if len(identities) != len(set(identities)):
        raise ValueError("a new regiment duplicates a four-field runtime identity")
    write_records(target, "groups.dat", groups, group_bom, group_newline)
    write_records(target, "regiments.dat", regiments, unit_bom, unit_newline)
    return {
        "folder": str(target), "save_version": "1.142", "experimental": True,
        "group_count": len(groups), "regiment_count": len(regiments),
        "new_group": {"id": 287, "name": "Division Artillery 2", "parent_id": 87} if include_all else None,
        "new_units": [
            {"id": int(r[0]), "name": r[1], "parent_id": int(r[3]),
             "type": int(r[4]), "commander_id": int(r[5]), "weapon_id": int(r[13]),
             "strength": int(r[6]), "home_state_id": int(r[29])}
            for r in additions
        ],
        "unchanged_companion_files": ["paths.dat", "armygrouprefs.dat", "battledata.dat", "garrisonrefs.dat"],
        "required_next_check": "Load and resave in game; compare game-written companion records and placement."
    }


if __name__ == "__main__":
    results = [build("stage-1-fort-artillery", False), build("stage-2-all-requested", True)]
    (BASE / "experiment-manifest.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
    print(json.dumps(results, indent=2))
