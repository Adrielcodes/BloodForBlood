using UnityEngine;

public class ThirdPersonCameraFollow : MonoBehaviour
{
    [SerializeField] private float distance = 5f;
    [SerializeField] private float height = 2f;
    [SerializeField] private float lookHeight = 1.5f;
    [SerializeField] private float followSmoothing = 10f;
    [SerializeField] private float lookSmoothing = 10f;

    private Transform target;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;

        if (target != null)
        {
            transform.position = DesiredPosition();
            transform.rotation = DesiredRotation();
        }
    }

    private void LateUpdate()
    {
        if (target == null) return;

        transform.position = Vector3.Lerp(transform.position, DesiredPosition(), followSmoothing * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, DesiredRotation(), lookSmoothing * Time.deltaTime);
    }

    private Vector3 DesiredPosition()
    {
        return target.position - target.forward * distance + Vector3.up * height;
    }

    private Quaternion DesiredRotation()
    {
        Vector3 lookPoint = target.position + Vector3.up * lookHeight;
        return Quaternion.LookRotation(lookPoint - DesiredPosition());
    }
}
