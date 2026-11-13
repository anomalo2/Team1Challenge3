using System;
using UnityEngine;

public class FingerGrabTrigger : MonoBehaviour
{
    // each finger trigger for grab caan have a copy of this script, forwards it to the check
    public event Action<Collider> OnEnter;
    public event Action<Collider> OnExit;

    //Triggers or collisions?
    private void OnTriggerEnter(Collider other) => OnEnter?.Invoke(other);
    private void OnTriggerExit(Collider other) => OnExit?.Invoke(other);
}