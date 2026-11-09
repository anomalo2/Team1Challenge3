using UnityEngine;

public class GroundCracks : MonoBehaviour
{
    [Header("Crack Settings")]
    public GameObject crackPrefab; // The crack decal or prefab
    public LayerMask groundLayer;  // Layer your floor is on
    public float crackLifetime = 5f; // How long before the crack disappears

    [Header("Foot References")]
    public Transform leftFoot;
    public Transform rightFoot;
    private bool useLeftFoot = true; // Toggle between feet

    // This method is called by the Animation Events
    public void SpawnCrack()
    {
        // Choose whether to cast from the specific foot or the central body
        Transform targetFoot = useLeftFoot ? leftFoot : rightFoot;
        Vector3 rayStart = targetFoot != null ? targetFoot.position + Vector3.up : transform.position + Vector3.up;

        // Raycast downward to find the ground point
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 2f, groundLayer))
        {
            // Spawn the crack slightly above the floor surface to prevent Z-fighting
            Vector3 spawnPos = hit.point + (hit.normal * 0.01f);

            // Align the crack rotation to match the surface incline
            Quaternion spawnRot = Quaternion.LookRotation(hit.normal) * Quaternion.Euler(90, Random.Range(0, 360), 0);

            GameObject crack = Instantiate(crackPrefab, spawnPos, spawnRot);

            // Destroy the crack after a set time to keep the scene clean
            Destroy(crack, crackLifetime);
        }

        // Alternate feet if references are provided
        if (leftFoot != null && rightFoot != null)
        {
            useLeftFoot = !useLeftFoot;
        }
    }
}