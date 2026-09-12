using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Boarded-up shortcut only the Killer can open (E, then `breakDuration` seconds in range) — DBD
// breakable-wall equivalent. Survivors get no prompt; until it's broken it's just a wall to them.
// Same server-resolves-the-caller RPC convention as SlamDoor/WeaponPickup.
public class BreakableBarrier : NetworkBehaviour
{
    [SerializeField] private float interactRange = 2.3f;
    [SerializeField] private float breakDuration = 2f;

    private readonly NetworkVariable<bool> isBroken = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private GameObject boards;
    private KillerController localCandidate;
    private ulong? breakingClientId;
    private float breakStartTime;

    private void Awake()
    {
        Transform t = transform.Find("Boards");
        boards = t != null ? t.gameObject : null;
    }

    public override void OnNetworkSpawn()
    {
        isBroken.OnValueChanged += (previous, current) => ApplyVisualState();
        ApplyVisualState();
    }

    private void ApplyVisualState()
    {
        if (boards != null) boards.SetActive(!isBroken.Value);
    }

    private void OnTriggerEnter(Collider other)
    {
        var killer = other.GetComponentInParent<KillerController>();
        if (killer != null && killer.IsOwner) localCandidate = killer;
    }

    private void OnTriggerExit(Collider other)
    {
        var killer = other.GetComponentInParent<KillerController>();
        if (killer != null && killer == localCandidate)
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

        InteractionPromptController.Show(breakingClientId.HasValue ? "Breaking..." : "Press E to break barrier");

        Keyboard kb = Keyboard.current;
        if (kb != null && kb.eKey.wasPressedThisFrame) RequestBreakRpc();
    }

    private void ServerTickBreak()
    {
        if (!breakingClientId.HasValue) return;
        if (Time.time - breakStartTime < breakDuration) return;

        if (IsClientInRange(breakingClientId.Value)) isBroken.Value = true;
        breakingClientId = null;
    }

    [Rpc(SendTo.Server)]
    private void RequestBreakRpc(RpcParams rpcParams = default)
    {
        if (isBroken.Value || breakingClientId.HasValue) return;

        ulong sender = rpcParams.Receive.SenderClientId;
        if (!NetworkManager.ConnectedClients.TryGetValue(sender, out NetworkClient client)) return;
        if (client.PlayerObject == null || client.PlayerObject.GetComponent<KillerController>() == null) return;
        if (!IsClientInRange(sender)) return;

        breakingClientId = sender;
        breakStartTime = Time.time;
    }

    private bool IsClientInRange(ulong clientId)
    {
        if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client)) return false;
        if (client.PlayerObject == null) return false;
        return (client.PlayerObject.transform.position - transform.position).sqrMagnitude <= interactRange * interactRange;
    }
}
