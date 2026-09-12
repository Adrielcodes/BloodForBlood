using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// This project's second in-scene-placed NetworkObject (after WeaponPickup) — auto-spawns with the
// scene, no NetworkPrefabsList entry needed. Server tracks every connected Survivor/Killer and
// evaluates win conditions whenever their combat state changes; Result is the one piece of state
// every client (not just the server) reacts to, since the match-end banner has to show for everyone.
public class MatchManager : NetworkBehaviour
{
    private readonly NetworkVariable<MatchResult> result = new NetworkVariable<MatchResult>(
        MatchResult.None,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly List<SurvivorController> survivors = new List<SurvivorController>();
    private KillerController killer;

    public NetworkVariable<MatchResult> Result => result;

    public override void OnNetworkSpawn()
    {
        result.OnValueChanged += HandleResultChanged;
        if (result.Value != MatchResult.None)
        {
            HandleResultChanged(MatchResult.None, result.Value);
        }

        if (!IsServer) return;

        NetworkManager.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;

        // Covers the host's own player object, which is already connected by the time this
        // in-scene object finishes spawning — OnClientConnectedCallback alone would miss it.
        foreach (var client in NetworkManager.ConnectedClients.Values)
        {
            RegisterPlayer(client.PlayerObject);
        }

        StartCoroutine(RandomizeObjectiveSpawns());
    }

    // The hospital map places more candidate spawn points than a match uses (8 beacon spots, 6
    // item spots); each match keeps a random subset and despawns the rest. Deferred one frame
    // because in-scene NetworkObjects all spawn during the same host-start pass this
    // OnNetworkSpawn runs in — not every candidate is guaranteed to be spawned yet on this frame.
    private System.Collections.IEnumerator RandomizeObjectiveSpawns()
    {
        yield return null;
        KeepRandomSubset(FindObjectsByType<RestoreBeacon>(FindObjectsSortMode.None), 4);
        KeepRandomSubset(FindObjectsByType<WeaponPickup>(FindObjectsSortMode.None), 3);
    }

    private static void KeepRandomSubset<T>(T[] candidates, int keep) where T : NetworkBehaviour
    {
        for (int i = candidates.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        for (int i = keep; i < candidates.Length; i++)
        {
            // Despawn(false) + the object's own OnNetworkDespawn -> SetActive(false) is the
            // established "consumed in-scene object" pattern (see WeaponPickup).
            if (candidates[i].IsSpawned) candidates[i].NetworkObject.Despawn(false);
            candidates[i].gameObject.SetActive(false);
        }
    }

    public override void OnNetworkDespawn()
    {
        result.OnValueChanged -= HandleResultChanged;

        if (!IsServer) return;

        NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
        NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
        {
            RegisterPlayer(client.PlayerObject);
        }
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        survivors.RemoveAll(survivor => survivor == null);
    }

    private void RegisterPlayer(NetworkObject playerObject)
    {
        if (playerObject == null) return;

        SurvivorController survivor = playerObject.GetComponent<SurvivorController>();
        if (survivor != null && !survivors.Contains(survivor))
        {
            survivors.Add(survivor);
            survivor.IsDowned.OnValueChanged += (previous, current) => CheckWinConditions();
        }

        KillerController newKiller = playerObject.GetComponent<KillerController>();
        if (newKiller != null)
        {
            killer = newKiller;
            killer.Health.OnValueChanged += (previous, current) => CheckWinConditions();
        }

        CheckWinConditions();
    }

    private void CheckWinConditions()
    {
        if (!IsServer || result.Value != MatchResult.None) return;

        if (killer != null && killer.Health.Value <= 0)
        {
            result.Value = MatchResult.SurvivorWin;
            return;
        }

        if (survivors.Count > 0 && survivors.TrueForAll(survivor => survivor.IsDowned.Value))
        {
            result.Value = MatchResult.KillerWin;
        }
    }

    private void HandleResultChanged(MatchResult previous, MatchResult current)
    {
        switch (current)
        {
            case MatchResult.KillerWin:
                MatchEndBanner.Show("Killer Wins");
                break;
            case MatchResult.SurvivorWin:
                MatchEndBanner.Show("Survivors Escape");
                break;
        }
    }
}
