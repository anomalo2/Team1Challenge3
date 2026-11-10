# Vehicle Catalog

Exactly 20 finished vehicles. Dimensions are approximate X/Y/Z (width/height/length) in metres, from combined renderer bounds. The showcase uses unit vehicle scale; scene placement/yaw is for presentation only.

Every listed vehicle has LOD0 / LOD1 / LOD2. The components below are source geometry/hierarchy, not included gameplay systems. Detail can differ between LODs.

| Exact prefab name | Category | X / Y / Z (m) | LOD availability | Notable components |
|---|---|---|---|---|
| MF_ElectricCityCar_01 | Civilian | 1.874 / 1.480 / 3.248 | LOD0 / LOD1 / LOD2 | Separate wheels, glass, trim and compact city-car body. |
| MF_Hatchback_01 | Civilian | 2.001 / 1.620 / 3.300 | LOD0 / LOD1 / LOD2 | Separate wheels, body, glass, lights and trim. |
| MF_Pickup_01 | Civilian | 2.258 / 1.910 / 4.665 | LOD0 / LOD1 / LOD2 | Separate wheels and pickup/cargo body components. |
| MF_Sedan_01 | Civilian | 2.190 / 1.495 / 4.550 | LOD0 / LOD1 / LOD2 | Separate detailed wheels and front steering pivots. |
| MF_SUV_01 | Civilian | 2.258 / 2.038 / 4.186 | LOD0 / LOD1 / LOD2 | Separate wheels and body/trim modules. |
| MF_Taxi_01 | Civilian | 2.124 / 1.810 / 3.854 | LOD0 / LOD1 / LOD2 | Separate wheels and existing taxi-specific exterior parts. |
| MF_BoxTruck_01 | Commercial | 2.603 / 3.130 / 7.144 | LOD0 / LOD1 / LOD2 | Cab/cargo body and liftgate pivot with platform, rails and grips. |
| MF_DeliveryVan_01 | Commercial | 2.135 / 2.340 / 3.967 | LOD0 / LOD1 / LOD2 | Rear door pivots, handles, windows and hinges; primitive coverage preserves the rear opening. |
| MF_FlatbedTruck_01 | Commercial | 2.545 / 2.670 / 5.756 | LOD0 / LOD1 / LOD2 | Cab, flatbed, load/rack and separate wheels. |
| MF_GarbageTruck_01 | Commercial | 2.480 / 2.710 / 5.585 | LOD0 / LOD1 / LOD2 | Rear lift pivot with arms, hooks and crossbar. |
| MF_MiniUtilityTruck_01 | Commercial | 2.162 / 2.290 / 4.384 | LOD0 / LOD1 / LOD2 | Compact utility cab, cargo/load components and separate wheels. |
| MF_RoadSweeper_01 | Commercial | 2.800 / 2.715 / 5.199 | LOD0 / LOD1 / LOD2 | Left/right brush spin pivots with brushes, bristles and drives. |
| MF_Tanker_01 | Commercial | 2.699 / 3.240 / 6.249 | LOD0 / LOD1 / LOD2 | Distinct cab and tank/body; six separate wheels. |
| MF_TowTruck_01 | Commercial | 2.277 / 2.625 / 5.523 | LOD0 / LOD1 / LOD2 | Tow-boom pivot, boom, pulley and cable. |
| MF_Ambulance_01 | Emergency | 2.258 / 2.780 / 4.767 | LOD0 / LOD1 / LOD2 | Rear compartment opening, rear-door pivots, handles/hinges and emergency exterior parts. |
| MF_FireEngine_01 | Emergency | 2.670 / 3.310 / 6.555 | LOD0 / LOD1 / LOD2 | Ladder raise pivot, ladder rails/rungs, mounting and emergency exterior parts. |
| MF_PoliceVehicle_01 | Emergency | 2.200 / 1.920 / 4.120 | LOD0 / LOD1 / LOD2 | Distinct attached red/blue roof-light modules, police exterior colors/parts and wheels. |
| MF_Forklift_01 | Industrial | 1.567 / 2.450 / 3.734 | LOD0 / LOD1 / LOD2 | Mast tilt pivot, nested carriage lift pivot, forks, guard, seat/body and wheels. |
| MF_CityBus_01 | Public Transport | 2.766 / 3.248 / 8.875 | LOD0 / LOD1 / LOD2 | Entry-door pivots, cab/passenger body, windows and separate wheels; detailed doorway openings at LOD0/1. |
| MF_SchoolBus_01 | Public Transport | 2.718 / 3.100 / 8.547 | LOD0 / LOD1 / LOD2 | Entry-door pivots, distinct school-bus body/trim and wheels; detailed doorway openings at LOD0/1. |

Prefabs are in `Assets/MetroFleet/Prefabs/<Category>/<ExactPrefabName>.prefab`; the Public Transport folder is named `PublicTransport`. See [FLEET_TECHNICAL_INVENTORY.md](FLEET_TECHNICAL_INVENTORY.md) for exact paths, material/renderer counts, collider counts and technical notes.
