using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class SurvivorController : NetworkedCharacterMotor
{
    [SerializeField] private float walkSpeed = 3.5f;
    [SerializeField] private float sprintSpeed = 5.5f;
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float staminaDrainPerSecond = 25f;
    [SerializeField] private float staminaRegenPerSecond = 15f;
    // Once stamina hits 0, sprint stays locked out until it regenerates back up to at least
    // this much — without a gap like this, the instant stamina ticks up even slightly above 0,
    // sprint re-enables and drains it straight back to 0 next frame, flip-flopping every single
    // frame (sprintSpeed/walkSpeed oscillating 60x/sec) for as long as Shift+move is held with
    // no stamina. That was the actual cause of the reported jitter.
    [SerializeField] private float minStaminaToResumeSprint = 15f;

    [SerializeField] private int hitsToDown = 3;

    [SerializeField] private int weaponDamage = 5;
    [SerializeField] private float weaponAttackForwardOffset = 1.2f;
    [SerializeField] private float weaponAttackRadius = 1f;
    [SerializeField] private float weaponAttackCooldown = 0.8f;

    private NetworkVariable<float> stamina;
    private readonly NetworkVariable<int> hitsTaken = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> isDowned = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> hasWeapon = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    // Declared BEFORE isHidden on purpose: NGO delivers same-tick NetworkVariable deltas in field
    // declaration order, so the anchor position always lands before the flag that consumes it.
    private readonly NetworkVariable<Vector3> hiddenAnchor = new NetworkVariable<Vector3>(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> isHidden = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Transform visualTransform;
    private float lastWeaponAttackServerTime = float.NegativeInfinity;
    private bool staminaExhausted;

    public NetworkVariable<float> Stamina => stamina;
    public NetworkVariable<int> HitsTaken => hitsTaken;
    public NetworkVariable<bool> IsDowned => isDowned;
    public NetworkVariable<bool> HasWeapon => hasWeapon;
    public NetworkVariable<bool> IsHidden => isHidden;
    public float MaxStamina => maxStamina;
    public int HitsToDown => hitsToDown;

    protected override PlayerRole RoleValue => PlayerRole.Survivor;

    protected override bool CanAct => base.CanAct && !isDowned.Value && !isHidden.Value;

    // Locker hide/unhide (HidingLocker). The server can't move an owner-authoritative transform,
    // so it replicates the anchor and every client applies the state locally: the owner teleports,
    // and everyone (server included — its OverlapSphere melee check must not find a hidden
    // Survivor) disables the CharacterController collider and hides the model.
    public void ServerSetHidden(bool hidden, Vector3 anchor)
    {
        if (!IsServer) return;
        hiddenAnchor.Value = anchor;
        isHidden.Value = hidden;
    }

    private void HandleHiddenChanged(bool previous, bool current)
    {
        if (visualTransform != null) visualTransform.gameObject.SetActive(!current);
        if (IsOwner) OwnerTeleport(hiddenAnchor.Value);
        Controller.enabled = !current;
    }

    protected override void Awake()
    {
        base.Awake();
        stamina = new NetworkVariable<float>(
            maxStamina,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);
        visualTransform = transform.Find("Visual");
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        isDowned.OnValueChanged += HandleDownedChanged;
        HandleDownedChanged(false, isDowned.Value);

        hasWeapon.OnValueChanged += HandleHasWeaponChanged;
        if (hasWeapon.Value) AttachSwordToRightHand();

        isHidden.OnValueChanged += HandleHiddenChanged;
        if (isHidden.Value) HandleHiddenChanged(false, true);

        // Any change to HitsTaken after spawn means "just got hit" (it only ever increments, no
        // heal/revive exists yet), so no initial-value special-casing is needed here unlike
        // isDowned/hasWeapon above.
        hitsTaken.OnValueChanged += (previous, current) =>
        {
            FlashHitColor(Color.red);
            Sfx.PlayAt("Hit", transform.position, 1f);
        };

        if (IsOwner)
        {
            SurvivorHudController.Create(this);
            gameObject.AddComponent<TerrorRadiusAudio>();
        }
    }

    private void HandleHasWeaponChanged(bool previous, bool current)
    {
        if (current) AttachSwordToRightHand();
    }

    private void HandleDownedChanged(bool previous, bool current)
    {
        if (visualTransform == null) return;
        visualTransform.localRotation = current ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
    }

    protected override float GetCurrentMoveSpeed(bool hasMoveInput, bool sprintHeld)
    {
        if (staminaExhausted && stamina.Value >= minStaminaToResumeSprint)
        {
            staminaExhausted = false;
        }

        bool isSprinting = sprintHeld && hasMoveInput && !staminaExhausted && stamina.Value > 0f;

        if (isSprinting)
        {
            SetStamina(Mathf.Max(0f, stamina.Value - staminaDrainPerSecond * Time.deltaTime));
            if (stamina.Value <= 0f)
            {
                staminaExhausted = true;
            }
            return sprintSpeed;
        }

        SetStamina(Mathf.Min(maxStamina, stamina.Value + staminaRegenPerSecond * Time.deltaTime));
        return walkSpeed;
    }

    // Skips reassigning (and therefore re-marking dirty / re-replicating) the NetworkVariable
    // once it's already sitting at the same clamped value — e.g. sitting at 0 while continuing
    // to sprint with no stamina, or already at max while standing still. Without this, Stamina
    // was being written to and synced over the network every single frame indefinitely, even
    // when nothing was actually changing.
    private void SetStamina(float newValue)
    {
        if (!Mathf.Approximately(stamina.Value, newValue))
        {
            stamina.Value = newValue;
        }
    }

    protected override void OnOwnerTick()
    {
        if (!hasWeapon.Value) return;

        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            RequestWeaponAttackRpc();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestWeaponAttackRpc(RpcParams rpcParams = default)
    {
        if (!hasWeapon.Value) return;
        if (Time.time - lastWeaponAttackServerTime < weaponAttackCooldown) return;
        lastWeaponAttackServerTime = Time.time;

        foreach (var killer in MeleeHitDetector.FindTargets<KillerController>(transform, weaponAttackForwardOffset, weaponAttackRadius))
        {
            killer.ServerApplyDamage(weaponDamage);
        }
    }

    public void ServerRegisterHit()
    {
        if (!IsServer || isDowned.Value) return;

        hitsTaken.Value++;
        if (hitsTaken.Value >= hitsToDown)
        {
            isDowned.Value = true;
        }
    }

    public void ServerGrantWeapon()
    {
        if (!IsServer) return;
        hasWeapon.Value = true;
    }
}
