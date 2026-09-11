using UnityEngine;

// Slow idle rotation for the activated beacon's light beam — a cheap bit of "living" motion,
// purely cosmetic, not networked (fine even if it drifts slightly out of sync between clients).
public class BeaconBeamSpin : MonoBehaviour
{
    [SerializeField] private float degreesPerSecond = 15f;

    private void Update()
    {
        transform.Rotate(Vector3.up, degreesPerSecond * Time.deltaTime, Space.Self);
    }
}
