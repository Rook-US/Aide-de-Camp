"""Read-only probe of the installed Unity campaign scene's town components."""

import collections
import json
import sys
import struct
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "artifacts" / "unitypy-temp"))
import UnityPy  # noqa: E402
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator  # noqa: E402


def main() -> None:
    scene = Path(sys.argv[1])
    env = UnityPy.load(str(scene))
    objects = list(env.objects)
    print("objects", len(objects))
    print("types", json.dumps(collections.Counter(obj.type.name for obj in objects).most_common(30)))
    samples = 0
    town_objects = []
    frontline_objects = []
    errors = collections.Counter()
    for obj in objects:
        if obj.type.name != "MonoBehaviour":
            continue
        try:
            data = obj.read(check_read=False)
            script = getattr(data, "m_Script", None)
            script_obj = script.read() if script else None
            script_name = getattr(script_obj, "m_Name", None)
            if samples < 6:
                print("sample", obj.path_id, type(data).__name__, list(getattr(data, "__dict__", {}).keys())[:12], script_name)
                samples += 1
            if script_name == "Town":
                town_objects.append(obj)
            if script_name == "Frontline2":
                frontline_objects.append(obj)
        except Exception as exc:
            errors[(type(exc).__name__, str(exc)[:100])] += 1
    print("errors", errors.most_common(5))
    print("town_components", len(town_objects))
    print("frontline_components", len(frontline_objects))
    generator = TypeTreeGenerator(objects[0].assets_file.unity_version)
    generator.load_local_dll_folder(sys.argv[2])
    env.typetree_generator = generator
    towns = []
    for obj in town_objects:
        try:
            towns.append((obj.path_id, obj.read_typetree(check_read=True)))
        except Exception as exc:
            print("town_error", obj.path_id, type(exc).__name__, str(exc)[:180])
    print("town_decoded", len(towns))
    print("town_states", collections.Counter(tree.get("state") for _, tree in towns))
    for path_id, tree in towns[:5]:
        print("town", path_id, tree.get("CityName"), tree.get("state"), tree.get("m_GameObject"))
    first_go = next(o for o in objects if o.path_id == towns[0][1]["m_GameObject"]["m_PathID"])
    go = first_go.read_typetree()
    print("first_gameobject", go)
    for item in go.get("m_Component", []):
        ptr = item.get("component", item)
        target = next((o for o in objects if o.path_id == ptr.get("m_PathID")), None)
        if target and target.type.name == "Transform":
            print("first_transform", target.read_typetree())
            current = target
            for _ in range(6):
                tree = current.read_typetree()
                father = tree["m_Father"]["m_PathID"]
                if father == 0:
                    break
                current = next(o for o in objects if o.path_id == father)
                print("ancestor_transform", current.path_id, current.read_typetree().get("m_LocalPosition"), current.read_typetree().get("m_LocalRotation"), current.read_typetree().get("m_LocalScale"))
    for obj in frontline_objects:
        try:
            tree = obj.read_typetree(check_read=False)
            print("frontline", obj.path_id, list(tree))
            for key, value in tree.items():
                if "Texture" in key or "Date" in key:
                    print("frontline_field", key, value)
        except Exception as exc:
            print("frontline_error", obj.path_id, type(exc).__name__, str(exc), "bytes", obj.byte_size)
            print("frontline_nodes", [str(n) for n in generator.get_nodes("Assembly-CSharp", "Frontline2")[:100]])
            print("frontline_prefix", obj.get_raw_data()[:128].hex(" "))
            raw = obj.get_raw_data()
            print("frontline_ints", [(i, struct.unpack_from("<i", raw, i)[0]) for i in range(192, min(len(raw), 320), 4)])
            count = struct.unpack_from("<i", raw, 208)[0]
            print("texture_refs", [struct.unpack_from("<iq", raw, 212 + 12 * i) for i in range(count)])
            offset = 212 + 12 * count
            date_count = struct.unpack_from("<i", raw, offset)[0]
            offset += 4
            dates = []
            for _ in range(date_count):
                length = struct.unpack_from("<i", raw, offset)[0]
                offset += 4
                dates.append(raw[offset : offset + length].decode())
                offset = (offset + length + 3) & ~3
            print("texture_dates", dates)
            print("externals", [str(e) for e in obj.assets_file.externals])


if __name__ == "__main__":
    main()
