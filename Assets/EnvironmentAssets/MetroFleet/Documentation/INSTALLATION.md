# Installation

## Project requirement

Use a Unity project already configured for the Universal Render Pipeline (URP). The verified development setup is Unity 6000.6.0f1 with URP 17.6.0. Do not use the Built-in pipeline or HDRP without your own material conversion/testing.

## Import

1. Back up your project.
2. Use `Assets > Import Package > Custom Package` to import `MetroFleet_LowPoly_Modular_City_Vehicles_v1.0.0.unitypackage`, keeping the `Assets/MetroFleet/` hierarchy intact.
3. Let Unity finish importing assets.
4. Open `Assets/MetroFleet/Demo/MetroFleet_Showcase.unity`.
5. Enter Play mode only if you want the default camera preview. The pack does not animate or drive vehicles.

Prefabs are in `Assets/MetroFleet/Prefabs/`, grouped into Civilian, Commercial, PublicTransport, Emergency and Industrial. Meshes and shared materials are inside the same MetroFleet root.

## Inspect the showcase

Expand `01_FLEET` in the Hierarchy, then a category and vehicle. Inspect its model, LODGroup, wheels, pivots and colliders. Camera objects are under `05_CAMERA_VIEWS`. The Main Fleet Overview camera is enabled by default; disable it before enabling another camera. Leave only one enabled camera. No camera controller or Cinemachine package is needed.

The large product-title plaque uses Unity's built-in Ignore Raycast layer so close-up cameras can exclude it; vehicle objects are not hidden.

## Recommended setup

Use the project's existing URP renderer with soft shadows enabled when supported. If distant overview shadows are absent, inspect your URP shadow-distance setting; close-up views provide a better material/component inspection. Do not change the render pipeline just to open the demo.

No paid environment assets, extra font package, traffic framework or controller dependency is required. The tested version above is the compatibility baseline.
