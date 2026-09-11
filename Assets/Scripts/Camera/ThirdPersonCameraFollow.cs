using UnityEngine;
using UnityEngine.InputSystem;

// Orbits independently of the target's own facing (driven by mouse look), the way DBD's camera
// works — WASD then moves relative to THIS camera's yaw (see NetworkedCharacterMotor.FlatForward
// usage), not raw world axes. Coupling movement to the target's instantaneous facing instead (the
// old approach) meant any diagonal input made the camera visibly whip around trying to keep up.
public class ThirdPersonCameraFollow : MonoBehaviour
{
    [SerializeField] private float distance = 5f;
    [SerializeField] private float lookHeight = 1.5f;
    [SerializeField] private float followSmoothing = 10f;
    [SerializeField] private float mouseSensitivity = 3f;
    [SerializeField] private float minPitch = -20f;
    [SerializeField] private float maxPitch = 60f;

    private Transform target;
    private float yaw;
    private float pitch = 15f;

    // Flat (yaw-only) directions for movement — looking up/down should never tilt WASD into
    // the ground or sky, only the camera orbit itself uses pitch.
    public Vector3 FlatForward => Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
    public Vector3 FlatRight => Quaternion.Euler(0f, yaw, 0f) * Vector3.right;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;

        if (target != null)
        {
            yaw = target.eulerAngles.y;
            transform.position = DesiredPosition();
            transform.rotation = Quaternion.LookRotation(LookPoint() - transform.position);
        }
    }

    private void LateUpdate()
    {
        if (target == null) return;

        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            Vector2 delta = mouse.delta.ReadValue();
            yaw += delta.x * mouseSensitivity * Time.deltaTime;
            pitch -= delta.y * mouseSensitivity * Time.deltaTime;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        transform.position = Vector3.Lerp(transform.position, DesiredPosition(), followSmoothing * Time.deltaTime);
        transform.rotation = Quaternion.LookRotation(LookPoint() - transform.position);
    }

    private Vector3 DesiredPosition()
    {
        Quaternion orbit = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 offset = orbit * new Vector3(0f, 0f, -distance);
        return LookPoint() + offset;
    }

    private Vector3 LookPoint()
    {
        return target.position + Vector3.up * lookHeight;
    }
}
