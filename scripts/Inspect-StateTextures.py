"""Read-only inspection of the scene's dated state textures in resources.assets."""

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "artifacts" / "unitypy-temp"))
import UnityPy  # noqa: E402


def main() -> None:
    path = Path(sys.argv[1])
    env = UnityPy.load(str(path))
    wanted = {6849, 1937, 4277, 4771, 1266}
    objects = [obj for obj in env.objects if obj.path_id in wanted]
    print("matching_objects", len(objects))
    for obj in objects:
        data = obj.read()
        print("texture", obj.path_id, obj.type.name, data.m_Name, data.m_Width, data.m_Height, data.m_TextureFormat)
        if obj.path_id == 4771:
            img = data.image
            for x, y in ((1777, 475), (1709, 860), (1000, 1000)):
                print("pixel", x, y, img.getpixel((x, y)), img.getpixel((x, 2047-y)))


if __name__ == "__main__":
    main()
