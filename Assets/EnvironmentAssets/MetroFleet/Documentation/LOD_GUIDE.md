# LOD Guide

Every finished vehicle has one LODGroup containing three populated levels.

| Level | Purpose | Default screen-relative transition |
|---|---|---:|
| LOD0 | Closest, most detailed geometry | 0.60 |
| LOD1 | Intermediate geometry | 0.30 |
| LOD2 | Most simplified distant geometry | 0.10 |

These values are projected screen-height fractions, not distances in metres. A vehicle below the final transition can be culled. Actual switching depends on camera projection, vehicle bounds, LOD bias and project settings.

## Adjust safely

1. Select a scene instance or create your own prefab variant.
2. Find the LODGroup under `Model`.
3. Adjust thresholds in descending order.
4. Preview each level and keep the intended renderer list populated.
5. Check wheels/body visibility after changes.

Do not assign the same renderer to multiple levels or remove references accidentally. Keep useful movable components together with their intended LOD renderers; functional detail availability differs by vehicle and LOD.

The showcase provides a disabled `LOD Demonstration View` camera aimed at the Sedan. Use the LODGroup preview to compare LOD0/1/2 without changing source prefabs. For a screenshot comparison, capture the same view at each preview level and clearly label the actual levels. Restore automatic selection afterward.

Renderer counts are listed in the technical inventory. No frame-rate, draw-call or platform-performance guarantee is made.
