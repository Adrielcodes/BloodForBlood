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

    private static GameObject CreateSurvivorPrefab()
    {
        return CreateRolePrefab<SurvivorController>("Survivor", Vector3.one);
    }

    private static GameObject CreateKillerPrefab()
    {
        return CreateRolePrefab<KillerController>("Killer", Vector3.one * 1.2f);
    }

    private static GameObject CreateRolePrefab<TController>(string prefabName, Vector3 visualScale)
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

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        visual.name = "Visual";
        visual.transform.SetParent(root.transform, false);
        visual.transform.localScale = visualScale;
        Object.DestroyImmediate(visual.GetComponent<CapsuleCollider>());

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
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

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "Visual";
        visual.transform.SetParent(root.transform, false);
        visual.transform.localScale = new Vector3(0.2f, 0.2f, 1f);
        Object.DestroyImmediate(visual.GetComponent<BoxCollider>());

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
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
