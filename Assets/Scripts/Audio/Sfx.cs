using System.Collections.Generic;
using UnityEngine;

// One-shot playback for the procedurally generated clips in Assets/Resources/Audio (see
// ProceduralAudio). Fire-and-forget: each call spawns a throwaway AudioSource that destroys itself
// when the clip ends. Purely local — callers play these from replicated state changes
// (NetworkVariable OnValueChanged, replicated movement), so every client hears its own copy.
public static class Sfx
{
    private static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();

    public static AudioClip Clip(string name)
    {
        if (!cache.TryGetValue(name, out AudioClip clip))
        {
            clip = Resources.Load<AudioClip>("Audio/" + name);
            cache[name] = clip;
        }
        return clip;
    }

    public static void PlayAt(string name, Vector3 position, float volume = 1f, float pitchJitter = 0.08f, float maxDistance = 30f)
    {
        AudioSource src = Spawn(name, volume, pitchJitter);
        if (src == null) return;
        src.transform.position = position;
        src.spatialBlend = 1f;
        src.minDistance = 1.5f;
        src.maxDistance = maxDistance;
        src.rolloffMode = AudioRolloffMode.Logarithmic;
        src.Play();
    }

    public static void Play2D(string name, float volume = 1f, float pitchJitter = 0.03f)
    {
        AudioSource src = Spawn(name, volume, pitchJitter);
        if (src == null) return;
        src.spatialBlend = 0f;
        src.Play();
    }

    private static AudioSource Spawn(string name, float volume, float pitchJitter)
    {
        AudioClip clip = Clip(name);
        if (clip == null) return null;
        var go = new GameObject("Sfx_" + name);
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.volume = volume;
        src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        Object.Destroy(go, clip.length / src.pitch + 0.1f);
        return src;
    }
}
