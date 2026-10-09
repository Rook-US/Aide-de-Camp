"""Read-only comparison of a moved root HQ and its saved descendants."""

from pathlib import Path

ROOT = Path(r"G:\SteamLibrary\steamapps\common\Grand Tactician The Civil War (1861-1865)\Campaigns\001\G")
FOLDERS = [ROOT / "Save7_10_2026_22_35_17", ROOT / "ADC-TEST-3-I-CORPS-NEW-YORK"]


def records(path, width):
    lines = path.read_text(encoding="utf-8-sig").splitlines()
    count = int(lines[0])
    if len(lines) != 1 + count * width:
        raise ValueError(f"Malformed {path.name}")
    return [lines[1 + width * i:1 + width * (i + 1)] for i in range(count)]


def placements(path):
    lines = path.read_text(encoding="utf-8-sig").splitlines()
    count = int(lines[42])
    tail = 43 + 15 * count
    if len(lines) != tail + 1 + 2 * int(lines[tail]):
        raise ValueError("Malformed battledata.dat")
    result = {}
    for i in range(count):
        r = lines[43 + 15 * i:43 + 15 * (i + 1)]
        key = (r[13] == "True", int(r[0]))
        if key in result:
            raise ValueError("Duplicate deployment identity")
        result[key] = (float(r[2]), float(r[3]))
    return result


for folder in FOLDERS:
    groups = records(folder / "groups.dat", 32)
    units = records(folder / "regiments.dat", 39)
    deployments = placements(folder / "battledata.dat")
    roots = [g for g in groups if g[1] == "I. CORPS" and g[4] == "0"
             and g[2] == "-1" and g[3] == "0" and g[17] == "16"]
    if len(roots) != 1:
        raise ValueError("I. CORPS root identity is not unique")
    root_id = int(roots[0][0])
    descendant_ids = {root_id}
    while True:
        next_ids = descendant_ids | {int(g[0]) for g in groups if int(g[2]) in descendant_ids}
        if next_ids == descendant_ids:
            break
        descendant_ids = next_ids
    descendants = [g for g in groups if int(g[0]) in descendant_ids]
    attached_units = [u for u in units if int(u[3]) in descendant_ids]
    print("SAVE", folder.name, "root_id", root_id, "group_descendants", len(descendants) - 1,
          "attached_combat_units", len(attached_units),
          "combat_deployments", sum((False, int(u[0])) in deployments for u in attached_units))
    for g in descendants:
        position = deployments.get((True, int(g[0])))
        if position is not None:
            print(" DEPLOYED GROUP", g[0], "parent", g[2], g[1], "map_x_z", position)
