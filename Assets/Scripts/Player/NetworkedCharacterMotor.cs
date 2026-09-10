using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public abstract class NetworkedCharacterMotor : NetworkBehaviour
{
    [SerializeField] private float rotationSpeed = 10f;
    [SerializeField] private float gravity = -9.81f;

    private CharacterController controller;
    private float verticalVelocity;

    private NetworkVariable<PlayerRole> role = new NetworkVariable<PlayerRole>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<PlayerRole> Role => role;

    protected bool IsGrounded => controller.isGrounded;

    protected abstract PlayerRole RoleValue { get; }

    protected virtual void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            role.Value = RoleValue;
        }
    }

    private void Update()
    {
        if (!IsOwner) return;

        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        float horizontal = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        float vertical = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
        bool sprintHeld = kb.leftShiftKey.isPressed;

        Vector3 move = new Vector3(horizontal, 0f, vertical);
        bool hasMoveInput = move.sqrMagnitude > 0.001f;
        if (hasMoveInput) move.Normalize();

        float speed = GetCurrentMoveSpeed(hasMoveInput, sprintHeld);

        if (hasMoveInput)
        {
            controller.Move(move * speed * Time.deltaTime);
            Quaternion targetRotation = Quaternion.LookRotation(move);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }

        verticalVelocity = controller.isGrounded ? -0.5f : verticalVelocity + gravity * Time.deltaTime;
        controller.Move(new Vector3(0f, verticalVelocity, 0f) * Time.deltaTime);
    }

    protected abstract float GetCurrentMoveSpeed(bool hasMoveInput, bool sprintHeld);
}
