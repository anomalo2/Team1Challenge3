# Prefab Guide

## Conventions

Finished prefab roots have position `0,0,0`, rotation `0,0,0` and scale `1,1,1`. Local forward is Unity `+Z`. Approximate physical dimensions are in metres.

A nested `Model` organizes the model hierarchy and uses identity position, rotation and scale. The shipping FBXs already use Unity's +Z forward / +Y up convention; no orientation-compensation transform is required. Showcase instances have composition-specific positions/yaw rotations, but the prefab assets retain their standardized roots and the instances keep unit scale.

## Structure

- One LODGroup under Model with LOD0/1/2 renderer assignments.
- Separate source wheel meshes and useful body/trim components.
- BoxCollider coverage on the root or lightweight child objects.
- Vehicle-specific pivots/components where present.

Collider count varies by vehicle. Doors, lifts, the tow boom and forklift assemblies may have colliders parented to their functional pivots. Decorative ladder details are not individually collider-wrapped. No detailed MeshCollider physics system is provided.

## Special components

Garbage Truck lift, Box Truck liftgate, Tow Truck boom, Road Sweeper brushes, bus entry doors, van/ambulance rear doors, Fire Engine ladder and Forklift mast/carriage are preserved as useful source hierarchy. Police and emergency-light parts remain separate where present. Availability and detail vary across LODs.

These components do not operate automatically. Add behavior in your own scene/variant if needed; do not assume a controller, animation clip, rigidbody setup or articulation range is included.

When duplicating/replacing a mesh object, update its LODGroup reference. Prefer prefab variants for project-specific edits and keep the original pack assets as a reference.
