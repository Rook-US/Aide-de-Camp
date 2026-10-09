"""Prepare a disposable 1.142 save testing a directly saved Zouave I perk."""

from pathlib import Path
import hashlib
import shutil

ROOT = Path(r"G:\SteamLibrary\steamapps\common\Grand Tactician The Civil War (1861-1865)\Campaigns\001\G")
SOURCE = ROOT / "Save7_10_2026_22_35_17"
TARGET = ROOT / "ADC-TEST-5-DIRECT-ZOUAVE-I"


def lines(path):
    return path.read_text(encoding="utf-8-sig").splitlines()


def write_preserving_encoding(path, values):
    raw = path.read_bytes()
    bom = b"\xef\xbb\xbf" if raw.startswith(b"\xef\xbb\xbf") else b""
    body = raw[len(bom):].decode("utf-8")
    separator = "\r\n" if "\r\n" in body else "\n"
    path.write_bytes(bom + (separator.join(values) + separator).encode("utf-8"))


if TARGET.exists():
    raise FileExistsError(f"Refusing to overwrite {TARGET}")
if (SOURCE / "version.dat").read_text().strip() != "1.142":
    raise ValueError("Unsupported source version")

regiments = lines(SOURCE / "regiments.dat")
count = int(regiments[0])
if len(regiments) != 1 + 39 * count:
    raise ValueError("Malformed regiment count")
matches = [1 + 39 * i for i in range(count)
           if regiments[1 + 39 * i + 1] == "ADC Test 1-1 Infantry"
           and regiments[1 + 39 * i + 4] == "0"
           and regiments[1 + 39 * i + 5] == "91"]
if len(matches) != 1:
    raise ValueError("Infantry target is missing or ambiguous")
start = matches[0]
if regiments[start + 17:start + 20] != ["-1", "0", "0"]:
    raise ValueError("Original perk slot is not empty")
regiments[start + 17:start + 20] = ["2", "0", "0"]
scenario = lines(SOURCE / "scenario.dat")
if scenario[2:5] != ["11", "7", "1861"]:
    raise ValueError("Unexpected scenario date")
scenario[24] = "ADC Test 5 - Direct Zouave I"

shutil.copytree(SOURCE, TARGET)
write_preserving_encoding(TARGET / "regiments.dat", regiments)
write_preserving_encoding(TARGET / "scenario.dat", scenario)
changed = sorted(p.name for p in TARGET.iterdir() if
                 hashlib.sha256(p.read_bytes()).digest() !=
                 hashlib.sha256((SOURCE / p.name).read_bytes()).digest())
if changed != ["regiments.dat", "scenario.dat"]:
    raise ValueError(f"Unexpected changed files: {changed}")
original = lines(SOURCE / "regiments.dat")
actual = lines(TARGET / "regiments.dat")
diff = [i for i, (a, b) in enumerate(zip(original, actual)) if a != b]
if len(actual) != len(original) or diff != [start + 17]:
    raise ValueError(f"Unexpected regiment diff: {diff}")
print("test_save", TARGET)
print("target", regiments[start + 1], "saved_id", regiments[start])
print("perk_before", original[start + 17:start + 20], "perk_in_test", actual[start + 17:start + 20])
print("changed_files", changed)
