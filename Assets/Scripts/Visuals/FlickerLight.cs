using UnityEngine;

// Dying fluorescent tube: Perlin-noise intensity wobble plus random brief blackouts. Purely
// local/cosmetic (not networked) — each client flickers on its own schedule, which is fine.
public class FlickerLight : MonoBehaviour
{
    [SerializeField] private float noiseSpeed = 9f;
    [SerializeField] private float dropoutsPerSecond = 0.15f;

    private Light cachedLight;
    private float baseIntensity;
    private float seed;
    private float dropUntil;

    private void Awake()
    {
        cachedLight = GetComponent<Light>();
        baseIntensity = cachedLight.intensity;
        seed = Random.value * 100f;
    }

    private void Update()
    {
        float t = Time.time;
        if (t < dropUntil)
        {
            cachedLight.intensity = 0f;
            return;
        }

        if (Random.value < dropoutsPerSecond * Time.deltaTime)
            dropUntil = t + Random.Range(0.04f, 0.35f);

        float n = Mathf.PerlinNoise(seed, t * noiseSpeed);
        cachedLight.intensity = baseIntensity * (0.65f + 0.45f * n);
    }
}
