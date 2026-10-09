"""Read-only, build-specific map from saved town positions to state-texture pixels.

Requires UnityPy in artifacts/unitypy-temp. Does not modify game or save files.
"""

import collections
import csv
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "artifacts" / "unitypy-temp"))
import UnityPy  # noqa: E402
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator  # noqa: E402


def saved_towns(path: Path):
    lines = path.read_text(encoding="utf-8-sig").splitlines()
    found = {}
    for i in range(len(lines) - 5):
        if lines[i + 4] not in ("True", "False"):
            continue
        try:
            xyz = tuple(float(lines[i + j]) for j in (1, 2, 3))
            int(lines[i + 5])
        except ValueError:
            continue
        found.setdefault(lines[i], []).append(xyz)
    return found


def rotate(q, v):
    """Apply a Unity quaternion to a child-position vector."""
    x, y, z, w = (q[key] for key in ("x", "y", "z", "w"))
    ux, uy, uz = x, y, z
    dot = ux * v[0] + uy * v[1] + uz * v[2]
    cross = (uy * v[2] - uz * v[1], uz * v[0] - ux * v[2], ux * v[1] - uy * v[0])
    uu = ux * ux + uy * uy + uz * uz
    return tuple(2 * dot * u + (w * w - uu) * a + 2 * w * c
                 for u, a, c in zip((ux, uy, uz), v, cross))


def main():
    scene_path, dll_path, resources_path, towns_path, output_path = map(Path, sys.argv[1:6])
    scene = UnityPy.load(str(scene_path))
    objects = list(scene.objects)
    by_id = {obj.path_id: obj for obj in objects}
    components = []
    for obj in objects:
        if obj.type.name != "MonoBehaviour":
            continue
        data = obj.read(check_read=False)
        script = getattr(data, "m_Script", None)
        try:
            if script and getattr(script.read(), "m_Name", None) == "Town":
                components.append(obj)
        except (FileNotFoundError, ValueError):
            continue
    if len(components) != 103:
        raise ValueError(f"Expected 103 installed-scene towns, found {len(components)}")
    generator = TypeTreeGenerator(objects[0].assets_file.unity_version)
    generator.load_local_dll_folder(str(dll_path))
    scene.typetree_generator = generator

    resources = UnityPy.load(str(resources_path))
    texture = next(obj for obj in resources.objects if obj.path_id == 4771)
    tex = texture.read()
    if tex.m_Name != "STATES_1861_lakes" or (tex.m_Width, tex.m_Height) != (2048, 2048):
        raise ValueError("Installed 1861 state texture does not match inspected build")
    pixels = tex.image
    saved = saved_towns(towns_path)
    rows = []
    for obj in components:
        town = obj.read_typetree(check_read=True)
        name = town["CityName"]
        go = by_id[town["m_GameObject"]["m_PathID"]].read_typetree()
        transform = next(by_id[item["component"]["m_PathID"]] for item in go["m_Component"]
                         if by_id[item["component"]["m_PathID"]].type.name == "Transform")
        data = transform.read_typetree()
        pos = [data["m_LocalPosition"][axis] for axis in ("x", "y", "z")]
        parent = data["m_Father"]["m_PathID"]
        transform = by_id[parent] if parent else None
        while transform:
            data = transform.read_typetree()
            scale = data["m_LocalScale"]
            pos = list(rotate(data["m_LocalRotation"], [pos[i] * scale[axis]
                                                        for i, axis in enumerate(("x", "y", "z"))]))
            local = data["m_LocalPosition"]
            for i, axis in enumerate(("x", "y", "z")):
                pos[i] += local[axis]
            parent = data["m_Father"]["m_PathID"]
            transform = by_id[parent] if parent else None
        candidates = [xyz for xyz in saved.get(name, []) if max(abs(xyz[i] - pos[i]) for i in range(3)) < 0.02]
        if len(candidates) != 1:
            raise ValueError(f"{name} has {len(candidates)} matching saved name-and-position identities")
        x, y, z = candidates[0]
        px, py = int(x), int(-z)
        if not (0 <= px < 2048 and 0 <= py < 2048):
            raise ValueError(f"{name} outside texture")
        r, g, b = pixels.getpixel((px, py))[:3]
        state = b if (r or g or b) and 0 <= b < 53 else None
        rows.append((name, x, y, z, px, py, r, g, b, state))
    if len(rows) != 103 or len({(r[0], r[1], r[2], r[3]) for r in rows}) != 103:
        raise ValueError("Town identity/count mismatch")
    print("state_counts", json.dumps(collections.Counter(row[-1] for row in rows), sort_keys=True))
    with output_path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.writer(stream)
        writer.writerow(("town", "world_x", "world_y", "world_z", "pixel_x", "pixel_y", "r", "g", "b", "texture_state_id"))
        writer.writerows(sorted(rows))
    print("wrote", output_path, "towns", len(rows))


if __name__ == "__main__":
    main()
