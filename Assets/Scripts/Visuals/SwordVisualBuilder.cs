using UnityEngine;

// Runtime-safe placeholder sword geometry (primitive parts, flat-color materials) — used both for
// the in-hand equip visuals (KillerController always-equipped, SurvivorController once armed) and,
// via NetworkScaffoldSetup's editor-only BuildSwordVisual, the WeaponPickup world model. Kept out
// of Assets/Editor (runtime code can't reference editor-only assemblies) so gameplay scripts can
// call it directly.
public static class SwordVisualBuilder
{
    public static GameObject Build(Transform parent, string name = "Sword")
    {
        var visual = new GameObject(name);
        visual.transform.SetParent(parent, false);

        Material bladeMat = CreateColorMaterial("BladeMat", new Color(0.75f, 0.76f, 0.78f));
        Material guardMat = CreateColorMaterial("GuardMat", new Color(0.55f, 0.46f, 0.16f));
        Material hiltMat = CreateColorMaterial("HiltMat", new Color(0.35f, 0.22f, 0.12f));

        CreatePart(PrimitiveType.Cube, visual.transform, bladeMat, "Blade",
            new Vector3(0f, 0.55f, 0f), new Vector3(0.05f, 0.7f, 0.02f));
        CreatePart(PrimitiveType.Cube, visual.transform, guardMat, "Guard",
            new Vector3(0f, 0.18f, 0f), new Vector3(0.24f, 0.04f, 0.04f));
        CreatePart(PrimitiveType.Cylinder, visual.transform, hiltMat, "Handle",
            new Vector3(0f, 0.08f, 0f), new Vector3(0.035f, 0.06f, 0.035f));
        CreatePart(PrimitiveType.Sphere, visual.transform, guardMat, "Pommel",
            Vector3.zero, new Vector3(0.07f, 0.07f, 0.07f));

        return visual;
    }

    private static void CreatePart(PrimitiveType type, Transform parent, Material material, string name,
        Vector3 localPosition, Vector3 localScale)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;

        Collider collider = part.GetComponent<Collider>();
        if (collider != null)
        {
            // DestroyImmediate is required in Edit mode (the editor scaffold tool calls this too,
            // outside Play mode) — Destroy silently no-ops there.
            if (Application.isPlaying) Object.Destroy(collider);
            else Object.DestroyImmediate(collider);
        }

        Renderer renderer = part.GetComponent<Renderer>();
        if (renderer != null) renderer.sharedMaterial = material;
    }

    private static Material CreateColorMaterial(string name, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        return new Material(shader) { name = name, color = color };
    }
}
