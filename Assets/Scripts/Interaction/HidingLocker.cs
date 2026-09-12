using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Survivor: E to climb in (model hidden, collider off — invisible to the Killer's melee overlap
// check too), E again to climb out. Killer: E on a locker searches it; if someone's inside they
// get pulled out and take a hit. Occupancy is server-owned; the actual hide/unhide is applied by
// SurvivorController.ServerSetHidden -> replicated IsHidden/anchor (see notes there on why the
// server can't just move the Survivor itself).
public class HidingLocker : NetworkBehaviour
{
    private const ulong NoOccupant = ulong.MaxValue;

    [SerializeField] private float interactRange = 2f;
    [SerializeField] private float exitDistance = 1.3f;

    private readonly NetworkVariable<ulong> occupantClientId = new NetworkVariable<ulong>(
        NoOccupant, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkedCharacterMotor localCandidate;

    public override void OnNetworkSpawn()
    {
        occupantClientId.OnValueChanged += (previous, current) => Sfx.PlayAt("DoorSlam", transform.position, 0.5f, 0.1f, 20f);
    }

    private void OnTriggerEnter(Collider other)
    {
        var motor = other.GetComponentInParent<NetworkedCharacterMotor>();
        if (motor != null && motor.IsOwner) localCandidate = motor;
    }

    private void OnTriggerExit(Collider other)
    {
        var motor = other.GetComponentInParent<NetworkedCharacterMotor>();
        if (motor == null || motor != localCandidate) return;
        // Hiding disables the Survivor's CharacterController, which fires OnTriggerExit — the
        // occupant must keep its candidate status or it could never press E to get back out.
        if (occupantClientId.Value == NetworkManager.LocalClientId) return;

        localCandidate = null;
        InteractionPromptController.Hide();
    }

    private void Update()
    {
        if (localCandidate == null || !IsSpawned) return;

        bool occupied = occupantClientId.Value != NoOccupant;
        bool occupiedByMe = occupantClientId.Value == NetworkManager.LocalClientId;

        string prompt = null;
        if (localCandidate is SurvivorController)
        {
            if (occupiedByMe) prompt = "Press E to leave locker";
            else if (!occupied && localCandidate.CanCurrentlyAct) prompt = "Press E to hide";
        }
        else if (localCandidate is KillerController && occupied && localCandidate.CanCurrentlyAct)
        {
            prompt = "Press E to search locker";
        }

        if (prompt == null) return;
        InteractionPromptController.Show(prompt);

        Keyboard kb = Keyboard.current;
        if (kb != null && kb.eKey.wasPressedThisFrame) RequestInteractRpc();
    }

    [Rpc(SendTo.Server)]
    private void RequestInteractRpc(RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (!NetworkManager.ConnectedClients.TryGetValue(sender, out NetworkClient client)) return;
        if (client.PlayerObject == null) return;

        var survivor = client.PlayerObject.GetComponent<SurvivorController>();
        if (survivor != null)
        {
            if (occupantClientId.Value == sender)
            {
                ServerEject(survivor);
            }
            else if (occupantClientId.Value == NoOccupant && !survivor.IsDowned.Value && IsInRange(survivor))
            {
                occupantClientId.Value = sender;
                survivor.ServerSetHidden(true, transform.position + Vector3.up);
            }
            return;
        }

        var killer = client.PlayerObject.GetComponent<KillerController>();
        if (killer == null || occupantClientId.Value == NoOccupant || !IsInRange(killer)) return;

        if (NetworkManager.ConnectedClients.TryGetValue(occupantClientId.Value, out NetworkClient occupantClient)
            && occupantClient.PlayerObject != null)
        {
            var occupant = occupantClient.PlayerObject.GetComponent<SurvivorController>();
            if (occupant != null)
            {
                ServerEject(occupant);
                occupant.ServerRegisterHit();
                return;
            }
        }

        occupantClientId.Value = NoOccupant;
    }

    private void ServerEject(SurvivorController survivor)
    {
        survivor.ServerSetHidden(false, transform.position + transform.forward * exitDistance + Vector3.up);
        occupantClientId.Value = NoOccupant;
    }

    private bool IsInRange(NetworkedCharacterMotor motor)
    {
        return (motor.transform.position - transform.position).sqrMagnitude <= interactRange * interactRange;
    }
}
