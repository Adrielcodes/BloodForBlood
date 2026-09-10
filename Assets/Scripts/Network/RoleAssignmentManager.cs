using Unity.Netcode;
using UnityEngine;

public class RoleAssignmentManager : MonoBehaviour
{
    [SerializeField] private GameObject survivorPrefab;
    [SerializeField] private GameObject killerPrefab;

    private bool killerAssigned;
    private ulong killerClientId;
    private int nextSpawnIndex;
    private uint killerPrefabHash;

    public GameObject SurvivorPrefab => survivorPrefab;
    public GameObject KillerPrefab => killerPrefab;

    public void Configure(GameObject survivor, GameObject killer)
    {
        survivorPrefab = survivor;
        killerPrefab = killer;
    }

    private void Start()
    {
        NetworkManager nm = NetworkManager.Singleton;

        killerPrefabHash = new NetworkPrefab { Prefab = killerPrefab }.SourcePrefabGlobalObjectIdHash;

        nm.NetworkConfig.ConnectionApproval = true;
        nm.ConnectionApprovalCallback = ApprovalCheck;
        nm.OnClientDisconnectCallback += OnClientDisconnect;
    }

    private void ApprovalCheck(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        response.Approved = true;
        response.CreatePlayerObject = true;
        response.Position = SpawnPositionFor(nextSpawnIndex);
        response.Rotation = Quaternion.identity;

        if (!killerAssigned)
        {
            killerAssigned = true;
            killerClientId = request.ClientNetworkId;
            response.PlayerPrefabHash = killerPrefabHash;
        }

        nextSpawnIndex++;
        response.Pending = false;
    }

    private void OnClientDisconnect(ulong clientId)
    {
        if (killerAssigned && clientId == killerClientId)
        {
            killerAssigned = false;
        }
    }

    private Vector3 SpawnPositionFor(int index)
    {
        return new Vector3(index * 3f, 1f, 0f);
    }
}
