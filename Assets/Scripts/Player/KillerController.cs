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
