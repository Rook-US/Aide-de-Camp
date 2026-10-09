"""Probe whether the tested 1.142 root-command placement rule also holds at Philadelphia.

This reuses Test 10's fully checked builder with a second verified playable
town. It writes only a disposable game-test copy and never replaces one.
"""

from importlib.util import module_from_spec, spec_from_file_location
from pathlib import Path


builder_path = Path(__file__).with_name("Build-NewYorkRootCommandExperiment.py")
spec = spec_from_file_location("new_root_command_builder", builder_path)
if spec is None or spec.loader is None:
    raise RuntimeError("Cannot load the checked root-command experiment builder")
builder = module_from_spec(spec)
spec.loader.exec_module(builder)

builder.TARGET = builder.CAMPAIGN / "ADC-TEST-12-PHILADELPHIA-ROOT-HQ"
builder.NAME = "ADC Test Philadelphia HQ"
builder.LABEL = "ADC Test 12 - Philadelphia Root HQ"
builder.TOWN_NAME = "Philadelphia"
builder.TOWN_STATE_ID = "31"
builder.EXPECTED_WORLD = (1711.0, 491.5283, -585.0)


if __name__ == "__main__":
    builder.main()
