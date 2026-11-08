# Basic Customization

## Colors

Duplicate the desired paint material, change Base Color, then assign it to corresponding body renderers on all three LOD levels of your scene instance or prefab variant. Leave shared tire/glass/trim materials unchanged unless you intend a fleet-wide edit.

## LODs

Select the LODGroup under Model. Keep thresholds in descending order and preview each level. The default values are 0.60 / 0.30 / 0.10; adjust to your camera/project rather than expecting one universal distance setting.

## Colliders

Enable collider gizmos and inspect BoxCollider center/size. Modify your instance/variant, not the pack reference. Keep practical coverage close to the model. Preserve door/entry openings and pivot-following colliders; avoid replacing them with one oversized box that blocks useful components.

## Wheels and existing pivots

Separate wheels and special components can be selected through the hierarchy. Do not reparent meshes casually: source mesh coordinates and functional pivot frames are significant. Shipping models already use +Z forward; the nested Model transform needs no orientation correction. Make small changes on a duplicate/variant, preview all LODs and verify materials/colliders afterward.

The pack does not supply steering, lifting, ladder, driving, traffic or siren code. Customers implement any behavior themselves.

## Presentation

Enable one named showcase camera at a time. Adjust scene lighting, original plaza colors or display positions for your project without resizing the vehicles for composition. The Main Fleet Overview and Hero Three-Quarter View include the product-title plaque; close-up views exclude that plaque only.

Use the separate technical QA scene to compare relative size and check references after customization.
