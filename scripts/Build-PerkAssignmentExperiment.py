"""Prepare a disposable 1.142 game-side infantry perk-choice experiment.

Changes one existing blank perk slot's earned experience from 0 to 1 in a
copied save, meeting the installed game's Regiment.ChoosePerk precondition.
The game/player performs the actual perk selection. No production ADC edit.
"""

from pathlib import Path
import hashlib
import shutil

ROOT = Path(r"G:\SteamLibrary\steamapps\common\Grand Tactician The Civil War (1861-1865)\Campaigns\001\G")
SOURCE = ROOT / "Save7_10_2026_22_35_17"
TARGET = ROOT / "ADC-TEST-4-INFANTRY-PERK-CHOICE"
NAME = "ADC Test 1-1 Infantry"
COMMANDER = "91"
LABEL = "ADC Test 4 - Choose Zouave I"


def read(path):
    raw = path.read_bytes()
    bom = b"\xef\xbb\xbf" if raw.startswith(b"\xef\xbb\xbf") else b""
    body = raw[len(bom):].decode("utf-8")
    separator = "\r\n" if "\r\n" in body else "\n"
    return body.splitlines(), bom, separator


def write(path, lines, bom, separator):
    path.write_bytes(bom + (separator.join(lines) + separator).encode("utf-8"))


def digest(path):
    return hashlib.sha256(path.read_bytes()).digest()


def main():
    if TARGET.exists():
        raise FileExistsError(f"Refusing to overwrite {TARGET}")
    if (SOURCE / "version.dat").read_text().strip() != "1.142":
        raise ValueError("Source is not the tested save version")
    units, bom, separator = read(SOURCE / "regiments.dat")
    count = int(units[0])
    if len(units) != 1 + count * 39:
        raise ValueError("Malformed combat count")
    starts = [1 + 39 * i for i in range(count)
              if units[1 + 39 * i + 1] == NAME and
              units[1 + 39 * i + 4] == "0" and
              units[1 + 39 * i + 5] == COMMANDER]
    if len(starts) != 1:
        raise ValueError("Target infantry identity is missing or ambiguous")
    start = starts[0]
    if units[start + 17:start + 20] != ["-1", "0", "0"]:
        raise ValueError("Target perk slot is not blank and unearned")
    source_record = units[start:start + 39]
    units[start + 19] = "1"
    scenario, scenario_bom, scenario_separator = read(SOURCE / "scenario.dat")
    if len(scenario) < 26 or scenario[2:5] != ["11", "7", "1861"]:
        raise ValueError("Source game date/label layout is not the validated save")
    scenario[24] = LABEL

    shutil.copytree(SOURCE, TARGET)
    write(TARGET / "regiments.dat", units, bom, separator)
    write(TARGET / "scenario.dat", scenario, scenario_bom, scenario_separator)
    changed = sorted(path.name for path in TARGET.iterdir()
                     if digest(path) != digest(SOURCE / path.name))
    if changed != ["regiments.dat", "scenario.dat"]:
        raise ValueError(f"Unexpected changed files: {changed}")
    actual, _, _ = read(TARGET / "regiments.dat")
    original, _, _ = read(SOURCE / "regiments.dat")
    if len(actual) != len(original) or [i for i, (a, b) in enumerate(zip(original, actual)) if a != b] != [start + 19]:
        raise ValueError("More than the earned perk experience changed")
    print("test_save", TARGET)
    print("target", NAME, "saved_id", source_record[0], "unit_type", source_record[4],
          "commander", COMMANDER)
    print("perk_slot_before", source_record[17:20], "perk_slot_in_test", actual[start + 17:start + 20])
    print("changed_files", changed)
    print("in_game_action", "If the perk choice is offered, choose Zouave I and make a manual save.")


if __name__ == "__main__":
    main()
