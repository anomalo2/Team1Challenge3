# MetroFleet — Low-Poly Modular City Vehicles

Version 1.0.0. 20 stylized low-poly vehicle assets for Unity URP projects.

## Contents

- Civilian: 6 vehicles.
- Commercial: 8 vehicles.
- Public Transport: 2 vehicles.
- Emergency: 3 vehicles.
- Industrial: 1 vehicle.

Unity-ready prefabs provide three LOD levels, practical primitive colliders, separate useful wheels/components, Unity +Z forward and standardized root transforms. The optimized low-poly art direction is stylized; performance depends on your project and has not been benchmarked.

## Start here

1. Read [INSTALLATION.md](INSTALLATION.md).
2. Open `Assets/MetroFleet/Demo/MetroFleet_Showcase.unity`.
3. Find vehicles in `Assets/MetroFleet/Prefabs/<Category>/`.
4. See [VEHICLE_CATALOG.md](VEHICLE_CATALOG.md) for the complete fleet.

The showcase contains all 20 linked prefabs, original lightweight plaza surfaces, five presentation zones, two directional lights, category plaques and named inspection cameras. The separate `MetroFleet_AllVehicles_QA.unity` scene retains a simpler technical layout.

## This is an asset pack

It does **not** include complete driving physics, vehicle controllers, traffic AI, siren/flashing-light gameplay, full interiors unless actually present, or complete animation/controller systems. Existing pivots and separate components are geometry/hierarchy, not automatic behavior.

## Guides

[Prefab structure](PREFAB_GUIDE.md) · [LOD settings](LOD_GUIDE.md) · [Materials](MATERIAL_GUIDE.md) · [Customization](CUSTOMIZATION_GUIDE.md) · [Limitations](KNOWN_LIMITATIONS.md) · [Changelog](CHANGELOG.md) · [License/provenance notes](LICENSE_NOTES.md)

[FLEET_TECHNICAL_INVENTORY.md](FLEET_TECHNICAL_INVENTORY.md) provides verified dimensions, renderer/material counts, collider counts and exact prefab paths.

Tested in Unity 6000.6.0f1 with URP 17.6.0. Older Unity/URP versions, Built-in and HDRP compatibility are not claimed.
