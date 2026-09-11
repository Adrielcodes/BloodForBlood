using UnityEngine;

// Purely cosmetic idle spin/bob for world pickups — not networked, runs independently (and not
// frame-perfectly synced) on every client, which is fine for eye-candy like this. Only ever
// attached to the WeaponPickup world model's Visual child, never to the in-hand equipped sword
// (NetworkedCharacterMotor.AttachSwordToRightHand builds that separately, without this component).
public class PickupVisualSpin : MonoBehaviour
{
    [SerializeField] private float degreesPerSecond = 90f;
    [SerializeField] private float bobHeight = 0.15f;
    [SerializeField] private float bobSpeed = 2f;

    private Vector3 basePosition;

    private void Start()
    {
        basePosition = transform.localPosition;
    }

    private void Update()
    {
        transform.Rotate(Vector3.up, degreesPerSecond * Time.deltaTime, Space.Self);
        float offset = Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.localPosition = basePosition + new Vector3(0f, offset, 0f);
    }
}
