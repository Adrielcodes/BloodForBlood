using UnityEngine;
using UnityEngine.InputSystem;

// Windows, beds, counters, gurneys: press E while inside the trigger to hop to the far side.
// Deliberately NOT a NetworkBehaviour — vaulting is just owner-authoritative movement
// (NetworkedCharacterMotor.BeginVault), which OwnerNetworkTransform already replicates like any
// other movement, so nothing here needs to sync. Works for both roles; the Killer's own
// VaultDuration override makes it slower for them.
public class VaultableObstacle : MonoBehaviour
{
    // How far past the obstacle's centre plane the character lands, along this transform's
    // forward axis (the "across" direction). Beds/counters are wider than a window sill.
    [SerializeField] private float crossDistance = 1.1f;
    [SerializeField] private string prompt = "Press E to vault";

    private NetworkedCharacterMotor localCandidate;

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
        if (localCandidate == null) return;
        if (!localCandidate.CanCurrentlyAct) return;

        InteractionPromptController.Show(prompt);

        Keyboard kb = Keyboard.current;
        if (kb == null || !kb.eKey.wasPressedThisFrame) return;

        Vector3 toPlayer = localCandidate.transform.position - transform.position;
        float side = Mathf.Sign(Vector3.Dot(toPlayer, transform.forward));
        if (side == 0f) side = 1f;

        Vector3 target = transform.position - transform.forward * side * crossDistance;
        target.y = localCandidate.transform.position.y;

        InteractionPromptController.Hide();
        localCandidate.BeginVault(target);
    }
}
