using UnityEngine;

/// <summary>
/// What this object actually is.
///
/// The city is a merged export whose object names are procedural
/// (unit_007_inst_078_mdl_010_lod_000_material_091_0). Working out what any of
/// it represents costs a full scene analysis every time. This component stores
/// the answer once, so later tools can just ask.
///
/// The useful key is ModelId: the mdl_ field is the model TYPE, so every
/// instance of mdl_014 is the same lamp post. Identify 21 types and you have
/// identified several thousand objects.
/// </summary>
public class PropLabel : MonoBehaviour
{
    public enum PropCategory
    {
        Unknown,
        StreetLight,    // lamp posts, tall lighting
        TrafficControl, // traffic lights, signs, chevrons, bollards
        StreetFurniture,// benches, bins, planters, hydrants
        Vegetation,     // trees, bushes
        Barrier,        // cones, fences, blocks
        Utility,        // AC units, cabinets, meters
        Building,
        Ground,         // roads, pavements, markings
        Backdrop
    }

    [Header("Identity")]
    [Tooltip("The mdl_ number from the source export. The same number is always the same model.")]
    public string modelId;
    [Tooltip("Readable name for this model type.")]
    public string typeName = "Unknown";
    public PropCategory category = PropCategory.Unknown;

    [Header("Measured")]
    [Tooltip("Typical world size of this model type, in metres.")]
    public Vector3 typicalSize;
    [Tooltip("How many of this type exist in the scene.")]
    public int instancesInScene;

    [Header("Behaviour")]
    [Tooltip("Should this break when hit? Set by the labelling pass from the category.")]
    public bool shouldBeBreakable;

    public override string ToString()
    {
        return string.Format("{0} (mdl_{1}, {2})", typeName, modelId, category);
    }
}
