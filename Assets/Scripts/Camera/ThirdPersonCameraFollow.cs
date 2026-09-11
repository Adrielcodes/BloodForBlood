using UnityEngine;
using UnityEngine.InputSystem;

// Standard third-person "over the shoulder" camera: horizontal orbit always matches the
// target's own facing (which NetworkedCharacterMotor rotates directly via mouse X — turning to
// look IS turning the character, same as most third-person action games), while vertical mouse
// look only tilts the camera's pitch and never affects the character itself.
public class ThirdPersonCameraFollow : MonoBehaviour
{
    [SerializeField] private float distance = 5f;
    [SerializeField] private float lookHeight = 1.5f;
    [SerializeField] private float followSmoothing = 10f;
    // Mouse.delta is already a per-frame pixel amount, not a rate — do NOT multiply by
    // Time.deltaTime (that was the original sensitivity bug: it made look speed inversely
    // proportional to framerate, so higher framerates felt sluggish).
    [SerializeField] private float mousePitchSensitivity = 0.2f;
    [SerializeField] private float minPitch = -20f;
    [SerializeField] private float maxPitch = 60f;

    private Transform target;
    private float pitch = 15f;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;

        if (target != null)
        {
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
            pitch -= mouse.delta.ReadValue().y * mousePitchSensitivity;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        transform.position = Vector3.Lerp(transform.position, DesiredPosition(), followSmoothing * Time.deltaTime);
        transform.rotation = Quaternion.LookRotation(LookPoint() - transform.position);
    }

    private Vector3 DesiredPosition()
    {
        Quaternion orbit = Quaternion.Euler(pitch, target.eulerAngles.y, 0f);
        Vector3 offset = orbit * new Vector3(0f, 0f, -distance);
        return LookPoint() + offset;
    }

    private Vector3 LookPoint()
    {
        return target.position + Vector3.up * lookHeight;
    }
}
