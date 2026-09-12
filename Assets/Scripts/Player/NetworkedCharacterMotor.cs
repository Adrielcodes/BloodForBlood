using System.Collections;
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

    // Subclasses combine with their own state (downed, stunned, hidden) via `base.CanAct && ...`.
    protected virtual bool CanAct => !isActionLocked;

    protected virtual void OnOwnerTick() { }

    // Vaulting is an owner-local action (no RPC): the owner just moves itself across the obstacle
    // and OwnerNetworkTransform replicates it like any other movement. The Killer vaults slower,
    // same idea as DBD — that gap is what makes a window a real chase tool for a Survivor.
    protected virtual float VaultDuration => 0.65f;

    private bool isActionLocked;

    public bool IsActionLocked => isActionLocked;

    // For interactables (VaultableObstacle, HidingLocker, SlamDoor) that need to know whether the
    // local character is currently free to act, without each of them re-deriving downed/stunned/
    // hidden/vaulting state.
    public bool CanCurrentlyAct => CanAct;

    protected CharacterController Controller => controller;

    // Moves this (owned) character to `target` over VaultDuration with input locked. Called by
    // VaultableObstacle for the local owner only.
    public void BeginVault(Vector3 target)
    {
        if (!IsOwner || isActionLocked) return;
        StartCoroutine(VaultRoutine(target));
    }

    private IEnumerator VaultRoutine(Vector3 target)
    {
        isActionLocked = true;
        Sfx.PlayAt("Vault", transform.position, 0.7f, 0.1f, 20f);
        Vector3 start = transform.position;
        Vector3 flat = target - start;
        flat.y = 0f;
        if (flat.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(flat);

        // CharacterController.Move would fight the obstacle's own collider mid-vault, so drive the
        // transform directly and re-enable the controller once we're on the far side.
        controller.enabled = false;
        float t = 0f;
        float duration = VaultDuration;
        while (t < duration)
        {
            t += Time.deltaTime;
            float p = Mathf.Clamp01(t / duration);
            Vector3 pos = Vector3.Lerp(start, target, p);
            pos.y += Mathf.Sin(p * Mathf.PI) * 0.6f;
            transform.position = pos;
            yield return null;
        }
        transform.position = target;
        controller.enabled = true;
        verticalVelocity = 0f;
        isActionLocked = false;
    }

    // Owner-side teleport (e.g. entering/leaving a locker) — the server can't move an
    // owner-authoritative transform directly, so it replicates an anchor and the owner applies it.
    public void OwnerTeleport(Vector3 position)
    {
        if (!IsOwner) return;
        controller.enabled = false;
        transform.position = position;
        controller.enabled = true;
        verticalVelocity = 0f;
    }

    // Camera framing hooks — shared close/shoulder-height default for both roles (see
    // KillerController's original tuning notes for why lookHeight is small: it's an offset above
    // the character's pivot/hip height, not the ground, so a small value already reaches
    // chest/shoulder height). Neither role currently overrides these; a future role-specific look
    // can still override per-role via these same hooks.
    protected virtual float CameraDistance => 1.8f;
    protected virtual float CameraLookHeight => 0.55f;
    protected virtual float CameraInitialPitch => 2f;

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
                cameraFollow.Configure(CameraDistance, CameraLookHeight, CameraInitialPitch);
                cameraFollow.SetTarget(transform);
            }

            // Lock the cursor to the center of the screen for gameplay now that we're actually
            // controlling a character — left free before this so the Host/Client dev UI buttons
            // are clickable. Escape (below, in Update) releases it again.
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            CrosshairController.Create();
        }
    }

    private void Update()
    {
        if (!IsOwner) return;

        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if (kb.escapeKey.wasPressedThisFrame)
        {
            bool isLocked = Cursor.lockState == CursorLockMode.Locked;
            Cursor.lockState = isLocked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = isLocked;
        }

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

        // -0.5f used to be enough to keep controller.isGrounded true while standing still, but it
        // was actually borderline: on a perfectly flat plane the CharacterController's own ground
        // check can drop out for a single frame, which flips this back into the gravity-accumulate
        // branch and produces a barely-visible vertical bob every frame — reported as "character
        // flickers" / "slightly above the ground". A firmer downward bias keeps contact stable.
        if (!controller.enabled) return;
        verticalVelocity = controller.isGrounded ? -2f : verticalVelocity + gravity * Time.deltaTime;
        controller.Move(new Vector3(0f, verticalVelocity, 0f) * Time.deltaTime);
    }

    protected abstract float GetCurrentMoveSpeed(bool hasMoveInput, bool sprintHeld);

    // Shared by KillerController (always equipped) and SurvivorController (once armed) — parents
    // a placeholder sword to the model's Humanoid-mapped right hand bone. Runs identically on
    // every client (not owner-gated): everyone watching needs to see the weapon, not just the
    // local player. Returns null if the model has no right hand (shouldn't happen for either
    // current character model, both are Humanoid).
    protected GameObject AttachSwordToRightHand()
    {
        if (animator == null) return null;

        Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        if (hand == null) return null;

        GameObject sword = SwordVisualBuilder.Build(hand, "EquippedSword");
        sword.transform.localPosition = new Vector3(0.02f, 0.05f, 0f);
        sword.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        return sword;
    }

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int LegacyColorId = Shader.PropertyToID("_Color");

    // Hit feedback — briefly overrides every renderer under "Visual" to a flat flash color via a
    // MaterialPropertyBlock (no per-instance material allocation, and the shared placeholder
    // materials stay untouched for every other character using them). Called from each role's
    // server-authoritative damage NetworkVariable's OnValueChanged, so it fires identically for
    // every client watching, not just the owner. Sets both _BaseColor (URP/Lit, what this
    // project's placeholder materials use) and the legacy _Color for safety.
    protected void FlashHitColor(Color flashColor, float duration = 0.15f)
    {
        StartCoroutine(FlashHitColorRoutine(flashColor, duration));
    }

    private IEnumerator FlashHitColorRoutine(Color flashColor, float duration)
    {
        Transform visual = transform.Find("Visual");
        if (visual == null) yield break;

        Renderer[] renderers = visual.GetComponentsInChildren<Renderer>();
        var block = new MaterialPropertyBlock();
        block.SetColor(BaseColorId, flashColor);
        block.SetColor(LegacyColorId, flashColor);

        foreach (Renderer r in renderers)
        {
            r.SetPropertyBlock(block);
        }

        yield return new WaitForSeconds(duration);

        foreach (Renderer r in renderers)
        {
            if (r != null) r.SetPropertyBlock(null);
        }
    }

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
