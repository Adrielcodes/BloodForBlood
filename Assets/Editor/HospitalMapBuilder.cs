using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Builds the abandoned-hospital gameplay map: a 56x56 two-storey layout designed around DBD-style
// chase loops. Every room has at least two ways in/out (a door plus a vaultable window, or two
// doors), so no corridor is a dead end; the second floor is a U-shaped mezzanine whose railings
// have gaps you can drop through into the central corridor or lobby. Static geometry lives under
// one versioned root ("HospitalMap#N" — bumping Version rebuilds it on the next Setup); the
// networked interactables (SlamDoor, BreakableBarrier, HidingLocker) are prefab instances at
// scene root like every other in-scene NetworkObject here. All coordinates are world units,
// origin at the map centre, z+ = north.
//
// Floor plan (ground): south lobby (Survivor spawns) -> lobby wall z=-14 -> west wing (3 wards) /
// central corridor / east wing (pharmacy, locker room, operating room) -> wall z=14 -> north
// wing (morgue = Killer spawn, flanked by two rooms). Two ramps ("staircases") reach the second
// floor: one in the lobby's west corner, one in the north wing's east corner. A roof closes the
// whole footprint; the lobby and north wing are double-height atriums under it.
//
// Surfaces are procedurally textured (ProceduralTextures) with normal maps and tiled per object
// by AutoTile; fluorescent fixtures flicker (FlickerLight); blood decals, drifting dust and floor
// fog do the rest of the "abandoned" read.
internal static class HospitalMapBuilder
{
    private const int Version = 6;
    private static string RootName => $"HospitalMap#{Version}";

    private const float FloorHeight = 4f;
    private const float SlabTopY = 4.15f;
    private const float SlabThickness = 0.3f;
    private const float RoofY = 8.3f;
    private const float WallT = 0.3f;
    private const float WindowWidth = 1.6f;
    private const string MaterialsDir = "Assets/Art/Environment/Materials";
    private const string PrefabDir = "Assets/Prefabs";

    private static Material wallMat, floorMat, ceilingMat, concreteMat, railMat, metalMat, woodMat, sheetMat, tubeMat, bloodMat, dustMat, fogMat;

    public static readonly Vector3 KillerSpawn = new Vector3(0f, 1f, 22f);
    public static readonly Vector3[] SurvivorSpawns =
    {
        new Vector3(-4.5f, 1f, -24f), new Vector3(-1.5f, 1f, -24f), new Vector3(1.5f, 1f, -24f), new Vector3(4.5f, 1f, -24f),
    };

    // 8 candidates, MatchManager keeps 4 per match. y = floor surface the beacon stands on.
    private static readonly Vector3[] BeaconSpawns =
    {
        new Vector3(-25f, 0f, -6f), new Vector3(25f, 0f, -6f), new Vector3(-6f, 0f, 20f), new Vector3(21f, 0f, 24f),
        new Vector3(-14f, 0f, -22f), new Vector3(24f, 0f, 12f), new Vector3(-25f, SlabTopY, -12f), new Vector3(24f, SlabTopY, 12f),
    };

    // 6 candidates, MatchManager keeps 3 per match. y = 1 above the floor (the pickup bobs there).
    private static readonly Vector3[] PickupSpawns =
    {
        new Vector3(-6f, 1f, -21f), new Vector3(14f, 1f, 3f), new Vector3(-16f, 1f, 13f),
        new Vector3(6f, 1f, 26f), new Vector3(-22f, SlabTopY + 1f, 4f), new Vector3(22f, SlabTopY + 1f, -6f),
    };

    public static void Build()
    {
        CreateMaterials();

        GameObject ground = GameObject.Find("Ground");
        if (ground != null)
        {
            ground.transform.localScale = new Vector3(7f, 1f, 7f);
            ground.GetComponent<Renderer>().sharedMaterial = floorMat;
            AutoTile tile = ground.GetComponent<AutoTile>() ?? ground.AddComponent<AutoTile>();
            var so = new SerializedObject(tile);
            so.FindProperty("unitScale").floatValue = 10f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // Interactables are re-created every run (prefabs overwritten in place) so material
        // upgrades reach existing scene instances; placement itself is skipped if present.
        PlaceNetworkedInteractables();

        if (GameObject.Find(RootName) != null)
        {
            Debug.Log($"Blood For Blood: {RootName} already present in scene, skipping geometry.");
            return;
        }

        foreach (GameObject go in SceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == "EnvironmentDressing" || go.name.StartsWith("HospitalMap")) Object.DestroyImmediate(go);

        // First build on a scene that still has the old open-field objective placements: clear
        // them so PlaceObjectives re-places everything at the hospital spawn points.
        if (GameObject.Find("HospitalMap") == null)
        {
            foreach (var b in Object.FindObjectsByType<RestoreBeacon>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.DestroyImmediate(b.gameObject);
            foreach (var p in Object.FindObjectsByType<WeaponPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);
        }

        var root = new GameObject(RootName).transform;
        BuildGroundFloor(root);
        BuildSecondFloor(root);
        BuildRoof(root);
        BuildRamps(root);
        BuildFurniture(root);
        BuildElevatorShaft(root);
        BuildLights(root);
        BuildDecals(root);
        BuildAtmosphere(root);

        NetworkScaffoldSetup.PersistRuntimeMaterials(root.gameObject, MaterialsDir);

        // ~450 static primitives: batch them so the map is a handful of draw calls, not hundreds.
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.GetComponent<Renderer>() != null && t.GetComponent<ParticleSystem>() == null)
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
    }

    public static void PlaceObjectives(GameObject beaconPrefab, GameObject pickupPrefab)
    {
        if (Object.FindFirstObjectByType<RestoreBeacon>(FindObjectsInactive.Include) == null)
            foreach (Vector3 pos in BeaconSpawns) Place(beaconPrefab, pos, 0f);

        if (Object.FindFirstObjectByType<WeaponPickup>(FindObjectsInactive.Include) == null)
            foreach (Vector3 pos in PickupSpawns) Place(pickupPrefab, pos, 0f);
    }

    // ---------------------------------------------------------------- materials

    private static void CreateMaterials()
    {
        wallMat = Textured("HospitalWallMat", ProceduralTextures.Wall(), Color.white, 0.18f, 0f);
        floorMat = Textured("HospitalFloorMat", ProceduralTextures.FloorTile(), Color.white, 0.35f, 0f);
        ceilingMat = Textured("HospitalCeilingMat", ProceduralTextures.Ceiling(), Color.white, 0.1f, 0f);
        concreteMat = Textured("HospitalConcreteMat", ProceduralTextures.Concrete(), Color.white, 0.12f, 0f);
        railMat = Textured("HospitalRailMat", ProceduralTextures.Metal(), new Color(0.55f, 0.55f, 0.58f), 0.5f, 0.7f);
        metalMat = Textured("HospitalMetalMat", ProceduralTextures.Metal(), Color.white, 0.45f, 0.6f);
        woodMat = Textured("HospitalWoodMat", ProceduralTextures.Wood(), Color.white, 0.25f, 0f);
        sheetMat = Textured("HospitalSheetMat", ProceduralTextures.Concrete(), new Color(0.78f, 0.74f, 0.66f), 0.05f, 0f);
        tubeMat = Emissive("HospitalTubeMat", new Color(0.75f, 0.95f, 0.8f), new Color(0.8f, 1f, 0.85f) * 2.5f);
        bloodMat = Cutout("HospitalBloodMat", ProceduralTextures.BloodSplat(), 0.55f);
        dustMat = Particle("HospitalDustMat", ProceduralTextures.SoftParticle());
        fogMat = Particle("HospitalFogMat", ProceduralTextures.SoftParticle());
    }

    // Persisted URP/Lit with albedo + normal map, created or updated in place by name — these
    // are real .mat assets from the start, so PersistRuntimeMaterials leaves them alone.
    internal static Material Textured(string name, ProceduralTextures.Surface surface, Color tint, float smoothness, float metallic)
    {
        Material m = LoadOrCreate(name, "Universal Render Pipeline/Lit");
        m.SetTexture("_BaseMap", surface.Albedo);
        m.SetColor("_BaseColor", tint);
        m.SetTexture("_BumpMap", surface.Normal);
        m.SetFloat("_BumpScale", 1f);
        m.EnableKeyword("_NORMALMAP");
        m.SetFloat("_Smoothness", smoothness);
        m.SetFloat("_Metallic", metallic);
        EditorUtility.SetDirty(m);
        return m;
    }

    internal static Material Emissive(string name, Color baseColor, Color emission)
    {
        Material m = LoadOrCreate(name, "Universal Render Pipeline/Lit");
        m.SetColor("_BaseColor", baseColor);
        m.SetColor("_EmissionColor", emission);
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(m);
        return m;
    }

    // Alpha-clipped decal rather than alpha-blended: cutout needs no blend/queue juggling and
    // sorts like opaque geometry, which is all a floor splat needs.
    internal static Material Cutout(string name, Texture2D texture, float smoothness)
    {
        Material m = LoadOrCreate(name, "Universal Render Pipeline/Lit");
        m.SetTexture("_BaseMap", texture);
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_AlphaClip", 1f);
        m.SetFloat("_Cutoff", 0.5f);
        m.EnableKeyword("_ALPHATEST_ON");
        m.SetOverrideTag("RenderType", "TransparentCutout");
        m.renderQueue = 2450;
        m.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(m);
        return m;
    }

    // `additive` for fire/embers (light adds up); default alpha blend for mist, dust and smoke.
    internal static Material Particle(string name, Texture2D texture, bool additive = false)
    {
        Material m = LoadOrCreate(name, "Universal Render Pipeline/Particles/Unlit");
        m.SetTexture("_BaseMap", texture);
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", additive ? 2f : 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)(additive ? UnityEngine.Rendering.BlendMode.SrcAlpha : UnityEngine.Rendering.BlendMode.SrcAlpha));
        m.SetInt("_DstBlend", (int)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = 3000;
        EditorUtility.SetDirty(m);
        return m;
    }

    private static Material LoadOrCreate(string name, string shaderName)
    {
        if (!AssetDatabase.IsValidFolder(MaterialsDir)) AssetDatabase.CreateFolder("Assets/Art/Environment", "Materials");
        string path = $"{MaterialsDir}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find(shaderName);
        if (m == null)
        {
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        else if (m.shader != shader)
        {
            m.shader = shader;
        }
        return m;
    }

    // ---------------------------------------------------------------- ground floor

    private static void BuildGroundFloor(Transform root)
    {
        Transform floor = Group(root, "GroundFloor");
        const float H = FloorHeight;

        Wall(floor, wallMat, V(-28, -28), V(28, -28), RoofY, 0f, "ExteriorS");
        Wall(floor, wallMat, V(28, -28), V(28, 28), RoofY, 0f, "ExteriorE");
        Wall(floor, wallMat, V(28, 28), V(-28, 28), RoofY, 0f, "ExteriorN");
        Wall(floor, wallMat, V(-28, 28), V(-28, -28), RoofY, 0f, "ExteriorW");

        // Lobby / wings divider (z = -14): corridor mouth, a door west, a boarded shortcut east,
        // and a window either side of the corridor.
        WallWithGaps(floor, wallMat, V(-28, -14), V(28, -14), H, 0f, (28f, 4f), (8f, 2f), (48f, 2f), (18f, WindowWidth), (38f, WindowWidth));
        Window(floor, V(-10, -14), true, 0f);
        Window(floor, V(10, -14), true, 0f);

        // Central corridor walls (x = ±3), three doors each plus one window each for loops.
        WallWithGaps(floor, wallMat, V(-3, -14), V(-3, 14), H, 0f, (5f, 2f), (15f, 2f), (24f, 2f), (19f, WindowWidth));
        Window(floor, V(-3, 5), false, 0f);
        WallWithGaps(floor, wallMat, V(3, -14), V(3, 14), H, 0f, (5f, 2f), (15f, 2f), (24f, 2f), (11f, WindowWidth));
        Window(floor, V(3, -3), false, 0f);

        // West wing: three wards separated by walls at z = -4 and z = 6.
        WallWithGaps(floor, wallMat, V(-28, -4), V(-3, -4), H, 0f, (13f, WindowWidth), (22f, 2f));
        Window(floor, V(-15, -4), true, 0f);
        WallWithGaps(floor, wallMat, V(-28, 6), V(-3, 6), H, 0f, (8f, WindowWidth), (20f, 2f));
        Window(floor, V(-20, 6), true, 0f);

        // East wing: pharmacy (south), locker room (middle), operating room (north).
        WallWithGaps(floor, wallMat, V(3, -4), V(28, -4), H, 0f, (12f, WindowWidth), (19f, 2f));
        Window(floor, V(15, -4), true, 0f);
        WallWithGaps(floor, wallMat, V(3, 6), V(28, 6), H, 0f, (5f, 2f), (19f, WindowWidth));
        Window(floor, V(22, 6), true, 0f);

        // Wings / north wing divider (z = 14). The east ramp passes through at x = 25.5.
        WallWithGaps(floor, wallMat, V(-28, 14), V(28, 14), H, 0f, (28f, 4f), (8f, 2f), (48f, 2f), (20f, WindowWidth), (36f, WindowWidth), (53.5f, 3.5f));
        Window(floor, V(-8, 14), true, 0f);
        Window(floor, V(8, 14), true, 0f);

        // North wing: morgue in the middle, a room either side.
        WallWithGaps(floor, wallMat, V(-14, 14), V(-14, 28), H, 0f, (7f, 2f));
        WallWithGaps(floor, wallMat, V(14, 14), V(14, 28), H, 0f, (7f, 2f));

        // Short freestanding loop walls inside the bigger rooms — something to run around.
        Wall(floor, wallMat, V(-16, -12), V(-16, -8), H, 0f, "LoopWall");
        Wall(floor, wallMat, V(18, 8), V(18, 12), H, 0f, "LoopWall");
        Wall(floor, wallMat, V(-4, 18), V(4, 18), H, 0f, "LoopWall");
    }

    // ---------------------------------------------------------------- second floor + roof

    private static void BuildSecondFloor(Transform root)
    {
        Transform upper = Group(root, "SecondFloor");
        const float H = FloorHeight;
        const float B = SlabTopY;

        Slab(upper, V(-15.5f, 0f), new Vector2(25f, 28f), "WestSlab");
        Slab(upper, V(15.5f, 0f), new Vector2(25f, 28f), "EastSlab");
        Slab(upper, V(0f, 12f), new Vector2(6f, 4f), "NorthBridge");
        Slab(upper, V(0f, -16f), new Vector2(28f, 4f), "LobbyBalcony");

        WallWithGaps(upper, wallMat, V(-28, -4), V(-3, -4), H, B, (16f, WindowWidth), (6f, 2f));
        Window(upper, V(-12, -4), true, B);
        WallWithGaps(upper, wallMat, V(-28, 6), V(-3, 6), H, B, (10f, WindowWidth), (22f, 2f));
        Window(upper, V(-18, 6), true, B);
        // Partial loop walls sit at x=±9, clear of the x=±12 windows in the z=-4 walls — the first
        // version started them exactly on those windows, which read as windows "cut off" by walls.
        WallWithGaps(upper, wallMat, V(-9, -4), V(-9, 6), H, B, (5f, WindowWidth));
        Window(upper, V(-9, 1), false, B);

        WallWithGaps(upper, wallMat, V(3, -4), V(28, -4), H, B, (9f, WindowWidth), (19f, 2f));
        Window(upper, V(12, -4), true, B);
        WallWithGaps(upper, wallMat, V(3, 6), V(28, 6), H, B, (3f, 2f), (15f, WindowWidth));
        Window(upper, V(18, 6), true, B);
        WallWithGaps(upper, wallMat, V(9, -4), V(9, 6), H, B, (5f, WindowWidth));
        Window(upper, V(9, 1), false, B);

        // Railings along every edge that overlooks a void. Gaps are the drop-down points.
        const float R = 1f;
        WallWithGaps(upper, railMat, V(-3, -14), V(-3, 10), R, B, (13f, 2f), (19f, 2f));
        WallWithGaps(upper, railMat, V(3, -14), V(3, 10), R, B, (13f, 2f), (19f, 2f));
        WallWithGaps(upper, railMat, V(-14, -18), V(14, -18), R, B, (4f, 2f), (14f, 2.5f), (24f, 2f));
        Wall(upper, railMat, V(-14, -18), V(-14, -14), R, B, "Rail", 0.12f);
        Wall(upper, railMat, V(14, -18), V(14, -14), R, B, "Rail", 0.12f);
        WallWithGaps(upper, railMat, V(-3, 10), V(3, 10), R, B, (3f, 2f));
        Wall(upper, railMat, V(-3, 14), V(3, 14), R, B, "Rail", 0.12f);
        WallWithGaps(upper, railMat, V(-28, 14), V(-3, 14), R, B, (8f, 2f));
        WallWithGaps(upper, railMat, V(3, 14), V(28, 14), R, B, (17f, 2f), (22.5f, 3.5f));
        WallWithGaps(upper, railMat, V(-28, -14), V(-14, -14), R, B, (2f, 3.5f), (8f, 2f));
        WallWithGaps(upper, railMat, V(14, -14), V(28, -14), R, B, (6f, 2f));
    }

    private static void BuildRoof(Transform root)
    {
        Transform group = Group(root, "Roof");
        Part(group, ceilingMat, "Roof", new Vector3(0f, RoofY + 0.15f, 0f), new Vector3(56.6f, 0.3f, 56.6f));
    }

    private static void BuildRamps(Transform root)
    {
        Transform group = Group(root, "Stairs");
        Ramp(group, new Vector3(-26f, 0f, -27.5f), new Vector3(-26f, SlabTopY, -13.5f), 2.5f, "StairsWest");
        Ramp(group, new Vector3(25.5f, 0f, 27.5f), new Vector3(25.5f, SlabTopY, 13.5f), 2.5f, "StairsEast");
    }

    // ---------------------------------------------------------------- furniture

    private static void BuildFurniture(Transform root)
    {
        Transform group = Group(root, "Furniture");

        foreach (var (x, y, z, yaw) in new[]
        {
            (-20f, 0f, -9f, 0f), (-12f, 0f, -9f, 0f), (-20f, 0f, 1f, 0f), (-12f, 0f, 1f, 0f), (-20f, 0f, 10f, 0f), (-12f, 0f, 10f, 0f),
            (-20f, SlabTopY, -9f, 0f), (-12f, SlabTopY, 1f, 90f), (20f, SlabTopY, 1f, 90f), (12f, SlabTopY, 10f, 0f), (-20f, SlabTopY, 10f, 0f), (20f, SlabTopY, -9f, 0f),
        })
            Bed(group, new Vector3(x, y, z), yaw);

        foreach (var (x, y, z, yaw) in new[]
        {
            (12f, 0f, -9f, 90f), (20f, 0f, -9f, 0f), (15f, 0f, 10f, 0f), (-21f, 0f, 24f, 90f), (21f, 0f, 18f, 90f), (-6f, 0f, 24f, 0f), (6f, 0f, 24f, 0f),
        })
            Gurney(group, new Vector3(x, y, z), yaw);

        Counter(group, new Vector3(0f, 0f, -18f), 8f);

        foreach (var (x, z) in new[] { (-8f, -24f), (9f, -25f), (24f, 4f), (-24f, -1f), (-24f, 24f), (10f, 26f) })
            Part(group, woodMat, "Crate", new Vector3(x, 0.5f, z), Vector3.one);

        // IV stands: a bit of clutter next to some beds.
        foreach (var (x, y, z) in new[] { (-18.8f, 0f, -8.3f), (-10.8f, 0f, 1.7f), (13.2f, 0f, 9.3f), (-18.8f, SlabTopY, 10.7f), (21.2f, SlabTopY, -8.3f) })
        {
            Part(group, metalMat, "IVStand", new Vector3(x, y + 0.9f, z), new Vector3(0.05f, 1.8f, 0.05f));
            Part(group, metalMat, "IVBase", new Vector3(x, y + 0.03f, z), new Vector3(0.5f, 0.06f, 0.5f));
        }
    }

    private static void BuildElevatorShaft(Transform root)
    {
        Transform group = Group(root, "Elevator");
        // Out-of-order lift in the lobby's east end: a closed shaft, purely atmospheric for now —
        // functional elevators (a moving platform the CharacterController rides) are not built.
        Wall(group, metalMat, V(22.5f, -23.5f), V(25.5f, -23.5f), RoofY, 0f, "ShaftBack");
        Wall(group, metalMat, V(22.5f, -23.5f), V(22.5f, -20.5f), RoofY, 0f, "ShaftSide");
        Wall(group, metalMat, V(25.5f, -23.5f), V(25.5f, -20.5f), RoofY, 0f, "ShaftSide");
        Part(group, railMat, "ElevatorDoors", new Vector3(24f, 1.5f, -20.5f), new Vector3(2.8f, 3f, 0.15f));
        Part(group, railMat, "ElevatorDoorsUpper", new Vector3(24f, SlabTopY + 1.5f, -20.5f), new Vector3(2.8f, 3f, 0.15f));
    }

    // ---------------------------------------------------------------- lights, decals, atmosphere

    private static void BuildLights(Transform root)
    {
        Transform group = Group(root, "Lights");
        // Warmer, less green than the first pass — closer to the reference's teal-amber wash.
        Color fluorescent = new Color(0.85f, 0.9f, 0.78f);
        int i = 0;
        // Ground-floor rooms are 3.85 high (slab underside), so tubes sit at 3.4; the lobby and
        // north wing are double-height atriums under the roof, so their fixtures hang as pendants
        // at 4.6 instead of being lost up at the roof.
        foreach (var (x, y, z) in new[]
        {
            (0f, 3.4f, -8f), (0f, 3.4f, 4f), (0f, 3.4f, 12f),
            (-8f, 4.6f, -22f), (8f, 4.6f, -22f), (-20f, 4.6f, -20f), (20f, 4.6f, -24f), (0f, 4.6f, -17f),
            (-22f, 3.4f, -9f), (-8f, 3.4f, -9f), (-22f, 3.4f, 1f), (-8f, 3.4f, 1f), (-22f, 3.4f, 10f), (-8f, 3.4f, 10f),
            (8f, 3.4f, -9f), (22f, 3.4f, -9f), (8f, 3.4f, 1f), (22f, 3.4f, 1f), (8f, 3.4f, 10f), (22f, 3.4f, 10f),
            (-21f, 4.6f, 18f), (-21f, 4.6f, 25f), (21f, 4.6f, 18f), (21f, 4.6f, 25f), (0f, 4.6f, 26f),
            (-22f, 7.6f, -9f), (-8f, 7.6f, -9f), (-22f, 7.6f, 1f), (-8f, 7.6f, 1f), (-22f, 7.6f, 10f), (-8f, 7.6f, 10f),
            (8f, 7.6f, -9f), (22f, 7.6f, -9f), (8f, 7.6f, 1f), (22f, 7.6f, 1f), (8f, 7.6f, 10f), (22f, 7.6f, 10f),
            (0f, 7.6f, 12f), (0f, 7.6f, -16f),
        })
        {
            bool flicker = i++ % 3 != 1;
            Fixture(group, new Vector3(x, y, z), fluorescent, flicker ? 6.5f : 5.5f, 18f, flicker);
        }

        // Morgue: red emergency lighting for the Killer's start room, plus one dying white tube.
        Light red = PointLight(group, new Vector3(0f, 3.4f, 21f), new Color(0.9f, 0.1f, 0.06f), 6f, 18f);
        red.gameObject.AddComponent<FlickerLight>();
    }

    private static void Fixture(Transform group, Vector3 pos, Color color, float intensity, float range, bool flicker)
    {
        Light light = PointLight(group, pos, color, intensity, range);
        if (flicker) light.gameObject.AddComponent<FlickerLight>();
        // Glowing tube just above the light so the source itself is visible, not just its glow.
        GameObject tube = Part(group, tubeMat, "Tube", pos + new Vector3(0f, 0.32f, 0f), new Vector3(1.3f, 0.07f, 0.28f));
        Object.DestroyImmediate(tube.GetComponent<Collider>());
        Object.DestroyImmediate(tube.GetComponent<AutoTile>());
    }

    private static void BuildDecals(Transform root)
    {
        Transform group = Group(root, "Decals");
        foreach (var (x, y, z, yaw, size) in new[]
        {
            (0f, 0f, -6f, 30f, 2.2f), (-1.5f, 0f, 8f, 200f, 1.6f), (-16f, 0f, -10f, 75f, 2.5f), (-9f, 0f, 3f, 10f, 1.4f),
            (14f, 0f, -11f, 120f, 2f), (22f, 0f, 1f, 300f, 1.8f), (0f, 0f, 22f, 45f, 3f), (-4f, 0f, 25f, 160f, 1.5f),
            (7f, 0f, -22f, 90f, 2f), (-20f, 0f, -22f, 15f, 1.7f), (-15f, SlabTopY, -8f, 240f, 2.2f), (13f, SlabTopY, 8f, 60f, 1.9f),
            (0f, SlabTopY, 12f, 0f, 1.6f), (-6f, SlabTopY, -16f, 110f, 2f),
        })
        {
            GameObject splat = Part(group, bloodMat, "BloodSplat", new Vector3(x, y + 0.012f, z), new Vector3(size, 0.02f, size), new Vector3(0f, yaw, 0f));
            Object.DestroyImmediate(splat.GetComponent<Collider>());
            Object.DestroyImmediate(splat.GetComponent<AutoTile>());
        }
    }

    private static void BuildAtmosphere(Transform root)
    {
        Transform group = Group(root, "Atmosphere");
        // Drifting dust motes across both storeys, and a low slow fog layer on each floor.
        Emitter(group, "Dust", new Vector3(0f, 4.2f, 0f), new Vector3(56f, 8f, 56f), dustMat, 600, 0.03f, 0.08f, new Color(0.85f, 0.9f, 0.85f, 0.35f), 0.05f, 14f, 0.25f);
        Emitter(group, "FloorFogGround", new Vector3(0f, 0.7f, 0f), new Vector3(56f, 1.2f, 56f), fogMat, 90, 7f, 11f, new Color(0.55f, 0.62f, 0.7f, 0.05f), 0.12f, 16f, 0.05f);
        Emitter(group, "FloorFogUpper", new Vector3(0f, SlabTopY + 0.7f, 0f), new Vector3(56f, 1.2f, 28f), fogMat, 40, 7f, 11f, new Color(0.55f, 0.62f, 0.7f, 0.05f), 0.12f, 16f, 0.05f);
    }

    private static void Emitter(Transform parent, string name, Vector3 center, Vector3 size, Material mat, int max, float sizeMin, float sizeMax, Color color, float speed, float life, float noise)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = center;
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.startLifetime = life;
        main.startSpeed = speed;
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startColor = color;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.maxParticles = max;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = max / life;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = size;
        var n = ps.noise;
        n.enabled = true;
        n.strength = noise;
        n.frequency = 0.15f;
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        col.color = gradient;
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = mat;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
    }

    // ---------------------------------------------------------------- networked interactables

    private static void PlaceNetworkedInteractables()
    {
        GameObject doorPrefab = CreateSlamDoorPrefab();
        GameObject barrierPrefab = CreateBreakableBarrierPrefab();
        GameObject lockerPrefab = CreateHidingLockerPrefab();

        if (Object.FindFirstObjectByType<SlamDoor>(FindObjectsInactive.Include) == null)
        {
            foreach (var (x, y, z, yaw) in new[]
            {
                (-20f, 0f, -14f, 0f),
                (-3f, 0f, -9f, 90f), (-3f, 0f, 1f, 90f), (-3f, 0f, 10f, 90f),
                (3f, 0f, -9f, 90f), (3f, 0f, 1f, 90f), (3f, 0f, 10f, 90f),
                (-6f, 0f, -4f, 0f), (-8f, 0f, 6f, 0f), (22f, 0f, -4f, 0f),
                (-20f, 0f, 14f, 0f), (20f, 0f, 14f, 0f), (-14f, 0f, 21f, 90f), (14f, 0f, 21f, 90f),
                (-22f, SlabTopY, -4f, 0f), (-6f, SlabTopY, 6f, 0f), (22f, SlabTopY, -4f, 0f), (6f, SlabTopY, 6f, 0f),
            })
                Place(doorPrefab, new Vector3(x, y, z), yaw);
        }

        if (Object.FindFirstObjectByType<BreakableBarrier>(FindObjectsInactive.Include) == null)
        {
            Place(barrierPrefab, new Vector3(20f, 0f, -14f), 0f);
            Place(barrierPrefab, new Vector3(8f, 0f, 6f), 0f);
            Place(barrierPrefab, new Vector3(0f, SlabTopY, 12f), 0f);
        }

        if (Object.FindFirstObjectByType<HidingLocker>(FindObjectsInactive.Include) == null)
        {
            foreach (var (x, y, z, yaw) in new[]
            {
                (27.4f, 0f, -2f, 270f), (27.4f, 0f, 0f, 270f), (27.4f, 0f, 2f, 270f),
                (-27.4f, 0f, -12f, 90f), (-27.4f, 0f, 10f, 90f),
                (-12f, 0f, 27.4f, 180f), (12f, 0f, 27.4f, 180f), (14f, 0f, -13.4f, 0f),
                (-27.4f, SlabTopY, -8f, 90f), (27.4f, SlabTopY, 8f, 270f), (-27.4f, SlabTopY, 8f, 90f),
            })
                Place(lockerPrefab, new Vector3(x, y, z), yaw);
        }
    }

    private static void Place(GameObject prefab, Vector3 position, float yaw)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
    }

    private static GameObject CreateSlamDoorPrefab()
    {
        var root = InteractableRoot("SlamDoor", 2f);
        // Hinge sits at the doorway's edge so the open panel swings clear of the 2-wide opening.
        var hinge = new GameObject("Hinge");
        hinge.transform.SetParent(root.transform, false);
        hinge.transform.localPosition = new Vector3(-0.95f, 0f, 0f);
        Part(hinge.transform, woodMat, "Panel", new Vector3(0.95f, 1.5f, 0f), new Vector3(1.9f, 3f, 0.12f));
        root.AddComponent<SlamDoor>();
        return SavePrefab(root, "SlamDoor");
    }

    private static GameObject CreateBreakableBarrierPrefab()
    {
        var root = InteractableRoot("BreakableBarrier", 2f);
        var boards = new GameObject("Boards");
        boards.transform.SetParent(root.transform, false);
        Part(boards.transform, woodMat, "Backing", new Vector3(0f, 1.2f, 0f), new Vector3(2f, 2.4f, 0.15f));
        Part(boards.transform, woodMat, "Plank", new Vector3(0f, 1.2f, 0.1f), new Vector3(0.22f, 2.7f, 0.05f), new Vector3(0f, 0f, 35f));
        Part(boards.transform, woodMat, "Plank", new Vector3(0f, 1.2f, 0.1f), new Vector3(0.22f, 2.7f, 0.05f), new Vector3(0f, 0f, -35f));
        root.AddComponent<BreakableBarrier>();
        return SavePrefab(root, "BreakableBarrier");
    }

    private static GameObject CreateHidingLockerPrefab()
    {
        var root = InteractableRoot("HidingLocker", 1.8f);
        Part(root.transform, metalMat, "Body", new Vector3(0f, 1.1f, 0f), new Vector3(0.9f, 2.2f, 0.9f));
        GameObject door = Part(root.transform, railMat, "DoorDetail", new Vector3(0f, 1.1f, 0.47f), new Vector3(0.7f, 1.8f, 0.05f));
        Object.DestroyImmediate(door.GetComponent<Collider>());
        root.AddComponent<HidingLocker>();
        return SavePrefab(root, "HidingLocker");
    }

    // NetworkObject + kinematic Rigidbody + sphere trigger: the same shell WeaponPickup and
    // RestoreBeacon use, so trigger events fire reliably against CharacterControllers.
    private static GameObject InteractableRoot(string name, float triggerRadius)
    {
        var root = new GameObject(name);
        root.AddComponent<NetworkObject>();
        SphereCollider trigger = root.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = triggerRadius;
        trigger.center = new Vector3(0f, 1f, 0f);
        Rigidbody rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        return root;
    }

    private static GameObject SavePrefab(GameObject root, string name)
    {
        if (!AssetDatabase.IsValidFolder(PrefabDir)) AssetDatabase.CreateFolder("Assets", "Prefabs");
        NetworkScaffoldSetup.PersistRuntimeMaterials(root, MaterialsDir);
        string path = $"{PrefabDir}/{name}.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    // ---------------------------------------------------------------- geometry helpers

    private static Vector3 V(float x, float z) => new Vector3(x, 0f, z);

    private static Transform Group(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    // CreateSolidPart + AutoTile, so every textured primitive tiles by its own size.
    private static GameObject Part(Transform parent, Material mat, string name, Vector3 localPos, Vector3 localScale, Vector3? localEuler = null, float metersPerTile = 2f)
    {
        GameObject part = NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cube, parent, mat, name, localPos, localScale, localEuler);
        AutoTile tile = part.AddComponent<AutoTile>();
        if (!Mathf.Approximately(metersPerTile, 2f))
        {
            var so = new SerializedObject(tile);
            so.FindProperty("metersPerTile").floatValue = metersPerTile;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        return part;
    }

    // Long walls are split into <=12m pieces: URP lights each renderer with at most
    // maxAdditionalLightsCount (8) lights, so one 56m exterior wall would only ever catch a
    // handful of the fixtures along it and go black elsewhere.
    private const float MaxWallSegment = 12f;

    private static GameObject Wall(Transform parent, Material mat, Vector3 a, Vector3 b, float height, float baseY, string name = "Wall", float thickness = WallT)
    {
        Vector3 dir = b - a;
        dir.y = 0f;
        float length = dir.magnitude;
        if (length > MaxWallSegment)
        {
            int pieces = Mathf.CeilToInt(length / MaxWallSegment);
            GameObject last = null;
            for (int i = 0; i < pieces; i++)
                last = Wall(parent, mat, Vector3.Lerp(a, b, (float)i / pieces), Vector3.Lerp(a, b, (float)(i + 1) / pieces), height, baseY, name, thickness);
            return last;
        }
        Vector3 center = (a + b) * 0.5f;
        center.y = baseY + height * 0.5f;
        float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        return Part(parent, mat, name, center, new Vector3(thickness, height, length), new Vector3(0f, yaw, 0f), mat == wallMat ? 3.5f : 2f);
    }

    // `gaps` are (distance along the wall from `a`, width). Segments are emitted between them.
    private static void WallWithGaps(Transform parent, Material mat, Vector3 a, Vector3 b, float height, float baseY, params (float at, float width)[] gaps)
    {
        Vector3 dir = b - a;
        dir.y = 0f;
        float length = dir.magnitude;
        Vector3 n = dir / length;
        float thickness = height <= 1.01f ? 0.12f : WallT;

        float cursor = 0f;
        foreach (var gap in gaps.OrderBy(g => g.at))
        {
            float start = gap.at - gap.width * 0.5f;
            if (start > cursor + 0.05f)
                Wall(parent, mat, a + n * cursor, a + n * start, height, baseY, "Wall", thickness);
            cursor = gap.at + gap.width * 0.5f;
        }
        if (cursor < length - 0.05f)
            Wall(parent, mat, a + n * cursor, b, height, baseY, "Wall", thickness);
    }

    // A vaultable window in a wall gap: sill below, lintel above, VaultableObstacle trigger in the
    // opening. Wall() maps "runs along X" to yaw 90 and "runs along Z" to yaw 0 — `alongX` keeps
    // that convention in one place (the first version passed the raw angles the wrong way round
    // and every window ended up perpendicular to its wall). The trigger's forward is the wall
    // normal, i.e. wall yaw + 90.
    private static void Window(Transform parent, Vector3 position, bool alongX, float baseY)
    {
        float wallYaw = alongX ? 90f : 0f;
        Vector3 pos = new Vector3(position.x, baseY, position.z);
        Vector3 euler = new Vector3(0f, wallYaw, 0f);
        Part(parent, wallMat, "WindowSill", pos + new Vector3(0f, 0.5f, 0f), new Vector3(WallT, 1f, WindowWidth), euler);
        Part(parent, wallMat, "WindowLintel", pos + new Vector3(0f, 3.1f, 0f), new Vector3(WallT, FloorHeight - 2.2f, WindowWidth), euler);
        GameObject frameTop = Part(parent, woodMat, "WindowFrame", pos + new Vector3(0f, 1.05f, 0f), new Vector3(WallT + 0.06f, 0.08f, WindowWidth + 0.1f), euler);
        Object.DestroyImmediate(frameTop.GetComponent<Collider>());

        var vault = new GameObject("Window");
        vault.transform.SetParent(parent, false);
        vault.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, wallYaw + 90f, 0f));
        AddVaultTrigger(vault, new Vector3(WindowWidth + 0.6f, 2.2f, 2.4f), 1.0f, "Press E to vault window");
    }

    private static void Bed(Transform parent, Vector3 pos, float yaw)
    {
        var bed = new GameObject("Bed");
        bed.transform.SetParent(parent, false);
        bed.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        Part(bed.transform, metalMat, "Frame", new Vector3(0f, 0.25f, 0f), new Vector3(2f, 0.5f, 1f));
        Part(bed.transform, sheetMat, "Mattress", new Vector3(0f, 0.58f, 0f), new Vector3(1.9f, 0.16f, 0.95f));
        Part(bed.transform, metalMat, "Headboard", new Vector3(-0.97f, 0.65f, 0f), new Vector3(0.08f, 1.3f, 1f));
        AddVaultTrigger(bed, new Vector3(2.6f, 2.2f, 2.6f), 1.2f, "Press E to vault bed");
    }

    private static void Gurney(Transform parent, Vector3 pos, float yaw)
    {
        var g = new GameObject("Gurney");
        g.transform.SetParent(parent, false);
        g.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        Part(g.transform, metalMat, "Top", new Vector3(0f, 0.85f, 0f), new Vector3(2f, 0.12f, 0.8f));
        Part(g.transform, metalMat, "Legs", new Vector3(0f, 0.4f, 0f), new Vector3(1.6f, 0.8f, 0.5f));
        AddVaultTrigger(g, new Vector3(2.6f, 2.2f, 2.4f), 1.1f, "Press E to vault");
    }

    private static void Counter(Transform parent, Vector3 pos, float length)
    {
        var c = new GameObject("Counter");
        c.transform.SetParent(parent, false);
        c.transform.position = pos;
        Part(c.transform, woodMat, "Body", new Vector3(0f, 0.55f, 0f), new Vector3(length, 1.1f, 1f));
        Part(c.transform, concreteMat, "Top", new Vector3(0f, 1.13f, 0f), new Vector3(length + 0.2f, 0.06f, 1.2f));
        AddVaultTrigger(c, new Vector3(length + 0.6f, 2.2f, 2.8f), 1.3f, "Press E to vault counter");
    }

    private static void AddVaultTrigger(GameObject go, Vector3 size, float crossDistance, string prompt)
    {
        BoxCollider trigger = go.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = size;
        trigger.center = new Vector3(0f, size.y * 0.5f, 0f);
        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        VaultableObstacle vault = go.AddComponent<VaultableObstacle>();
        var so = new SerializedObject(vault);
        so.FindProperty("crossDistance").floatValue = crossDistance;
        so.FindProperty("prompt").stringValue = prompt;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // Two layers: a thin tiled floor on top, a stained ceiling-panel underside for the room below.
    private static void Slab(Transform parent, Vector3 centerXZ, Vector2 size, string name)
    {
        const float topT = 0.12f;
        Part(parent, floorMat, name, new Vector3(centerXZ.x, SlabTopY - topT * 0.5f, centerXZ.z), new Vector3(size.x, topT, size.y));
        Part(parent, ceilingMat, name + "Ceiling", new Vector3(centerXZ.x, SlabTopY - topT - (SlabThickness - topT) * 0.5f, centerXZ.z),
            new Vector3(size.x, SlabThickness - topT, size.y));
    }

    // A sloped box standing in for a staircase — a real stepped stair is more geometry for the
    // same CharacterController traversal (slopeLimit 45 handles ~17 degrees easily).
    private static void Ramp(Transform parent, Vector3 bottom, Vector3 top, float width, string name)
    {
        Vector3 dir = top - bottom;
        float rise = dir.y;
        Vector3 flat = new Vector3(dir.x, 0f, dir.z);
        float run = flat.magnitude;
        float length = Mathf.Sqrt(run * run + rise * rise);
        float yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
        float pitch = -Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
        Vector3 center = (bottom + top) * 0.5f;
        center.y -= 0.125f;
        Part(parent, concreteMat, name, center, new Vector3(width, 0.25f, length), new Vector3(pitch, yaw, 0f));
    }

    private static Light PointLight(Transform parent, Vector3 pos, Color color, float intensity, float range)
    {
        var go = new GameObject("Light");
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        Light light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.None;
        return light;
    }
}
