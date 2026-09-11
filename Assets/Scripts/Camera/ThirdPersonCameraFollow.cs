using UnityEngine;
using UnityEngine.InputSystem;

// Purely a free-look camera: orbits around the target's position via mouse, completely
// decoupled from the target's own facing/movement. Movement (see NetworkedCharacterMotor) does
// NOT read anything from this camera — moving the mouse only changes what you're looking at,
// never which way WASD moves you.
public class ThirdPersonCameraFollow : MonoBehaviour
{
    [SerializeField] private float distance = 5f;
    [SerializeField] private float lookHeight = 1.5f;
    [SerializeField] private float followSmoothing = 10f;
    // Mouse.delta is already a per-frame pixel amount, not a rate — do NOT multiply by
    // Time.deltaTime (that was the original sensitivity bug: it made look speed inversely
    // proportional to framerate, so higher framerates felt sluggish).
    [SerializeField] private float mouseSensitivity = 0.2f;
    [SerializeField] private float minPitch = -20f;
    [SerializeField] private float maxPitch = 60f;

    private Transform target;
    private float yaw;
    private float pitch = 15f;

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
            yaw += delta.x * mouseSensitivity;
            pitch -= delta.y * mouseSensitivity;
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
