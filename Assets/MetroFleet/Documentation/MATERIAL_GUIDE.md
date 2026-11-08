# Material Guide

Vehicle and plaza materials use `Universal Render Pipeline/Lit`. The showcase's lightweight TextMesh signage uses a Unity-bundled font and its font material, which was visually checked in the current URP project.

Materials live in `Assets/MetroFleet/Materials/`. Batch1–Batch4 are stable vehicle production groupings; Showcase contains the original plaza/sign-surface palette. Different vehicles can share compatible tire, trim, glass, metal and light materials. Visually different paint/light materials are intentionally retained.

## Change a body color safely

1. Identify the body-paint material on the intended renderer.
2. Duplicate that material into your own project folder.
3. Change its URP Base Color.
4. Assign the duplicate to matching body renderers at LOD0, LOD1 and LOD2 on your instance or prefab variant.
5. Check other body panels and all LOD levels.

Editing a shared material directly changes every vehicle using it. Duplicate first when the change should affect only one vehicle.

Glass is stylized rather than a promise of physically transparent interiors. Tire, metal, trim and red/blue/amber light surfaces preserve their existing identity. Colored light meshes do not imply flashing effects, sirens or gameplay illumination.

Vehicle appearance is predominantly material-color based. No external texture set, UV repaint workflow or texture-customization feature is promised. Do not switch shaders indiscriminately, delete material assets, or break references to embedded FBX meshes/materials. Use Unity's asset moves so GUID references remain intact.
