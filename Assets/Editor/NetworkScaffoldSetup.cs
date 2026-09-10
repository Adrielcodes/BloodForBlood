using System.IO;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class NetworkScaffoldSetup
{
    [MenuItem("Blood For Blood/Setup Network Scaffolding")]
    public static void Setup()
    {
        CreateGroundPlane();
        GameObject playerPrefab = CreatePlayerPrefab();
        CreateNetworkManager(playerPrefab);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

        Debug.Log("Blood For Blood: network scaffolding created (Ground + NetworkManager + Player prefab).");
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

    private static GameObject CreatePlayerPrefab()
    {
        const string prefabDir = "Assets/Prefabs";
        const string prefabPath = prefabDir + "/Player.prefab";

        if (!Directory.Exists(prefabDir))
            Directory.CreateDirectory(prefabDir);

        var root = new GameObject("Player");
        root.transform.position = new Vector3(0f, 1f, 0f);
        root.AddComponent<CharacterController>();
        root.AddComponent<NetworkObject>();
        root.AddComponent<OwnerNetworkTransform>();
        root.AddComponent<NetworkedPlayerController>();

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        visual.name = "Visual";
        visual.transform.SetParent(root.transform, false);
        Object.DestroyImmediate(visual.GetComponent<CapsuleCollider>());

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
    }

    private static void CreateNetworkManager(GameObject playerPrefab)
    {
        if (Object.FindAnyObjectByType<NetworkManager>() != null)
        {
            Debug.Log("Blood For Blood: NetworkManager already present in scene, skipping.");
            return;
        }

        var nmGO = new GameObject("NetworkManager");
        NetworkManager nm = nmGO.AddComponent<NetworkManager>();
        UnityTransport transport = nmGO.AddComponent<UnityTransport>();
        nmGO.AddComponent<NetworkBootstrapUI>();

        nm.NetworkConfig.NetworkTransport = transport;
        nm.NetworkConfig.PlayerPrefab = playerPrefab;

        transport.ConnectionData.Address = "127.0.0.1";
        transport.ConnectionData.Port = 7777;

        EditorUtility.SetDirty(nmGO);
    }
}
