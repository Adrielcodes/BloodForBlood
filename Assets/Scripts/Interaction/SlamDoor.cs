using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// A doorway a Survivor can slam shut (E toggles it open/closed for Survivors) to block a chase.
// The Killer can't open it — they have to break it (E, then `breakDuration` seconds while staying
// in range), after which it's gone for the rest of the match. Slamming it while the Killer is
// standing in the doorway stuns them (DBD pallet-stun equivalent).
//
// In-scene NetworkObject with no client owner: same RPC convention as WeaponPickup/RestoreBeacon —
// [Rpc(SendTo.Server)] with the server resolving the real caller from SenderClientId.
public class SlamDoor : NetworkBehaviour
{
    [SerializeField] private float interactRange = 2.3f;
    [SerializeField] private float breakDuration = 1.5f;
    [SerializeField] private float stunRadius = 1.4f;
    [SerializeField] private float stunSeconds = 2.5f;

    private readonly NetworkVariable<bool> isClosed = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> isBroken = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Transform hinge;
    private Collider panelCollider;
    private NetworkedCharacterMotor localCandidate;

    private ulong? breakingClientId;
    private float breakStartTime;

    private void Awake()
    {
        hinge = transform.Find("Hinge");
        if (hinge != null) panelCollider = hinge.GetComponentInChildren<Collider>();
    }

    public override void OnNetworkSpawn()
    {
        isClosed.OnValueChanged += (previous, current) => ApplyVisualState();
        isBroken.OnValueChanged += (previous, current) => ApplyVisualState();
        ApplyVisualState();
    }

    private void ApplyVisualState()
    {
        if (hinge == null) return;
        hinge.gameObject.SetActive(!isBroken.Value);
        hinge.localRotation = isClosed.Value ? Quaternion.identity : Quaternion.Euler(0f, 100f, 0f);
        if (panelCollider != null) panelCollider.enabled = isClosed.Value;
    }

    private void OnTriggerEnter(Collider other)
    {
        var motor = other.GetComponentInParent<NetworkedCharacterMotor>();
        if (motor != null && motor.IsOwner) localCandidate = motor;
    }

    private void OnTriggerExit(Collider other)
    {
        var motor = other.GetComponentInParent<NetworkedCharacterMotor>();
        if (motor != null && motor == localCandidate)
        {
            localCandidate = null;
            InteractionPromptController.Hide();
        }
    }

    private void Update()
    {
        if (IsServer) ServerTickBreak();

        if (localCandidate == null || !IsSpawned || isBroken.Value) return;
        if (!localCandidate.CanCurrentlyAct) return;

        bool isSurvivor = localCandidate is SurvivorController;
        if (isSurvivor)
            InteractionPromptController.Show(isClosed.Value ? "Press E to open door" : "Press E to slam door");
        else if (isClosed.Value)
            InteractionPromptController.Show(breakingClientId.HasValue ? "Breaking..." : "Press E to break door");
        else
            return;

        Keyboard kb = Keyboard.current;
        if (kb != null && kb.eKey.wasPressedThisFrame) RequestInteractRpc();
    }

    private void ServerTickBreak()
    {
        if (!breakingClientId.HasValue) return;
        if (Time.time - breakStartTime < breakDuration) return;

        if (IsClientInRange(breakingClientId.Value))
        {
            isBroken.Value = true;
            isClosed.Value = false;
        }
        breakingClientId = null;
    }

    [Rpc(SendTo.Server)]
    private void RequestInteractRpc(RpcParams rpcParams = default)
    {
        if (isBroken.Value) return;

        ulong sender = rpcParams.Receive.SenderClientId;
        NetworkedCharacterMotor caller = ResolveCaller(sender);
        if (caller == null || !IsClientInRange(sender)) return;

        if (caller is SurvivorController)
        {
            bool closing = !isClosed.Value;
            isClosed.Value = closing;
            if (closing) StunKillersInDoorway();
        }
        else if (caller is KillerController && isClosed.Value && !breakingClientId.HasValue)
        {
            breakingClientId = sender;
            breakStartTime = Time.time;
        }
    }

    private void StunKillersInDoorway()
    {
        foreach (NetworkClient client in NetworkManager.ConnectedClients.Values)
        {
            if (client.PlayerObject == null) continue;
            KillerController killer = client.PlayerObject.GetComponent<KillerController>();
            if (killer == null) continue;

            Vector3 flat = killer.transform.position - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude <= stunRadius * stunRadius) killer.ServerApplyStun(stunSeconds);
        }
    }

    private NetworkedCharacterMotor ResolveCaller(ulong clientId)
    {
        if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client)) return null;
        return client.PlayerObject != null ? client.PlayerObject.GetComponent<NetworkedCharacterMotor>() : null;
    }

    private bool IsClientInRange(ulong clientId)
    {
        NetworkedCharacterMotor caller = ResolveCaller(clientId);
        if (caller == null) return false;
        return (caller.transform.position - transform.position).sqrMagnitude <= interactRange * interactRange;
    }
}
