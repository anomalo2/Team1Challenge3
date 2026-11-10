# MetroFleet — Technical Fleet Inventory

Audited 2026-10-04 in Unity 6000.6.0f1 with URP 17.6.0. Product: low-poly modular city vehicles; asset models, not driving/gameplay controllers.

## Final prefab inventory

Exactly 20 finished prefabs: Civilian 6, Commercial 8, PublicTransport 2, Emergency 3, Industrial 1. Dimensions are approximate combined renderer bounds, Unity X/Y/Z (width/height/length), in metres. Material counts are distinct materials used by each prefab; LOD columns count assigned renderers, not mesh polygons.

| Vehicle | Category | Prefab path | X / Y / Z (m) | Materials | LOD0 | LOD1 | LOD2 | Colliders | Functional hierarchy notes |
|---|---|---|---|---:|---:|---:|---:|---|---|
| MF_ElectricCityCar_01 | Civilian | `Assets/MetroFleet/Prefabs/Civilian/MF_ElectricCityCar_01.prefab` | 1.874 / 1.480 / 3.248 | 9 | 40 | 32 | 8 | 1 BoxCollider | Separate wheels and distinct electric-city-car materials preserved. |
| MF_Hatchback_01 | Civilian | `Assets/MetroFleet/Prefabs/Civilian/MF_Hatchback_01.prefab` | 2.001 / 1.620 / 3.300 | 8 | 51 | 34 | 7 | 1 BoxCollider | Separate wheels, body, glass, lights and trim preserved. |
| MF_Pickup_01 | Civilian | `Assets/MetroFleet/Prefabs/Civilian/MF_Pickup_01.prefab` | 2.258 / 1.910 / 4.665 | 8 | 48 | 40 | 10 | 1 BoxCollider | Separate wheels and pickup/cargo components preserved. |
| MF_Sedan_01 | Civilian | `Assets/MetroFleet/Prefabs/Civilian/MF_Sedan_01.prefab` | 2.190 / 1.495 / 4.550 | 16 | 97 | 41 | 30 | 1 BoxCollider | Separate wheels and steering pivots; front transforms and detailed-wheel LOD0 assignments repaired. |
| MF_SUV_01 | Civilian | `Assets/MetroFleet/Prefabs/Civilian/MF_SUV_01.prefab` | 2.258 / 2.038 / 4.186 | 8 | 45 | 38 | 7 | 1 BoxCollider | Separate wheels and body/trim modules preserved. |
| MF_Taxi_01 | Civilian | `Assets/MetroFleet/Prefabs/Civilian/MF_Taxi_01.prefab` | 2.124 / 1.810 / 3.854 | 9 | 49 | 39 | 7 | 1 BoxCollider | Separate wheels and existing taxi-specific components preserved. |
| MF_BoxTruck_01 | Commercial | `Assets/MetroFleet/Prefabs/Commercial/MF_BoxTruck_01.prefab` | 2.603 / 3.130 / 7.144 | 10 | 64 | 50 | 11 | 4 BoxCollider | LiftgatePivot with platform, side rails and grip details; mechanism collider follows pivot. |
| MF_DeliveryVan_01 | Commercial | `Assets/MetroFleet/Prefabs/Commercial/MF_DeliveryVan_01.prefab` | 2.135 / 2.340 / 3.967 | 13 | 84 | 55 | 12 | 7 BoxCollider | Rear door pivots, windows, handles and hinges; hinges aligned; hollow primitive coverage and door colliders. |
| MF_FlatbedTruck_01 | Commercial | `Assets/MetroFleet/Prefabs/Commercial/MF_FlatbedTruck_01.prefab` | 2.545 / 2.670 / 5.756 | 9 | 56 | 46 | 10 | 5 BoxCollider | Cab, flatbed, load/rack and separate wheels preserved. |
| MF_GarbageTruck_01 | Commercial | `Assets/MetroFleet/Prefabs/Commercial/MF_GarbageTruck_01.prefab` | 2.480 / 2.710 / 5.585 | 10 | 99 | 58 | 16 | 4 BoxCollider | LiftPivot with arms, hooks and crossbar; mechanism collider follows pivot. |
| MF_MiniUtilityTruck_01 | Commercial | `Assets/MetroFleet/Prefabs/Commercial/MF_MiniUtilityTruck_01.prefab` | 2.162 / 2.290 / 4.384 | 9 | 51 | 42 | 10 | 4 BoxCollider | Compact utility cab, cargo/load and separate wheels preserved. |
| MF_RoadSweeper_01 | Commercial | `Assets/MetroFleet/Prefabs/Commercial/MF_RoadSweeper_01.prefab` | 2.800 / 2.715 / 5.199 | 9 | 56 | 47 | 12 | 3 BoxCollider | Left/right BrushSpinPivot assemblies with brushes, bristles and drives preserved. |
| MF_Tanker_01 | Commercial | `Assets/MetroFleet/Prefabs/Commercial/MF_Tanker_01.prefab` | 2.699 / 3.240 / 6.249 | 9 | 57 | 46 | 10 | 3 BoxCollider | Distinct tank/body and cab; six separate wheels preserved. |
| MF_TowTruck_01 | Commercial | `Assets/MetroFleet/Prefabs/Commercial/MF_TowTruck_01.prefab` | 2.277 / 2.625 / 5.523 | 10 | 49 | 41 | 11 | 5 BoxCollider | TowBoomPivot, boom, pulley and cable preserved; boom collider follows pivot. |
| MF_Ambulance_01 | Emergency | `Assets/MetroFleet/Prefabs/Emergency/MF_Ambulance_01.prefab` | 2.258 / 2.780 / 4.767 | 10 | 62 | 52 | 10 | 8 BoxCollider | Rear-door pivots and existing rear opening; hinges aligned; compartment primitives and pivot-following door colliders. |
| MF_FireEngine_01 | Emergency | `Assets/MetroFleet/Prefabs/Emergency/MF_FireEngine_01.prefab` | 2.670 / 3.310 / 6.555 | 10 | 70 | 59 | 17 | 3 BoxCollider | LadderRaisePivot with ladder, rails/rungs and mounts; decorative ladder excluded from colliders. |
| MF_PoliceVehicle_01 | Emergency | `Assets/MetroFleet/Prefabs/Emergency/MF_PoliceVehicle_01.prefab` | 2.200 / 1.920 / 4.120 | 11 | 48 | 37 | 7 | 2 BoxCollider | Distinct attached red/blue roof-light modules; no siren or flashing-light scripts. |
| MF_Forklift_01 | Industrial | `Assets/MetroFleet/Prefabs/Industrial/MF_Forklift_01.prefab` | 1.567 / 2.450 / 3.734 | 8 | 43 | 32 | 24 | 3 BoxCollider | MastTiltPivot and nested CarriageLiftPivot preserved; mast and fork-area colliders follow their respective pivots. |
| MF_CityBus_01 | PublicTransport | `Assets/MetroFleet/Prefabs/PublicTransport/MF_CityBus_01.prefab` | 2.766 / 3.248 / 8.875 | 9 | 50 | 39 | 8 | 6 BoxCollider | Entry door pivots; LOD0/1 cab/window doorway meshes repaired; coarse LOD2 remains closed; door-aware primitives. |
| MF_SchoolBus_01 | PublicTransport | `Assets/MetroFleet/Prefabs/PublicTransport/MF_SchoolBus_01.prefab` | 2.718 / 3.100 / 8.547 | 11 | 53 | 41 | 8 | 6 BoxCollider | Entry door pivots; LOD0/1 cab/window doorway meshes repaired; coarse LOD2 remains closed; school identity retained. |

## Common technical standard

All roots: position (0,0,0), rotation (0,0,0), scale (1,1,1), facing Unity +Z. Each prefab has one LODGroup with populated LOD0/LOD1/LOD2, transitions 0.60 / 0.30 / 0.10, no null/duplicate assignments, and all renderers accounted for. Separate source components and useful pivots remain available. Colliders are BoxColliders; no detailed MeshColliders, controllers, animation systems, or emergency-light scripts were added. All vehicle materials use Universal Render Pipeline/Lit.

## Shipping structure and dependencies

Version 1.0.0 shipping content is under Assets/MetroFleet/: Demo, Documentation, Materials, Meshes and Prefabs. Meshes and Prefabs use the five inventory categories. The corrected bus FBXs include the approved door-aperture geometry; Meshes/PublicTransport/DoorApertures retains the 12 original derived LOD0/LOD1 cab/window meshes, but the finished prefabs no longer depend on them. There are 64 external URP/Lit material assets: 54 vehicle materials and 10 showcase materials. No external textures are required.

No custom asset dependency outside Assets/MetroFleet. Legitimate package references are URP Lit.shader, Editor/AssetVersion.cs material-version metadata, UniversalAdditionalCameraData.cs and UniversalAdditionalLightData.cs. Unity-provided primitive meshes and bundled LegacyRuntime font resources are also used by the showcase.


## Demo scenes

`Assets/MetroFleet/Demo/MetroFleet_AllVehicles_QA.unity` provides a technical fleet layout with all 20 unique vehicle prefabs.

`Assets/MetroFleet/Demo/MetroFleet_Showcase.unity` provides all 20 linked prefabs in five category zones at their original relative scale. It includes an original primitive plaza, 10 URP/Lit environment materials, category plaques using Unity's bundled font, two directional lights and ambient fill.

Nine named cameras are included: Main Fleet Overview, Hero Three-Quarter View, Civilian View, Commercial View, Public Transport View, Emergency View, Industrial Forklift View, LOD Demonstration View and Prefab Component View. Only Main Fleet Overview is enabled by default. Enable one camera at a time for inspection. The scene does not provide runtime camera navigation.

## Documentation

Version 1.0.0 includes README, INSTALLATION, VEHICLE_CATALOG, LOD_GUIDE, MATERIAL_GUIDE, PREFAB_GUIDE, CUSTOMIZATION_GUIDE, KNOWN_LIMITATIONS, CHANGELOG, LICENSE_NOTES and this technical inventory. Publication images and internal release records are not shipping dependencies.
