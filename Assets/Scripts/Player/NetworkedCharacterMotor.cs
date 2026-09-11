using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public abstract class NetworkedCharacterMotor : NetworkBehaviour
{
    [SerializeField] private float rotationSpeed = 20f;
    [SerializeField] private float gravity = -9.81f;

    private CharacterController controller;
    private float verticalVelocity;
    private Animator animator;
    private Vector3 lastPosition;
    private ThirdPersonCameraFollow cameraFollow;

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
                cameraFollow = cam.GetComponent<ThirdPersonCameraFollow>();
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

        if (CanAct)
        {
            float horizontal = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float vertical = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            bool sprintHeld = kb.leftShiftKey.isPressed;

            // Camera-relative, DBD-style: WASD moves relative to where the (mouse-orbited)
            // camera is currently facing, not raw world axes — the camera's yaw is independent
            // of the character's own facing, so diagonal input (e.g. W+D) no longer fights the
            // camera trying to stay behind an instantly-snapping facing direction.
            Vector3 camForward = cameraFollow != null ? cameraFollow.FlatForward : transform.forward;
            Vector3 camRight = cameraFollow != null ? cameraFollow.FlatRight : transform.right;
            Vector3 move = camForward * vertical + camRight * horizontal;
            bool hasMoveInput = move.sqrMagnitude > 0.001f;
            if (hasMoveInput) move.Normalize();

            float speed = GetCurrentMoveSpeed(hasMoveInput, sprintHeld);

            if (hasMoveInput)
            {
                controller.Move(move * speed * Time.deltaTime);
                Quaternion targetRotation = Quaternion.LookRotation(move);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
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
