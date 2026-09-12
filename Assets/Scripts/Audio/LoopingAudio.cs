using UnityEngine;

// Looping bed (menu music, hospital ambience) from Assets/Resources/Audio, configured by the
// scaffold tool. Scene-local: unloading the scene stops it, which is exactly the menu -> gameplay
// handoff we want.
public class LoopingAudio : MonoBehaviour
{
    [SerializeField] private string clipName = "Ambient";
    [SerializeField] private float volume = 0.7f;
    [SerializeField] private float fadeInSeconds = 2f;

    private AudioSource source;
    private float elapsed;

    private void Awake()
    {
        source = gameObject.AddComponent<AudioSource>();
        source.clip = Sfx.Clip(clipName);
        source.loop = true;
        source.spatialBlend = 0f;
        source.volume = 0f;
        if (source.clip != null) source.Play();
    }

    private void Update()
    {
        if (elapsed >= fadeInSeconds) return;
        elapsed += Time.deltaTime;
        source.volume = volume * Mathf.Clamp01(elapsed / fadeInSeconds);
    }
}
