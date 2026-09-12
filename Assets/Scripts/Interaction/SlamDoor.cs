using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// A doorway anyone can open or close with E. The difference between roles is what a *closed*
// door means: a Survivor can open it again; the Killer can't — they have to break it (E, then
// `breakDuration` seconds while staying in range), after which it's gone for the rest of the
// match. Slamming it shut while the Killer is standing in the doorway stuns them (DBD
// pallet-stun equivalent). Closing is available to the Killer too, so they can shut a route
// behind a Survivor — but then it's a door they'd have to break to use themselves.
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
        isClosed.OnValueChanged += (previous, current) =>
        {
            ApplyVisualState();
            Sfx.PlayAt("DoorSlam", transform.position, current ? 1f : 0.45f, 0.08f, 35f);
        };
        isBroken.OnValueChanged += (previous, current) =>
        {
            ApplyVisualState();
            if (current) Sfx.PlayAt("DoorBreak", transform.position, 1f, 0.06f, 40f);
        };
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

        bool isKiller = localCandidate is KillerController;
        string prompt;
        if (!isClosed.Value) prompt = "Press E to close door";
        else if (!isKiller) prompt = "Press E to open door";
        else prompt = breakingClientId.HasValue ? "Breaking..." : "Press E to break door";
        InteractionPromptController.Show(prompt);

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

        if (!isClosed.Value)
        {
            isClosed.Value = true;
            if (caller is SurvivorController) StunKillersInDoorway();
            return;
        }

        if (caller is SurvivorController)
        {
            isClosed.Value = false;
        }
        else if (caller is KillerController && !breakingClientId.HasValue)
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
