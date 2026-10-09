"""Prepare a disposable 1.142 I. CORPS command-perk choice test."""

from pathlib import Path
import hashlib
import shutil

ROOT = Path(r"G:\SteamLibrary\steamapps\common\Grand Tactician The Civil War (1861-1865)\Campaigns\001\G")
SOURCE = ROOT / "Save7_10_2026_22_35_17"
TARGET = ROOT / "ADC-TEST-7-I-CORPS-PERK-CHOICE"


def read(path):
    return path.read_text(encoding="utf-8-sig").splitlines()


def write(path, values):
    raw = path.read_bytes()
    bom = b"\xef\xbb\xbf" if raw.startswith(b"\xef\xbb\xbf") else b""
    body = raw[len(bom):].decode("utf-8")
    separator = "\r\n" if "\r\n" in body else "\n"
    path.write_bytes(bom + (separator.join(values) + separator).encode("utf-8"))


if TARGET.exists():
    raise FileExistsError(f"Refusing to overwrite {TARGET}")
if (SOURCE / "version.dat").read_text().strip() != "1.142":
    raise ValueError("Unsupported source version")
groups = read(SOURCE / "groups.dat")
count = int(groups[0])
if len(groups) != 1 + 32 * count:
    raise ValueError("Malformed command count")
matches = [1 + 32 * i for i in range(count)
           if groups[1 + 32 * i + 1] == "I. CORPS"
           and groups[1 + 32 * i + 2] == "-1"
           and groups[1 + 32 * i + 3] == "0"
           and groups[1 + 32 * i + 4] == "0"
           and groups[1 + 32 * i + 17] == "16"]
if len(matches) != 1:
    raise ValueError("Independent I. CORPS is missing or ambiguous")
start = matches[0]
if groups[start + 20:start + 23] != ["-1", "0", "0.5873892"]:
    raise ValueError("First command perk slot is not the inspected empty slot")
groups[start + 22] = "1"
scenario = read(SOURCE / "scenario.dat")
if scenario[2:5] != ["11", "7", "1861"]:
    raise ValueError("Unexpected scenario date")
scenario[24] = "ADC Test 7 - I Corps Perk Choice"

shutil.copytree(SOURCE, TARGET)
write(TARGET / "groups.dat", groups)
write(TARGET / "scenario.dat", scenario)
changed = sorted(p.name for p in TARGET.iterdir() if
                 hashlib.sha256(p.read_bytes()).digest() !=
                 hashlib.sha256((SOURCE / p.name).read_bytes()).digest())
if changed != ["groups.dat", "scenario.dat"]:
    raise ValueError(f"Unexpected changed files: {changed}")
old, new = read(SOURCE / "groups.dat"), read(TARGET / "groups.dat")
diff = [i for i, (a, b) in enumerate(zip(old, new)) if a != b]
if len(new) != len(old) or diff != [start + 22]:
    raise ValueError(f"Unexpected group diff: {diff}")
print("test_save", TARGET)
print("command", groups[start + 1], "saved_id", groups[start], "native_tier", groups[start + 17])
print("first_slot_before", old[start + 20:start + 23], "in_test", new[start + 20:start + 23])
print("changed_files", changed)
