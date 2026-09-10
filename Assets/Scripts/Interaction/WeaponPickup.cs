using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class WeaponPickup : NetworkBehaviour
{
    [SerializeField] private float interactRange = 1.5f;

    private readonly NetworkVariable<bool> isClaimed = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private SurvivorController localCandidate;

    private void OnTriggerEnter(Collider other)
    {
        var survivor = other.GetComponentInParent<SurvivorController>();
        if (survivor != null && survivor.IsOwner)
        {
            localCandidate = survivor;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        var survivor = other.GetComponentInParent<SurvivorController>();
        if (survivor != null && survivor == localCandidate)
        {
            localCandidate = null;
        }
    }

    private void Update()
    {
        if (!IsSpawned || isClaimed.Value || localCandidate == null) return;

        Keyboard kb = Keyboard.current;
        if (kb != null && kb.eKey.wasPressedThisFrame)
        {
            RequestPickupRpc();
        }
    }

    [Rpc(SendTo.Server)]
    private void RequestPickupRpc(RpcParams rpcParams = default)
    {
        if (isClaimed.Value) return;

        if (!NetworkManager.ConnectedClients.TryGetValue(rpcParams.Receive.SenderClientId, out NetworkClient client)) return;
        SurvivorController survivor = client.PlayerObject != null ? client.PlayerObject.GetComponent<SurvivorController>() : null;
        if (survivor == null) return;

        float distSq = (survivor.transform.position - transform.position).sqrMagnitude;
        if (distSq > interactRange * interactRange) return;

        isClaimed.Value = true;
        survivor.ServerGrantWeapon();
        NetworkObject.Despawn(false);
    }

    public override void OnNetworkDespawn()
    {
        gameObject.SetActive(false);
        base.OnNetworkDespawn();
    }
}
