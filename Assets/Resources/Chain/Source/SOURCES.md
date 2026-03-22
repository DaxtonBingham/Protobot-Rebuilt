# Chain Model Sources

These runtime chain meshes are derived from official VEX CAD files copied into this folder:

- `VEX-Chain-3p75.step` from `CHAIN-LINK.STEP` in
  `https://content.vexrobotics.com/cad/STEP/Vex-Sprocket-and-Chain-Kit-STEP-08282007.zip`
- `VEX-Chain-9p79.step` from `High Strength Chain Links (276-2252-001).stp` in
  `https://www.vexrobotics.com/cadmodels/archive/download/product_id/1315`

Pitch references (VEX Library):

- 3.75 mm chain: `276-2166`
- 6.35 mm chain: `228-4983`
- 9.79 mm chain: `276-2172`

`build_chain_models.py` converts those STEP files into:

- `Assets/Resources/Chain/Chain3p75Link.obj`
- `Assets/Resources/Chain/Chain6p35Link.obj`
- `Assets/Resources/Chain/Chain9p79Link.obj`

Notes:

- 6.35 mm output is generated from official VEX link geometry with pitch scaling.
- Meshes are decimated for runtime use (about 5.5k faces/link).
- Keeps the largest solid from each STEP file.
- Rotates to match runtime expectation (`+Z` along chain direction).
- Converts millimeters to inches (`1 / 25.4`).
- Centers mesh on origin for midpoint placement.
