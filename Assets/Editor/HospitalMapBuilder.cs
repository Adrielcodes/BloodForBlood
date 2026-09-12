using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

// Builds the abandoned-hospital gameplay map: a 56x56 two-storey layout designed around DBD-style
// chase loops. Every room has at least two ways in/out (a door plus a vaultable window, or two
// doors), so no corridor is a dead end; the second floor is a U-shaped mezzanine whose railings
// have gaps you can drop through into the central corridor or lobby. Static geometry lives under
// one "HospitalMap" root (idempotent by name); the networked interactables (SlamDoor,
// BreakableBarrier, HidingLocker) are prefab instances at scene root like every other in-scene
// NetworkObject here. All coordinates are world units, origin at the map centre, z+ = north.
//
// Floor plan (ground): south lobby (Survivor spawns) -> lobby wall z=-14 -> west wing (3 wards) /
// central corridor / east wing (pharmacy, locker room, operating room) -> wall z=14 -> north
// wing (morgue = Killer spawn, flanked by two rooms). Two ramps ("staircases") reach the second
// floor: one in the lobby's west corner, one in the north wing's east corner.
internal static class HospitalMapBuilder
{
    private const float FloorHeight = 4f;
    private const float SlabTopY = 4.15f;
    private const float SlabThickness = 0.3f;
    private const float WallT = 0.3f;
    private const float WindowWidth = 1.6f;
    private const string MaterialsDir = "Assets/Art/Environment/Materials";
    private const string PrefabDir = "Assets/Prefabs";

    private static Material wallMat, wallUpperMat, slabMat, railMat, bedMat, sheetMat, counterMat, crateMat, rampMat, metalMat, woodMat;

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
        GameObject legacy = GameObject.Find("EnvironmentDressing");
        if (legacy != null) Object.DestroyImmediate(legacy);

        if (GameObject.Find("HospitalMap") != null)
        {
            Debug.Log("Blood For Blood: HospitalMap already present in scene, skipping.");
            return;
        }

        // First build on a scene that still has the old open-field objective placements: clear
        // them so PlaceObjectives below re-places everything at the hospital spawn points.
        foreach (var b in Object.FindObjectsByType<RestoreBeacon>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.DestroyImmediate(b.gameObject);
        foreach (var p in Object.FindObjectsByType<WeaponPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Object.DestroyImmediate(p.gameObject);

        GameObject ground = GameObject.Find("Ground");
        if (ground != null) ground.transform.localScale = new Vector3(7f, 1f, 7f);

        CreateMaterials();

        var root = new GameObject("HospitalMap").transform;
        BuildGroundFloor(root);
        BuildSecondFloor(root);
        BuildRamps(root);
        BuildFurniture(root);
        BuildElevatorShaft(root);
        BuildLights(root);

        NetworkScaffoldSetup.PersistRuntimeMaterials(root.gameObject, MaterialsDir);

        PlaceNetworkedInteractables();
    }

    public static void PlaceObjectives(GameObject beaconPrefab, GameObject pickupPrefab)
    {
        if (Object.FindFirstObjectByType<RestoreBeacon>(FindObjectsInactive.Include) == null)
        {
            foreach (Vector3 pos in BeaconSpawns)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(beaconPrefab);
                go.transform.position = pos;
            }
        }

        if (Object.FindFirstObjectByType<WeaponPickup>(FindObjectsInactive.Include) == null)
        {
            foreach (Vector3 pos in PickupSpawns)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(pickupPrefab);
                go.transform.position = pos;
            }
        }
    }

    private static void CreateMaterials()
    {
        wallMat = NetworkScaffoldSetup.CreateColorMaterial("HospitalWallMat", new Color(0.5f, 0.56f, 0.5f));
        wallUpperMat = NetworkScaffoldSetup.CreateColorMaterial("HospitalWallUpperMat", new Color(0.42f, 0.46f, 0.43f));
        slabMat = NetworkScaffoldSetup.CreateColorMaterial("HospitalSlabMat", new Color(0.28f, 0.28f, 0.3f));
        railMat = NetworkScaffoldSetup.CreateColorMaterial("HospitalRailMat", new Color(0.2f, 0.2f, 0.22f));
        bedMat = NetworkScaffoldSetup.CreateColorMaterial("HospitalBedFrameMat", new Color(0.35f, 0.36f, 0.4f));
        sheetMat = NetworkScaffoldSetup.CreateColorMaterial("HospitalSheetMat", new Color(0.7f, 0.68f, 0.62f));
        counterMat = NetworkScaffoldSetup.CreateColorMaterial("HospitalCounterMat", new Color(0.32f, 0.26f, 0.2f));
        crateMat = NetworkScaffoldSetup.CreateColorMaterial("HospitalCrateMat", new Color(0.42f, 0.29f, 0.16f));
        rampMat = NetworkScaffoldSetup.CreateColorMaterial("HospitalRampMat", new Color(0.24f, 0.24f, 0.26f));
        metalMat = NetworkScaffoldSetup.CreateColorMaterial("HospitalMetalMat", new Color(0.3f, 0.36f, 0.34f));
        woodMat = NetworkScaffoldSetup.CreateColorMaterial("HospitalWoodMat", new Color(0.38f, 0.27f, 0.15f));
    }

    // ---------------------------------------------------------------- ground floor

    private static void BuildGroundFloor(Transform root)
    {
        Transform floor = Group(root, "GroundFloor");
        const float H = FloorHeight;

        // Exterior shell, both storeys tall.
        Wall(floor, wallMat, V(-28, -28), V(28, -28), 8.3f, 0f, "ExteriorS");
        Wall(floor, wallMat, V(28, -28), V(28, 28), 8.3f, 0f, "ExteriorE");
        Wall(floor, wallMat, V(28, 28), V(-28, 28), 8.3f, 0f, "ExteriorN");
        Wall(floor, wallMat, V(-28, 28), V(-28, -28), 8.3f, 0f, "ExteriorW");

        // Lobby / wings divider (z = -14): corridor mouth, a door west, a boarded shortcut east,
        // and a window either side of the corridor.
        WallWithGaps(floor, wallMat, V(-28, -14), V(28, -14), H, 0f, (28f, 4f), (8f, 2f), (48f, 2f), (18f, WindowWidth), (38f, WindowWidth));
        Window(floor, V(-10, -14), 0f, 0f);
        Window(floor, V(10, -14), 0f, 0f);

        // Central corridor walls (x = ±3), three doors each plus one window each for loops.
        WallWithGaps(floor, wallMat, V(-3, -14), V(-3, 14), H, 0f, (5f, 2f), (15f, 2f), (24f, 2f), (19f, WindowWidth));
        Window(floor, V(-3, 5), 90f, 0f);
        WallWithGaps(floor, wallMat, V(3, -14), V(3, 14), H, 0f, (5f, 2f), (15f, 2f), (24f, 2f), (11f, WindowWidth));
        Window(floor, V(3, -3), 90f, 0f);

        // West wing: three wards separated by walls at z = -4 and z = 6.
        WallWithGaps(floor, wallMat, V(-28, -4), V(-3, -4), H, 0f, (13f, WindowWidth), (22f, 2f));
        Window(floor, V(-15, -4), 0f, 0f);
        WallWithGaps(floor, wallMat, V(-28, 6), V(-3, 6), H, 0f, (8f, WindowWidth), (20f, 2f));
        Window(floor, V(-20, 6), 0f, 0f);

        // East wing: pharmacy (south), locker room (middle), operating room (north).
        WallWithGaps(floor, wallMat, V(3, -4), V(28, -4), H, 0f, (12f, WindowWidth), (19f, 2f));
        Window(floor, V(15, -4), 0f, 0f);
        WallWithGaps(floor, wallMat, V(3, 6), V(28, 6), H, 0f, (5f, 2f), (19f, WindowWidth));
        Window(floor, V(22, 6), 0f, 0f);

        // Wings / north wing divider (z = 14). The east ramp passes through at x = 25.5.
        WallWithGaps(floor, wallMat, V(-28, 14), V(28, 14), H, 0f, (28f, 4f), (8f, 2f), (48f, 2f), (20f, WindowWidth), (36f, WindowWidth), (53.5f, 3.5f));
        Window(floor, V(-8, 14), 0f, 0f);
        Window(floor, V(8, 14), 0f, 0f);

        // North wing: morgue in the middle, a room either side.
        WallWithGaps(floor, wallMat, V(-14, 14), V(-14, 28), H, 0f, (7f, 2f));
        WallWithGaps(floor, wallMat, V(14, 14), V(14, 28), H, 0f, (7f, 2f));

        // Short freestanding loop walls inside the bigger rooms — something to run around.
        Wall(floor, wallUpperMat, V(-20, -12), V(-20, -8), H, 0f, "LoopWall");
        Wall(floor, wallUpperMat, V(18, 8), V(18, 12), H, 0f, "LoopWall");
        Wall(floor, wallUpperMat, V(-4, 18), V(4, 18), H, 0f, "LoopWall");
    }

    // ---------------------------------------------------------------- second floor

    private static void BuildSecondFloor(Transform root)
    {
        Transform upper = Group(root, "SecondFloor");
        const float H = FloorHeight;
        const float B = SlabTopY;

        Slab(upper, V(-15.5f, 0f), new Vector2(25f, 28f), "WestSlab");
        Slab(upper, V(15.5f, 0f), new Vector2(25f, 28f), "EastSlab");
        Slab(upper, V(0f, 12f), new Vector2(6f, 4f), "NorthBridge");
        Slab(upper, V(0f, -16f), new Vector2(28f, 4f), "LobbyBalcony");

        // Upstairs rooms mirror the wings below, with their own door/window loops.
        WallWithGaps(upper, wallUpperMat, V(-28, -4), V(-3, -4), H, B, (16f, WindowWidth), (6f, 2f));
        Window(upper, V(-12, -4), 0f, B);
        WallWithGaps(upper, wallUpperMat, V(-28, 6), V(-3, 6), H, B, (10f, WindowWidth), (22f, 2f));
        Window(upper, V(-18, 6), 0f, B);
        WallWithGaps(upper, wallUpperMat, V(-12, -4), V(-12, 6), H, B, (5f, WindowWidth));
        Window(upper, V(-12, 1), 90f, B);

        WallWithGaps(upper, wallUpperMat, V(3, -4), V(28, -4), H, B, (9f, WindowWidth), (19f, 2f));
        Window(upper, V(12, -4), 0f, B);
        WallWithGaps(upper, wallUpperMat, V(3, 6), V(28, 6), H, B, (3f, 2f), (15f, WindowWidth));
        Window(upper, V(18, 6), 0f, B);
        WallWithGaps(upper, wallUpperMat, V(12, -4), V(12, 6), H, B, (5f, WindowWidth));
        Window(upper, V(12, 1), 90f, B);

        // Railings along every edge that overlooks a void. Gaps are the drop-down points.
        const float R = 1f;
        WallWithGaps(upper, railMat, V(-3, -14), V(-3, 10), R, B, (13f, 2f), (19f, 2f));
        WallWithGaps(upper, railMat, V(3, -14), V(3, 10), R, B, (13f, 2f), (19f, 2f));
        WallWithGaps(upper, railMat, V(-14, -18), V(14, -18), R, B, (4f, 2f), (14f, 2.5f), (24f, 2f));
        Wall(upper, railMat, V(-14, -18), V(-14, -14), R, B, "Rail");
        Wall(upper, railMat, V(14, -18), V(14, -14), R, B, "Rail");
        WallWithGaps(upper, railMat, V(-3, 10), V(3, 10), R, B, (3f, 2f));
        Wall(upper, railMat, V(-3, 14), V(3, 14), R, B, "Rail");
        WallWithGaps(upper, railMat, V(-28, 14), V(-3, 14), R, B, (8f, 2f));
        WallWithGaps(upper, railMat, V(3, 14), V(28, 14), R, B, (17f, 2f), (22.5f, 3.5f));
        WallWithGaps(upper, railMat, V(-28, -14), V(-14, -14), R, B, (2f, 3.5f), (8f, 2f));
        WallWithGaps(upper, railMat, V(14, -14), V(28, -14), R, B, (6f, 2f));
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

        // Wards + upstairs: beds (vaultable across their width).
        foreach (var (x, y, z, yaw) in new[]
        {
            (-20f, 0f, -9f, 0f), (-12f, 0f, -9f, 0f), (-20f, 0f, 1f, 0f), (-12f, 0f, 1f, 0f), (-20f, 0f, 10f, 0f), (-12f, 0f, 10f, 0f),
            (-20f, SlabTopY, -9f, 0f), (-12f, SlabTopY, 1f, 90f), (20f, SlabTopY, 1f, 90f), (12f, SlabTopY, 10f, 0f), (-20f, SlabTopY, 10f, 0f), (20f, SlabTopY, -9f, 0f),
        })
            Bed(group, new Vector3(x, y, z), yaw);

        // Gurneys / tables (vaultable).
        foreach (var (x, y, z, yaw) in new[]
        {
            (12f, 0f, -9f, 90f), (20f, 0f, -9f, 0f), (15f, 0f, 10f, 0f), (-21f, 0f, 24f, 90f), (21f, 0f, 18f, 90f), (-6f, 0f, 24f, 0f), (6f, 0f, 24f, 0f),
        })
            Gurney(group, new Vector3(x, y, z), yaw);

        Counter(group, new Vector3(0f, 0f, -18f), 8f);

        foreach (var (x, z) in new[] { (-8f, -24f), (9f, -25f), (24f, 4f), (-24f, -1f), (-24f, 24f), (10f, 26f) })
            NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cube, group, crateMat, "Crate", new Vector3(x, 0.5f, z), Vector3.one);
    }

    private static void BuildElevatorShaft(Transform root)
    {
        Transform group = Group(root, "Elevator");
        // Out-of-order lift in the lobby's east end: a closed shaft, purely atmospheric for now —
        // functional elevators (a moving platform the CharacterController rides) are not built.
        Wall(group, metalMat, V(22.5f, -23.5f), V(25.5f, -23.5f), 8.3f, 0f, "ShaftBack");
        Wall(group, metalMat, V(22.5f, -23.5f), V(22.5f, -20.5f), 8.3f, 0f, "ShaftSide");
        Wall(group, metalMat, V(25.5f, -23.5f), V(25.5f, -20.5f), 8.3f, 0f, "ShaftSide");
        NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cube, group, railMat, "ElevatorDoors",
            new Vector3(24f, 1.5f, -20.5f), new Vector3(2.8f, 3f, 0.15f));
        NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cube, group, railMat, "ElevatorDoorsUpper",
            new Vector3(24f, SlabTopY + 1.5f, -20.5f), new Vector3(2.8f, 3f, 0.15f));
    }

    private static void BuildLights(Transform root)
    {
        Transform group = Group(root, "Lights");
        Color fluorescent = new Color(0.72f, 0.85f, 0.7f);
        foreach (var (x, y, z) in new[]
        {
            (0f, 3f, -8f), (0f, 3f, 4f), (0f, 3f, 12f),
            (0f, 3.5f, -22f), (-18f, 3.5f, -22f), (18f, 3.5f, -22f),
            (-15f, 3f, -9f), (-15f, 3f, 1f), (-15f, 3f, 10f),
            (15f, 3f, -9f), (15f, 3f, 1f), (15f, 3f, 10f),
            (-21f, 3f, 21f), (21f, 3f, 21f),
            (-15f, 7.5f, -9f), (-15f, 7.5f, 1f), (15f, 7.5f, 1f), (15f, 7.5f, 10f), (0f, 7.5f, 12f), (0f, 7.5f, -16f),
        })
            PointLight(group, new Vector3(x, y, z), fluorescent, 1.6f, 11f);

        // Morgue: red emergency lighting for the Killer's start room.
        PointLight(group, new Vector3(0f, 3f, 21f), new Color(0.85f, 0.12f, 0.08f), 3f, 14f);
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
        NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cube, hinge.transform, woodMat, "Panel",
            new Vector3(0.95f, 1.5f, 0f), new Vector3(1.9f, 3f, 0.12f));
        root.AddComponent<SlamDoor>();
        return SavePrefab(root, "SlamDoor");
    }

    private static GameObject CreateBreakableBarrierPrefab()
    {
        var root = InteractableRoot("BreakableBarrier", 2f);
        var boards = new GameObject("Boards");
        boards.transform.SetParent(root.transform, false);
        NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cube, boards.transform, woodMat, "Backing",
            new Vector3(0f, 1.2f, 0f), new Vector3(2f, 2.4f, 0.15f));
        NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cube, boards.transform, crateMat, "Plank",
            new Vector3(0f, 1.2f, 0.1f), new Vector3(0.22f, 2.7f, 0.05f), new Vector3(0f, 0f, 35f));
        NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cube, boards.transform, crateMat, "Plank",
            new Vector3(0f, 1.2f, 0.1f), new Vector3(0.22f, 2.7f, 0.05f), new Vector3(0f, 0f, -35f));
        root.AddComponent<BreakableBarrier>();
        return SavePrefab(root, "BreakableBarrier");
    }

    private static GameObject CreateHidingLockerPrefab()
    {
        var root = InteractableRoot("HidingLocker", 1.8f);
        NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cube, root.transform, metalMat, "Body",
            new Vector3(0f, 1.1f, 0f), new Vector3(0.9f, 2.2f, 0.9f));
        GameObject door = NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cube, root.transform, railMat, "DoorDetail",
            new Vector3(0f, 1.1f, 0.47f), new Vector3(0.7f, 1.8f, 0.05f));
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

    private static GameObject Wall(Transform parent, Material mat, Vector3 a, Vector3 b, float height, float baseY, string name = "Wall", float thickness = WallT)
    {
        Vector3 dir = b - a;
        dir.y = 0f;
        float length = dir.magnitude;
        Vector3 center = (a + b) * 0.5f;
        center.y = baseY + height * 0.5f;
        float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        return NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cube, parent, mat, name, center,
            new Vector3(thickness, height, length), new Vector3(0f, yaw, 0f));
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
    // opening. `wallYaw` is the direction the wall runs; the trigger's forward is the wall normal.
    private static void Window(Transform parent, Vector3 position, float wallYaw, float baseY)
    {
        Vector3 pos = new Vector3(position.x, baseY, position.z);
        Quaternion along = Quaternion.Euler(0f, wallYaw, 0f);
        NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cube, parent, wallMat, "WindowSill",
            pos + new Vector3(0f, 0.5f, 0f), new Vector3(WallT, 1f, WindowWidth), along.eulerAngles);
        NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cube, parent, wallMat, "WindowLintel",
            pos + new Vector3(0f, 3.1f, 0f), new Vector3(WallT, FloorHeight - 2.2f, WindowWidth), along.eulerAngles);

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
        NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cube, bed.transform, bedMat, "Frame", new Vector3(0f, 0.25f, 0f), new Vector3(2f, 0.5f, 1f));
        NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cube, bed.transform, sheetMat, "Mattress", new Vector3(0f, 0.58f, 0f), new Vector3(1.9f, 0.16f, 0.95f));
        NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cube, bed.transform, bedMat, "Headboard", new Vector3(-0.97f, 0.65f, 0f), new Vector3(0.08f, 1.3f, 1f));
        AddVaultTrigger(bed, new Vector3(2.6f, 2.2f, 2.6f), 1.2f, "Press E to vault bed");
    }

    private static void Gurney(Transform parent, Vector3 pos, float yaw)
    {
        var g = new GameObject("Gurney");
        g.transform.SetParent(parent, false);
        g.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cube, g.transform, metalMat, "Top", new Vector3(0f, 0.85f, 0f), new Vector3(2f, 0.12f, 0.8f));
        NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cube, g.transform, metalMat, "Legs", new Vector3(0f, 0.4f, 0f), new Vector3(1.6f, 0.8f, 0.5f));
        AddVaultTrigger(g, new Vector3(2.6f, 2.2f, 2.4f), 1.1f, "Press E to vault");
    }

    private static void Counter(Transform parent, Vector3 pos, float length)
    {
        var c = new GameObject("Counter");
        c.transform.SetParent(parent, false);
        c.transform.position = pos;
        NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cube, c.transform, counterMat, "Body", new Vector3(0f, 0.55f, 0f), new Vector3(length, 1.1f, 1f));
        NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cube, c.transform, sheetMat, "Top", new Vector3(0f, 1.13f, 0f), new Vector3(length + 0.2f, 0.06f, 1.2f));
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

    private static void Slab(Transform parent, Vector3 centerXZ, Vector2 size, string name)
    {
        NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cube, parent, slabMat, name,
            new Vector3(centerXZ.x, SlabTopY - SlabThickness * 0.5f, centerXZ.z), new Vector3(size.x, SlabThickness, size.y));
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
        NetworkScaffoldSetup.CreateSolidPart(PrimitiveType.Cube, parent, rampMat, name, center,
            new Vector3(width, 0.25f, length), new Vector3(pitch, yaw, 0f));
    }

    private static void PointLight(Transform parent, Vector3 pos, Color color, float intensity, float range)
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
    }
}
