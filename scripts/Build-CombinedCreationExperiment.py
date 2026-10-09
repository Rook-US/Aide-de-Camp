"""Build one disposable 1.142 save for five distinct creation checks.

Each case has a unique four-field runtime identity. This is an experiment,
not ADC's production writer. It never replaces a source or existing test.
"""

from __future__ import annotations

import json
import runpy
import shutil
from pathlib import Path


helpers = runpy.run_path(str(Path(__file__).with_name("Build-NewUnitHalfStockExperiment.py")))
read = helpers["read"]
write = helpers["write"]
records = helpers["records"]
path_records = helpers["path_records"]
sha = helpers["sha"]
SOURCE = helpers["SOURCE"]
TARGET = SOURCE.parent / "ADC-TEST-13-COMBINED-CREATION"
LABEL = "ADC Test 13 - Combined Creation"

CASES = [
    # key, source unit, source officer, new officer, saved parent, stock, perk
    ("fort_infantry", "ADC Test 1-1 Infantry", 91, 92, 27, None, None),
    ("fort_cavalry", "ADC Test Division Cavalry", 292, 93, 27, None, None),
    ("cavalry_stock", "ADC Test Division Cavalry", 292, 98, 88, (300, 600, 300, 300), None),
    ("artillery_stock", "ADC Test 1st Artillery Battalion", 48, 100, 75, (60, 30, 30, 60), None),
    ("new_zouave", "ADC Test 1-1 Infantry", 91, 101, 71, None, (2, 0, 0)),
]


def main() -> None:
    if TARGET.exists():
        raise FileExistsError(f"Refusing to replace existing test: {TARGET}")
    if (SOURCE / "version.dat").read_text(encoding="utf-8-sig").strip() != "1.142":
        raise ValueError("Only the validated 1.142 save is used")
    group_lines, _, _ = read(SOURCE / "groups.dat")
    groups = records(group_lines, 32, "groups.dat")
    unit_lines, unit_bom, unit_sep = read(SOURCE / "regiments.dat")
    units = records(unit_lines, 39, "regiments.dat")
    path_lines, path_bom, path_sep = read(SOURCE / "paths.dat")
    spans = path_records(path_lines)
    officer_lines, _, _ = read(SOURCE / "commanders.txt")
    officers = records(officer_lines, 66, "commanders.txt")
    fort_lines, _, _ = read(SOURCE / "garrisonrefs.dat")
    if len(fort_lines) != 1 + 8 * int(fort_lines[0]):
        raise ValueError("Fort reference count is malformed")
    fort = [fort_lines[1 + 8 * i:1 + 8 * (i + 1)] for i in range(int(fort_lines[0]))
            if fort_lines[1 + 8 * i] == "Fort Monroe"]
    if len(fort) != 1 or fort[0] != ["Fort Monroe", "1709.999", "489.074", "-860.0001",
                                    "Ft. Monroe Garrison", "", "15", "1113"]:
        raise ValueError("The explicit Fort Monroe saved reference changed")
    parent_checks = {
        27: (fort[0][4], "-1", "0", fort[0][7], "14"),
        71: ("1-1 INFBDE", "88", "0", "2", "14"),
        75: ("Division Artillery 2", "88", "0", "46", "14"),
        88: ("1st Infantry Division", "70", "0", "1", "15"),
    }
    for parent_id, expected in parent_checks.items():
        row = groups[parent_id]
        if (row[1], row[2], row[3], row[4], row[17]) != expected:
            raise ValueError(f"Saved parent {parent_id} changed")
    used_officers = {int(row[4]) for row in groups} | {int(row[5]) for row in units}
    new_units: list[list[str]] = []
    new_paths: list[list[str]] = []
    manifest: list[dict] = []
    for key, source_name, source_officer, officer_id, parent_id, stock, perk in CASES:
        matches = [row for row in units if row[1] == source_name and row[5] == str(source_officer)]
        if len(matches) != 1 or matches[0][3] != {"fort_infantry": "71", "fort_cavalry": "88",
                                               "cavalry_stock": "88", "artillery_stock": "75",
                                               "new_zouave": "71", "new_cold_steel": "88",
                                               "new_experience": "71", "fort_cavalry_stock": "88"}[key]:
            raise ValueError(f"Unique game-written source for {key} changed")
        source = matches[0]
        if officer_id in used_officers or officers[officer_id][4] != "0" or officers[officer_id][65] != "True":
            raise ValueError(f"Officer {officer_id} is not an unused active Union officer")
        used_officers.add(officer_id)
        name = "ADC Batch " + key.replace("_", " ").title()
        if any(row[1] == name for row in units) or any(row[1] == name for row in new_units):
            raise ValueError(f"Duplicate new identity: {name}")
        new_id = len(units) + len(new_units)
        unit = source.copy()
        unit[0:4] = [str(new_id), name, name, str(parent_id)]
        unit[5] = str(officer_id)
        unit[31] = f"Raised on July 11, 1861 for ADC combined test: {key}"
        if parent_id == 27:
            unit[34:37] = fort[0][1:4]
        if key == "new_experience":
            unit[10] = "50"  # Test input: 50% experience on a new infantry record.
        if perk is not None:
            required_branch = {"new_zouave": "0", "new_cold_steel": "1"}.get(key)
            if unit[17:20] != ["-1", "0", "0"] or unit[4] != required_branch:
                raise ValueError(f"{key} source does not have an empty compatible perk slot")
            unit[17:20] = [str(value) for value in perk]
        if len(unit) != 39:
            raise ValueError("New combat record has incorrect width")
        new_units.append(unit)
        if stock is not None:
            identity = (source_name, source_name, source[4], str(source_officer))
            matches = [(start, end, stock_line) for start, end, stock_line in spans
                       if tuple(path_lines[start:start + 4]) == identity]
            if len(matches) != 1 or matches[0][2] is None:
                raise ValueError(f"Unique full path template for {key} is missing")
            start, end, stock_line = matches[0]
            if end - start != 250:
                raise ValueError(f"Unsupported nonstandard path structure for {key}")
            path = path_lines[start:end].copy()
            path[0:4] = [name, name, source[4], str(officer_id)]
            offset = stock_line - start
            path[offset:offset + 4] = [str(value) for value in stock]
            path[237] = unit[31]
            new_paths.append(path)
        manifest.append({"case": key, "name": name, "input_id": new_id,
                         "parent_id": parent_id, "officer_id": officer_id,
                         "branch": int(unit[4]), "weapon_id": int(unit[13]),
                         "strength": int(unit[6]), "home_state_id": int(unit[29]),
                         "stock": stock, "perk": perk,
                         "experience_percent": int(unit[10])})
    identities = [(row[1], row[2], row[4], row[5]) for row in units + new_units]
    if len(identities) != len(set(identities)):
        raise ValueError("Combined test contains a duplicate four-field unit identity")
    unit_lines[0] = str(len(units) + len(new_units))
    unit_lines.extend(field for row in new_units for field in row)
    path_lines[0] = str(len(spans) + len(new_paths))
    path_lines.extend(field for row in new_paths for field in row)
    scenario_lines, scenario_bom, scenario_sep = read(SOURCE / "scenario.dat")
    if scenario_lines[2:5] != ["11", "7", "1861"]:
        raise ValueError("Source campaign date changed")
    scenario_lines[24] = LABEL
    shutil.copytree(SOURCE, TARGET)
    write(TARGET / "regiments.dat", unit_lines, unit_bom, unit_sep)
    write(TARGET / "paths.dat", path_lines, path_bom, path_sep)
    write(TARGET / "scenario.dat", scenario_lines, scenario_bom, scenario_sep)
    changed = sorted(path.name for path in TARGET.iterdir() if path.is_file() and sha(path) != sha(SOURCE / path.name))
    if changed != ["paths.dat", "regiments.dat", "scenario.dat"]:
        raise ValueError(f"Unexpected changed files: {changed}")
    saved_units = records(read(TARGET / "regiments.dat")[0], 39, "regiments.dat")
    saved_paths = read(TARGET / "paths.dat")[0]
    if saved_units[-len(new_units):] != new_units or len(path_records(saved_paths)) != len(spans) + len(new_paths):
        raise ValueError("Combined test did not reparse")
    print(json.dumps({"test_copy": str(TARGET), "label": LABEL,
                      "cases": manifest, "changed_files": changed,
                      "status": "Awaiting one game load and one paused resave"}, indent=2))


if __name__ == "__main__":
    main()
