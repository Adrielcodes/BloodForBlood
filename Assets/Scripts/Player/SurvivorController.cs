using Unity.Netcode;
using UnityEngine;

public class SurvivorController : NetworkedCharacterMotor
{
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float sprintSpeed = 8f;
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float staminaDrainPerSecond = 25f;
    [SerializeField] private float staminaRegenPerSecond = 15f;

    private NetworkVariable<float> stamina;

    public NetworkVariable<float> Stamina => stamina;

    protected override PlayerRole RoleValue => PlayerRole.Survivor;

    protected override void Awake()
    {
        base.Awake();
        stamina = new NetworkVariable<float>(
            maxStamina,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);
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
}
