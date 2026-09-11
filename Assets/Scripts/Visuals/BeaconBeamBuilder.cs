using UnityEngine;

// Built and hidden once at runtime by RestoreBeacon.Awake() (not the editor scaffold tool — never
// saved into a prefab/scene, so unlike SwordVisualBuilder this never needs PersistRuntimeMaterials;
// a plain in-memory Material is fine for something that only ever lives for the length of a Play
// session). RestoreBeacon.SetActive(true)s the returned root once IsActivated flips true.
public static class BeaconBeamBuilder
{
    public static GameObject Build(Transform parent)
    {
        var root = new GameObject("BeaconBeam");
        root.transform.SetParent(parent, false);

        GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beam.name = "BeamCore";
        beam.transform.SetParent(root.transform, false);
        Object.Destroy(beam.GetComponent<Collider>());
        beam.transform.localPosition = new Vector3(0f, 40f, 0f);
        beam.transform.localScale = new Vector3(0.4f, 40f, 0.4f);

        Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Universal Render Pipeline/Lit");
        var beamMat = new Material(unlitShader) { color = new Color(0.85f, 0.92f, 1f, 1f) };
        beam.GetComponent<Renderer>().sharedMaterial = beamMat;

        // A real Light pointed straight up gives the beam actual scene illumination on top of the
        // emissive-looking mesh above, rather than just a bright shape with no light-feel to it.
        var lightGO = new GameObject("BeamLight");
        lightGO.transform.SetParent(root.transform, false);
        lightGO.transform.localPosition = new Vector3(0f, 1f, 0f);
        lightGO.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        Light beamLight = lightGO.AddComponent<Light>();
        beamLight.type = LightType.Spot;
        beamLight.color = new Color(0.85f, 0.92f, 1f);
        beamLight.intensity = 8f;
        beamLight.range = 60f;
        beamLight.spotAngle = 25f;

        root.AddComponent<BeaconBeamSpin>();

        root.SetActive(false);
        return root;
    }
}
