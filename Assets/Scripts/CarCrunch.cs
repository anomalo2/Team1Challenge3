using UnityEngine;

public class CarCrunch : MonoBehaviour
{
    // TODO Fix

    [SerializeField] Renderer bodyRenderer;
    [SerializeField] float maxStrength = 0.25f;
    [SerializeField] float radius = 0.6f;
    [SerializeField] float impactThreshold = 4f;

    MaterialPropertyBlock mpb;
    float current, target;

    void Awake()
    {
        mpb = new MaterialPropertyBlock();
        bodyRenderer.GetPropertyBlock(mpb);
        mpb.SetFloat("_Radius", radius);
        mpb.SetFloat("_Strength", 0);
        bodyRenderer.SetPropertyBlock(mpb);
    }

    void OnCollisionEnter(Collision c)
    {
        float speed = c.relativeVelocity.magnitude;
        if (speed < impactThreshold) return;

        var contact = c.GetContact(0);
        Vector3 localPoint = transform.InverseTransformPoint(contact.point);
        Vector3 localDir = transform.InverseTransformDirection(-contact.normal);

        target = Mathf.Min(target + speed * 0.01f, maxStrength);

        bodyRenderer.GetPropertyBlock(mpb);
        mpb.SetVector("_ImpactPoint", localPoint);
        mpb.SetVector("_ImpactDir", localDir);
        bodyRenderer.SetPropertyBlock(mpb);
    }

    void Update()
    {
        if (Mathf.Approximately(current, target)) return;
        current = Mathf.MoveTowards(current, target, Time.deltaTime * 2f);
        bodyRenderer.GetPropertyBlock(mpb);
        mpb.SetFloat("_Strength", current);
        bodyRenderer.SetPropertyBlock(mpb);
    }
}