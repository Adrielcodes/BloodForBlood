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

    private Transform visualTransform;
    private float lastWeaponAttackServerTime = float.NegativeInfinity;

    public NetworkVariable<float> Stamina => stamina;
    public NetworkVariable<int> HitsTaken => hitsTaken;
    public NetworkVariable<bool> IsDowned => isDowned;
    public NetworkVariable<bool> HasWeapon => hasWeapon;
    public float MaxStamina => maxStamina;
    public int HitsToDown => hitsToDown;

    protected override PlayerRole RoleValue => PlayerRole.Survivor;

    protected override bool CanAct => !isDowned.Value;

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

        if (IsOwner)
        {
            SurvivorHudController.Create(this);
        }
    }

    private void HandleDownedChanged(bool previous, bool current)
    {
        if (visualTransform == null) return;
        visualTransform.localRotation = current ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
    }

    protected override float GetCurrentMoveSpeed(bool hasMoveInput, bool sprintHeld)
    {
        bool isSprinting = sprintHeld && hasMoveInput && stamina.Value > 0f;

        if (isSprinting)
        {
            stamina.Value = Mathf.Max(0f, stamina.Value - staminaDrainPerSecond * Time.deltaTime);
            return sprintSpeed;
        }

        stamina.Value = Mathf.Min(maxStamina, stamina.Value + staminaRegenPerSecond * Time.deltaTime);
        return walkSpeed;
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
