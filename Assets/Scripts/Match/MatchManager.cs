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
