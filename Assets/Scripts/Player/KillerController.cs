using UnityEngine;

public class KillerController : NetworkedCharacterMotor
{
    [SerializeField] private float killerMoveSpeed = 9f;

    protected override PlayerRole RoleValue => PlayerRole.Killer;

    protected override float GetCurrentMoveSpeed(bool hasMoveInput, bool sprintHeld)
    {
        return killerMoveSpeed;
    }
}
