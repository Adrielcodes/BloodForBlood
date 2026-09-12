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

    // Hospital map (see Assets/Editor/HospitalMapBuilder.cs): the Killer starts in the morgue at
    // the north end, Survivors in the south lobby — index 0 is always the Killer (first approved
    // connection, see ApprovalCheck). Duplicated here rather than referenced because the builder
    // is editor-only code.
    private static readonly Vector3 KillerSpawn = new Vector3(0f, 1f, 22f);
    private static readonly Vector3[] SurvivorSpawns =
    {
        new Vector3(-4.5f, 1f, -24f), new Vector3(-1.5f, 1f, -24f), new Vector3(1.5f, 1f, -24f), new Vector3(4.5f, 1f, -24f),
    };

    private Vector3 SpawnPositionFor(int index)
    {
        if (index == 0) return KillerSpawn;
        return SurvivorSpawns[(index - 1) % SurvivorSpawns.Length];
    }
}
