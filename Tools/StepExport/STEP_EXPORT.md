# STEP assembly export

Use **File > Export As... > STEP...** for the current assembly. Export does not modify the PBB. The writer runs in a separate process only while an export is active; it needs no Python, Fusion, FreeCAD, account or network connection on the user's machine.

The output is an AP214 STEP assembly in millimetres, with named component instances, placements, mirrored/scaled geometry and face colors. Repeated parts share definitions. Chain links and automatically displayed shaft inserts are separate components. Poly Maker outlines, Bezier curves, thickness, cutouts and round/elliptical/square holes become analytic CAD solids.

Stock geometry comes from the original CAD sources listed with pinned revisions and SHA-256 hashes in `Assets/StreamingAssets/StepExport/Library/sources.json`. Mesh triangles are used offline to register and compare those sources; they are **not** converted to thousands of planar STEP faces. Closed manufacturer shells are made solid; genuine sheet surfaces such as labels/screens are retained.

## Coverage and limits

The bundled library includes 205 registered stock and game templates, plus generated channel/angle lengths, shaft lengths, plates and Poly Maker parts. `Library/index.json` records the exact coverage, comparisons and exceptions.

All Override parts and both field layouts are supported using the original VEX CAD and the app's exact node placements. High Stakes rings/mobile goals, Push Back blocks/goals/loaders and the Spin Up disc also have CAD equivalents. The 12T high-strength gear, slide bracket, mecanum wheel, 2-inch V2 omni wheel, 2.25-inch screw, normal 10T/40T/48T sprockets and V1 2.75-inch traction wheel are supported.

Three stock variants remain unverified: the 2.75-inch V2 omni wheel and V1 4/5-inch traction wheels fail the exporter’s solid validation at microscopic fillet surfaces in the available CAD. The complete legacy High Stakes and Push Back field meshes also differ from the available CAD assemblies. These entries fail explicitly; export never drops them or replaces an existing file on failure. STL remains available for their displayed geometry.

Source-model details are explicit:

- Override pins use the original two-piece CAD, including their internal joints. Each color variant has an exact material map, including the single-color yellow pin.
- The original mecanum wheel, 2.25-inch screw, High Stakes ring and Spin Up disc preserve manufacturer dimensions. Their legacy display meshes differ slightly; per-part notes and measured deviations are recorded in the index. The screw is aligned at the underside of its head, retaining its nominal 2.250-inch shank.
- Field CAD retains original surface detail even where the app uses coarser display meshes. Broken zero-area seam faces in the original Override risers are removed before sewing their original analytic faces into valid solids. Foam tiles use the app's square-interior configuration.
- Protobot's 0.250-inch chain asset is a scaled version of the small link. Its CAD reference applies the same scale to the 0.148-inch source link; it is not a separate manufacturer SKU.

STEP supplies editable imported bodies and component structure. It does not recreate Fusion feature history, mechanical joints or a parametric timeline. Geometry should be checked before manufacturing. Full assemblies with detailed stock CAD can produce large files.

## STL mesh export

Use **File > Export As... > STL...** for a binary STL of the current assembly. Coordinates are in **millimeters**, with Z up, and preserve the assembled positions, rotations and scale. When an importing app asks for STL units, choose millimeters. STL itself has no standardized unit metadata.

STL captures the original part meshes, full-detail chain links, shaft inserts and Poly Maker geometry, independently of the camera, display detail and rendering batches. It does not require the CAD library, so a part missing a STEP template can still export to STL.

STL contains triangles only: no colors, component names, editable CAD surfaces or joints. Separate and intersecting parts remain assembled shells; this command does not merge an entire robot into a watertight printable solid or repair source meshes. Only zero-area triangles are discarded; invalid geometry fails the export rather than silently omitting a part.

`StlExportWriter.cs` streams the captured geometry to a binary file on a background task. It transforms normals and winding correctly for reflected and nonuniformly scaled parts. Cancellation, invalid geometry and write failures preserve any existing destination. The writer atomically publishes a complete sibling temporary file and removes unfinished temporary files. It does no work while idle, and does not create a large intermediate JSON snapshot. Editor verification is available under **Tools > Verify STL Export**.

## Implementation

- `StepExportSnapshot.cs` captures active document parts, full original geometry references, custom definitions, chain link transforms and shaft inserts on Unity's main thread. Camera visibility and batched-renderer flags do not change export contents.
- `StepExportController.cs` supplies native File-menu commands and a small progress/cancel/details strip. It starts the bundled hidden worker and only polls while an export is running.
- `step_export.py` resolves exact catalog recipes, creates the XCAF assembly and atomically publishes a sibling temporary STEP file. Missing CAD, invalid solids, writer failures and cancellation preserve an existing destination.
- `kernel.py` contains CAD operations without importing CadQuery, NumPy or VTK's Python modules. Templates are compressed on disk and loaded on demand.
- `build_library.py` and `catalog_recipes.py` perform offline registration and source-assembly configuration. Generated reports require review before adding a template to the release library. Registration alone is not an approval to ship a model.
- `axial_registration.py` checks rotational phase and reflection for symmetric wheels, gears and sprockets without stretching CAD. `reviewed_stock_registration.json` pins the added stock placements to source hashes. `extract_field_cad.py` and `build_override_library.py` reproduce the Override library from original field CAD and app catalog captures. `build_reviewed_game_library.py` uses `reviewed_game_registration.json` for legacy game definitions and included/excluded assembly components. Field rotations are orthogonalized within single-precision rounding tolerance before transforming analytic CAD; actual scale or shear requires review.

The complete `StreamingAssets/StepExport` directory must accompany the app. Its native libraries are ordinary StreamingAssets, not Unity plugins. License notices and source links are included under `Licenses`. The CAD runtime and library add approximately 537 MB on disk, with no CAD process or CAD library loaded into the app while idle.

## Developer commands

Use Windows x64 and Python 3.12 with `requirements-build.txt` in an isolated development environment.

```powershell
python Tools/StepExport/test_step_export.py
python Tools/StepExport/build_library.py <downloaded-sources> <captured-catalog-json> <registered-library>
python Tools/StepExport/build_runtime.py --output <outside-project-dist> --work <outside-project-build>
```

The runtime package is built with PyInstaller. Copy the `Protobot CAD` directory's contents and the reviewed compressed `Library` into `Assets/StreamingAssets/StepExport`, then build the Unity player. Do not package downloaded raw STEP sources, Python environments, registration captures or compiler caches.

For a source-library update, compare every configured part (including composite wheel adapters, extended pistons and automatically displayed inserts), verify valid BReps, units, reflected placements, component counts and material maps, and read back the resulting STEP assembly. Preserve the original PBB fixtures.
