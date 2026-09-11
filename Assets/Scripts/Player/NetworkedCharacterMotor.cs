using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public abstract class NetworkedCharacterMotor : NetworkBehaviour
{
    // Mouse.delta is a per-frame pixel amount, not a rate — do not multiply by Time.deltaTime
    // (that inverts the relationship with framerate and feels sluggish/inconsistent).
    [SerializeField] private float mouseYawSensitivity = 0.2f;
    [SerializeField] private float gravity = -9.81f;

    private CharacterController controller;
    private float verticalVelocity;
    private Animator animator;
    private Vector3 lastPosition;

    private NetworkVariable<PlayerRole> role = new NetworkVariable<PlayerRole>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<PlayerRole> Role => role;

    protected bool IsGrounded => controller.isGrounded;

    protected abstract PlayerRole RoleValue { get; }

    protected virtual bool CanAct => true;

    protected virtual void OnOwnerTick() { }

    protected virtual void Awake()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponentInChildren<Animator>();
        lastPosition = transform.position;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            role.Value = RoleValue;
        }

        // Each client only ever sees its own local Camera.main, so gating this on IsOwner
        // correctly means "only the locally controlled character gets followed" without any
        // networked camera state — every other client's view is unaffected.
        if (IsOwner)
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                ThirdPersonCameraFollow cameraFollow = cam.GetComponent<ThirdPersonCameraFollow>();
                if (cameraFollow == null)
                    cameraFollow = cam.gameObject.AddComponent<ThirdPersonCameraFollow>();
                cameraFollow.SetTarget(transform);
            }
        }
    }

    private void Update()
    {
        if (!IsOwner) return;

        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        // Mouse turns the character directly — this IS "looking around" in a third-person game
        // with a forward-facing character. Allowed even while CanAct is false (downed) so you
        // can still look, just not move.
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            float yawDelta = mouse.delta.ReadValue().x * mouseYawSensitivity;
            transform.Rotate(Vector3.up, yawDelta, Space.World);
        }

        if (CanAct)
        {
            float horizontal = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float vertical = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            bool sprintHeld = kb.leftShiftKey.isPressed;

            // Character-relative: W always means "the direction I'm currently facing" (which
            // the mouse controls), not a fixed world axis and not the camera's own direction.
            Vector3 move = transform.forward * vertical + transform.right * horizontal;
            bool hasMoveInput = move.sqrMagnitude > 0.001f;
            if (hasMoveInput) move.Normalize();

            float speed = GetCurrentMoveSpeed(hasMoveInput, sprintHeld);

            if (hasMoveInput)
            {
                controller.Move(move * speed * Time.deltaTime);
            }

            OnOwnerTick();
        }

        verticalVelocity = controller.isGrounded ? -0.5f : verticalVelocity + gravity * Time.deltaTime;
        controller.Move(new Vector3(0f, verticalVelocity, 0f) * Time.deltaTime);
    }

    protected abstract float GetCurrentMoveSpeed(bool hasMoveInput, bool sprintHeld);

    // Runs on every client (owner and observers alike), unlike Update()'s owner-gated input
    // handling — animation needs to play for everyone watching this character, not just its
    // owner. Deriving speed from the already-replicated transform.position (via
    // OwnerNetworkTransform) avoids needing a separate NetworkVariable just for animation.
    private void LateUpdate()
    {
        if (animator == null) return;

        Vector3 delta = transform.position - lastPosition;
        delta.y = 0f;
        float speed = Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;
        animator.SetFloat("Speed", speed);
        lastPosition = transform.position;
    }
}
