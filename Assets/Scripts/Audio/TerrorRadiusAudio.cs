using UnityEngine;

// DBD-style terror radius: the local Survivor hears a heartbeat that gets louder and faster as the
// nearest Killer closes in. Added at runtime by SurvivorController for the owner only. Pitch is
// used for tempo (it also raises the thump's pitch slightly, which reads as urgency anyway).
public class TerrorRadiusAudio : MonoBehaviour
{
    [SerializeField] private float radius = 26f;

    private AudioSource source;
    private KillerController[] killers = System.Array.Empty<KillerController>();
    private float nextScan;

    private void Awake()
    {
        source = gameObject.AddComponent<AudioSource>();
        source.clip = Sfx.Clip("Heartbeat");
        source.loop = true;
        source.spatialBlend = 0f;
        source.volume = 0f;
        if (source.clip != null) source.Play();
    }

    private void Update()
    {
        if (Time.time >= nextScan)
        {
            killers = FindObjectsByType<KillerController>(FindObjectsSortMode.None);
            nextScan = Time.time + 0.5f;
        }

        float nearest = float.MaxValue;
        foreach (KillerController k in killers)
            nearest = Mathf.Min(nearest, (k.transform.position - transform.position).magnitude);

        float closeness = nearest >= radius ? 0f : 1f - nearest / radius;
        float target = Mathf.Pow(closeness, 1.4f) * 0.9f;
        source.volume = Mathf.MoveTowards(source.volume, target, Time.deltaTime * 0.8f);
        source.pitch = 0.85f + 0.75f * closeness;
    }
}
