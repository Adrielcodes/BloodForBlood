using System.IO;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class NetworkScaffoldSetup
{
    private const string PrefabDir = "Assets/Prefabs";

    [MenuItem("Blood For Blood/Setup Network Scaffolding")]
    public static void Setup()
    {
        CreateGroundPlane();
        GameObject survivorPrefab = CreateSurvivorPrefab();
        GameObject killerPrefab = CreateKillerPrefab();
        CreateOrUpdateNetworkManager(survivorPrefab, killerPrefab);

        GameObject weaponPickupPrefab = CreateWeaponPickupPrefab();
        PlaceWeaponPickupInScene(weaponPickupPrefab);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

        Debug.Log("Blood For Blood: network scaffolding created (Ground + NetworkManager + Survivor/Killer prefabs + WeaponPickup).");
    }

    private static void CreateGroundPlane()
    {
        if (GameObject.Find("Ground") != null)
        {
            Debug.Log("Blood For Blood: Ground already present in scene, skipping.");
            return;
        }

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.position = Vector3.zero;
        ground.transform.localScale = new Vector3(5f, 1f, 5f);
    }

    private const string CharactersDir = "Assets/Art/Characters";

    private static GameObject CreateSurvivorPrefab()
    {
        return CreateRolePrefab<SurvivorController>(
            "Survivor",
            $"{CharactersDir}/SurvivorMale.fbx",
            Vector3.one,
            skinColor: new Color(0.92f, 0.75f, 0.64f),
            outfitColor: new Color(0.55f, 0.16f, 0.2f),
            hairColor: new Color(0.25f, 0.15f, 0.1f),
            isKiller: false);
    }

    private static GameObject CreateKillerPrefab()
    {
        return CreateRolePrefab<KillerController>(
            "Killer",
            $"{CharactersDir}/ZombieGirl.fbx",
            Vector3.one * 1.2f,
            skinColor: new Color(0.72f, 0.7f, 0.68f),
            outfitColor: new Color(0.12f, 0.1f, 0.12f),
            hairColor: new Color(0.05f, 0.05f, 0.05f),
            isKiller: true);
    }

    private static GameObject CreateRolePrefab<TController>(
        string prefabName, string modelPath, Vector3 visualScale,
        Color skinColor, Color outfitColor, Color hairColor, bool isKiller)
        where TController : NetworkedCharacterMotor
    {
        if (!Directory.Exists(PrefabDir))
            Directory.CreateDirectory(PrefabDir);

        string prefabPath = $"{PrefabDir}/{prefabName}.prefab";

        var root = new GameObject(prefabName);
        root.transform.position = new Vector3(0f, 1f, 0f);
        root.AddComponent<CharacterController>();
        root.AddComponent<NetworkObject>();
        root.AddComponent<OwnerNetworkTransform>();
        root.AddComponent<TController>();

        GameObject visual = BuildModelVisual(root.transform, modelPath, prefabName);
        if (visual == null)
        {
            Debug.LogWarning($"Blood For Blood: model not found at {modelPath}, using primitive placeholder for {prefabName}.");
            visual = BuildHumanoidVisual(root.transform, skinColor, outfitColor, hairColor, isKiller);
        }
        visual.transform.localScale = visualScale;

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
    }

    private static GameObject BuildModelVisual(Transform parent, string modelPath, string prefabName)
    {
        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (modelAsset == null)
            return null;

        GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
        visual.name = "Visual";
        visual.transform.SetParent(parent, false);
        // Mixamo characters are exported with their root at the feet; CharacterController's
        // collision capsule bottom sits at local y = -1 (default height 2, center 0), so align to that.
        visual.transform.localPosition = new Vector3(0f, -1f, 0f);

        if (visual.GetComponent<Animator>() == null)
            visual.AddComponent<Animator>();

        return visual;
    }

    private static GameObject BuildHumanoidVisual(Transform parent, Color skinColor, Color outfitColor, Color hairColor, bool isKiller)
    {
        var visual = new GameObject("Visual");
        visual.transform.SetParent(parent, false);

        Material skinMat = CreateColorMaterial("SkinMat", skinColor);
        Material outfitMat = CreateColorMaterial("OutfitMat", outfitColor);
        Material hairMat = CreateColorMaterial("HairMat", hairColor);

        CreatePart(PrimitiveType.Sphere, visual.transform, skinMat, "Head",
            new Vector3(0f, 0.78f, 0f), new Vector3(0.36f, 0.36f, 0.36f));

        CreatePart(PrimitiveType.Capsule, visual.transform, hairMat, "Hair",
            new Vector3(0f, 0.6f, -0.08f), new Vector3(0.18f, 0.22f, 0.18f), new Vector3(15f, 0f, 0f));

        CreatePart(PrimitiveType.Capsule, visual.transform, outfitMat, "Torso",
            new Vector3(0f, 0.45f, 0f), new Vector3(0.44f, 0.3f, 0.3f));

        CreatePart(PrimitiveType.Cylinder, visual.transform, outfitMat, "Skirt",
            new Vector3(0f, 0.16f, 0f), new Vector3(0.56f, 0.12f, 0.56f));

        CreatePart(PrimitiveType.Capsule, visual.transform, skinMat, "ArmLeft",
            new Vector3(-0.3f, 0.35f, 0f), new Vector3(0.14f, 0.26f, 0.14f));
        CreatePart(PrimitiveType.Capsule, visual.transform, skinMat, "ArmRight",
            new Vector3(0.3f, 0.35f, 0f), new Vector3(0.14f, 0.26f, 0.14f));

        CreatePart(PrimitiveType.Capsule, visual.transform, skinMat, "LegLeft",
            new Vector3(-0.12f, -0.5f, 0f), new Vector3(0.17f, 0.45f, 0.17f));
        CreatePart(PrimitiveType.Capsule, visual.transform, skinMat, "LegRight",
            new Vector3(0.12f, -0.5f, 0f), new Vector3(0.17f, 0.45f, 0.17f));

        if (isKiller)
        {
            Material maskMat = CreateColorMaterial("MaskMat", new Color(0.9f, 0.88f, 0.85f));
            CreatePart(PrimitiveType.Cube, visual.transform, maskMat, "Mask",
                new Vector3(0f, 0.78f, 0.16f), new Vector3(0.3f, 0.32f, 0.08f));
        }

        return visual;
    }

    private static GameObject CreatePart(
        PrimitiveType type, Transform parent, Material material, string name,
        Vector3 localPos, Vector3 localScale, Vector3? localEuler = null)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPos;
        part.transform.localScale = localScale;
        part.transform.localRotation = localEuler.HasValue ? Quaternion.Euler(localEuler.Value) : Quaternion.identity;

        Collider collider = part.GetComponent<Collider>();
        if (collider != null)
            Object.DestroyImmediate(collider);

        Renderer renderer = part.GetComponent<Renderer>();
        if (renderer != null)
            renderer.sharedMaterial = material;

        return part;
    }

    private static Material CreateColorMaterial(string name, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(shader) { name = name, color = color };
        return mat;
    }

    private static GameObject CreateWeaponPickupPrefab()
    {
        if (!Directory.Exists(PrefabDir))
            Directory.CreateDirectory(PrefabDir);

        string prefabPath = $"{PrefabDir}/WeaponPickup.prefab";

        // NetworkObject is enough — NGO auto-registers it into Assets/DefaultNetworkPrefabs.asset.
        // Do not add an explicit NetworkPrefabsList entry (see CreateOrUpdateNetworkManager below).
        var root = new GameObject("WeaponPickup");
        root.AddComponent<NetworkObject>();

        SphereCollider trigger = root.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = 1.5f;

        // Kinematic Rigidbody ensures OnTrigger callbacks fire reliably against the Survivor's CharacterController.
        Rigidbody rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        root.AddComponent<WeaponPickup>();

        BuildSwordVisual(root.transform);

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
    }

    private static void BuildSwordVisual(Transform parent)
    {
        var visual = new GameObject("Visual");
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
    }

    private static void PlaceWeaponPickupInScene(GameObject prefab)
    {
        if (GameObject.Find("WeaponPickup") != null)
        {
            Debug.Log("Blood For Blood: WeaponPickup already present in scene, skipping placement.");
            return;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.transform.position = new Vector3(3f, 1f, 5f);
    }

    private static void CreateOrUpdateNetworkManager(GameObject survivorPrefab, GameObject killerPrefab)
    {
        NetworkManager existing = Object.FindAnyObjectByType<NetworkManager>();
        GameObject nmGO = existing != null ? existing.gameObject : null;

        if (nmGO == null)
        {
            nmGO = new GameObject("NetworkManager");
            NetworkManager newManager = nmGO.AddComponent<NetworkManager>();
            UnityTransport transport = nmGO.AddComponent<UnityTransport>();

            newManager.NetworkConfig.NetworkTransport = transport;
            transport.ConnectionData.Address = "127.0.0.1";
            transport.ConnectionData.Port = 7777;
        }

        NetworkManager manager = nmGO.GetComponent<NetworkManager>();
        manager.NetworkConfig.PlayerPrefab = survivorPrefab;
        manager.NetworkConfig.ConnectionApproval = true;

        // Any NetworkObject-bearing prefab (Survivor, Killer) is auto-registered by NGO into
        // Assets/DefaultNetworkPrefabs.asset, which is auto-wired into NetworkPrefabsLists already —
        // do not add a second explicit list here, it duplicates the same hash and NGO errors on Awake.
        // Clean up any stale/missing list reference left over from a prior misconfiguration.
        manager.NetworkConfig.Prefabs.NetworkPrefabsLists.RemoveAll(l => l == null);

        if (nmGO.GetComponent<NetworkBootstrapUI>() == null)
            nmGO.AddComponent<NetworkBootstrapUI>();

        RoleAssignmentManager roleAssigner = nmGO.GetComponent<RoleAssignmentManager>();
        if (roleAssigner == null)
            roleAssigner = nmGO.AddComponent<RoleAssignmentManager>();
        roleAssigner.Configure(survivorPrefab, killerPrefab);

        EditorUtility.SetDirty(nmGO);
    }
}
