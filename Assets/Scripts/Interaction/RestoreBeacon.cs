using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Long-interaction Survivor objective: hold E near the beacon for `restoreDuration` seconds of
// continuous interaction (interrupted by releasing E, moving out of range, or getting downed) to
// activate it. While held, random skill checks (SkillCheckController) fire on the interacting
// client's own screen — missing one stops the interaction and costs progress. On completion the
// beacon fires a beam of light into the sky (BeaconBeamBuilder), identically on every client.
//
// Server-authoritative progress, same trust model as WeaponPickup: this NetworkObject has no
// client owner (server-spawned/in-scene), so its RPCs use [Rpc(SendTo.Server)] with no
// InvokePermission restriction, and the server resolves the *actual* calling player itself via
// rpcParams.Receive.SenderClientId rather than trusting a client-supplied reference.
public class RestoreBeacon : NetworkBehaviour
{
    [SerializeField] private float interactRange = 2.5f;
    [SerializeField] private float restoreDuration = 20f;
    [SerializeField] private float skillCheckMinInterval = 3f;
    [SerializeField] private float skillCheckMaxInterval = 6f;
    [SerializeField] private float skillCheckPenalty = 0.15f;

    private readonly NetworkVariable<float> progress = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> isActivated = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private ulong? activeInteractorClientId;

    private GameObject beamVisual;
    private SurvivorController localCandidate;
    private bool isInteracting;
    private Coroutine skillCheckRoutine;

    public NetworkVariable<float> Progress => progress;
    public NetworkVariable<bool> IsActivated => isActivated;

    private void Awake()
    {
        beamVisual = BeaconBeamBuilder.Build(transform);
    }

    public override void OnNetworkSpawn()
    {
        isActivated.OnValueChanged += HandleActivatedChanged;
        if (isActivated.Value) HandleActivatedChanged(false, true);
    }

    public override void OnNetworkDespawn()
    {
        isActivated.OnValueChanged -= HandleActivatedChanged;
        // Unused candidate spawn points get despawned by MatchManager at match start.
        gameObject.SetActive(false);
    }

    private void HandleActivatedChanged(bool previous, bool current)
    {
        beamVisual.SetActive(current);
        if (current) InteractionPromptController.Hide();
    }

    private void OnTriggerEnter(Collider other)
    {
        SurvivorController survivor = other.GetComponentInParent<SurvivorController>();
        if (survivor != null && survivor.IsOwner)
        {
            localCandidate = survivor;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        SurvivorController survivor = other.GetComponentInParent<SurvivorController>();
        if (survivor != null && survivor == localCandidate)
        {
            localCandidate = null;
            InteractionPromptController.Hide();
            StopInteracting();
        }
    }

    private void Update()
    {
        if (IsServer) ServerTickProgress();

        if (localCandidate == null || !IsSpawned) return;

        if (isActivated.Value || localCandidate.IsDowned.Value)
        {
            if (isInteracting) StopInteracting();
            return;
        }

        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        if (kb.eKey.wasPressedThisFrame && !isInteracting)
        {
            StartInteracting();
        }
        else if (kb.eKey.wasReleasedThisFrame && isInteracting)
        {
            StopInteracting();
        }

        InteractionPromptController.Show(isInteracting
            ? $"Restoring Beacon... {Mathf.RoundToInt(progress.Value * 100f)}%"
            : "Hold E to Restore Beacon");
    }

    // Runs on the server only, every frame, for every beacon — cheap (a float compare + add), and
    // simpler than driving progress from client-reported deltas (which would need per-tick RPCs).
    private void ServerTickProgress()
    {
        if (!activeInteractorClientId.HasValue || isActivated.Value) return;

        progress.Value = Mathf.Min(1f, progress.Value + Time.deltaTime / restoreDuration);
        if (progress.Value >= 1f)
        {
            isActivated.Value = true;
            activeInteractorClientId = null;
        }
    }

    private void StartInteracting()
    {
        isInteracting = true;
        RequestStartInteractingRpc();
        skillCheckRoutine = StartCoroutine(SkillCheckLoop());
    }

    private void StopInteracting()
    {
        if (!isInteracting) return;

        isInteracting = false;
        RequestStopInteractingRpc();

        if (skillCheckRoutine != null)
        {
            StopCoroutine(skillCheckRoutine);
            skillCheckRoutine = null;
        }
    }

    private IEnumerator SkillCheckLoop()
    {
        while (isInteracting)
        {
            yield return new WaitForSeconds(Random.Range(skillCheckMinInterval, skillCheckMaxInterval));
            if (!isInteracting) yield break;

            bool success = false;
            yield return SkillCheckController.Run(result => success = result);

            if (!isInteracting) yield break;

            if (!success)
            {
                ReportSkillCheckFailedRpc();
                StopInteracting();
                yield break;
            }
        }
    }

    [Rpc(SendTo.Server)]
    private void RequestStartInteractingRpc(RpcParams rpcParams = default)
    {
        if (isActivated.Value || activeInteractorClientId.HasValue) return;
        if (!IsCallerInRange(rpcParams.Receive.SenderClientId)) return;

        activeInteractorClientId = rpcParams.Receive.SenderClientId;
    }

    [Rpc(SendTo.Server)]
    private void RequestStopInteractingRpc(RpcParams rpcParams = default)
    {
        if (activeInteractorClientId == rpcParams.Receive.SenderClientId)
            activeInteractorClientId = null;
    }

    [Rpc(SendTo.Server)]
    private void ReportSkillCheckFailedRpc(RpcParams rpcParams = default)
    {
        if (activeInteractorClientId != rpcParams.Receive.SenderClientId) return;
        progress.Value = Mathf.Max(0f, progress.Value - skillCheckPenalty);
    }

    private bool IsCallerInRange(ulong clientId)
    {
        if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client)) return false;
        if (client.PlayerObject == null) return false;

        float distSq = (client.PlayerObject.transform.position - transform.position).sqrMagnitude;
        return distSq <= interactRange * interactRange;
    }
}
