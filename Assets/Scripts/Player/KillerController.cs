using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class KillerController : NetworkedCharacterMotor
{
    // ~1.15x Survivor's walkSpeed (3.5) — matches Dead by Daylight's real killer:survivor
    // base-speed ratio (4.6 vs 4.0 m/s), so the Killer is always a bit faster chasing normally,
    // while a sprinting Survivor (5.5) can still briefly outrun them.
    [SerializeField] private float killerMoveSpeed = 4f;

    [SerializeField] private int maxHealth = 100;
    [SerializeField] private float attackForwardOffset = 1.5f;
    [SerializeField] private float attackRadius = 1.2f;
    [SerializeField] private float attackHeightOffset = 1f;
    [SerializeField] private float attackCooldown = 1f;

    private NetworkVariable<int> health;
    private float lastAttackServerTime = float.NegativeInfinity;

    public NetworkVariable<int> Health => health;
    public int MaxHealth => maxHealth;

    protected override PlayerRole RoleValue => PlayerRole.Killer;

    // Close, shoulder-height, shallow-pitch camera — reference: a tight over-the-shoulder Killer
    // POV from another asymmetric horror game, much closer and more level than the default
    // pulled-back/downward-angled Survivor framing (5 / 1.5 / 15), for a more intense,
    // claustrophobic feel while chasing.
    // lookHeight is an offset ABOVE THE CHARACTER'S PIVOT, not above the ground — and
    // CharacterController.center=0 puts that pivot at roughly hip/waist height (the capsule's
    // vertical center), not the feet. Survivor's 1.5 already aims the look point above the head,
    // which reads fine pulled back at distance 5/pitch 15, but at Killer-close range the same
    // offset put the camera floating near head-height looking almost level — showing far too much
    // sky/ground instead of the character. 0.55 puts the look point around chest/shoulder height.
    protected override float CameraDistance => 1.8f;
    protected override float CameraLookHeight => 0.55f;
    protected override float CameraInitialPitch => 2f;

    protected override void Awake()
    {
        base.Awake();
        health = new NetworkVariable<int>(
            maxHealth,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        AttachSwordToRightHand();

        // Health only ever decreases (no heal exists yet), so any change after spawn means "just
        // took damage" — no initial-value special-casing needed.
        health.OnValueChanged += (previous, current) => FlashHitColor(Color.red);

        if (IsOwner)
        {
            KillerHudController.Create(this);
        }
    }

    protected override float GetCurrentMoveSpeed(bool hasMoveInput, bool sprintHeld)
    {
        return killerMoveSpeed;
    }

    protected override void OnOwnerTick()
    {
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            RequestAttackRpc();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestAttackRpc(RpcParams rpcParams = default)
    {
        if (Time.time - lastAttackServerTime < attackCooldown) return;
        lastAttackServerTime = Time.time;

        foreach (var survivor in MeleeHitDetector.FindTargets<SurvivorController>(transform, attackForwardOffset, attackRadius, attackHeightOffset))
        {
            survivor.ServerRegisterHit();
        }
    }

    public void ServerApplyDamage(int amount)
    {
        if (!IsServer) return;
        health.Value = Mathf.Max(0, health.Value - amount);
    }
}
