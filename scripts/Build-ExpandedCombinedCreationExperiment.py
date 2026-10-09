"""Supersede Test 13 with eight separately identifiable cases in one save."""

from importlib.util import module_from_spec, spec_from_file_location
from pathlib import Path


builder_path = Path(__file__).with_name("Build-CombinedCreationExperiment.py")
spec = spec_from_file_location("combined_creation_builder", builder_path)
if spec is None or spec.loader is None:
    raise RuntimeError("Cannot load the checked combined-test builder")
builder = module_from_spec(spec)
spec.loader.exec_module(builder)

builder.TARGET = builder.SOURCE.parent / "ADC-TEST-14-EXPANDED-CREATION"
builder.LABEL = "ADC Test 14 - Expanded Creation"
builder.CASES.extend([
    # Saved Cold Steel II on 1st US Cavalry is perk 9, level 1. Test level 0.
    ("new_cold_steel", "ADC Test Division Cavalry", 292, 104, 88, None, (9, 0, 0)),
    ("new_experience", "ADC Test 1-1 Infantry", 91, 102, 71, None, None),
    ("fort_cavalry_stock", "ADC Test Division Cavalry", 292, 105, 27,
     (300, 600, 300, 300), None),
])


if __name__ == "__main__":
    builder.main()
