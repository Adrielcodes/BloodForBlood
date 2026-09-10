using System.IO;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class NetworkScaffoldSetup
{
    [MenuItem("Blood For Blood/Setup Network Scaffolding")]
    public static void Setup()
    {
        GameObject playerPrefab = CreatePlayerPrefab();
        CreateNetworkManager(playerPrefab);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());

        Debug.Log("Blood For Blood: network scaffolding created (NetworkManager + Player prefab).");
    }

    private static GameObject CreatePlayerPrefab()
    {
        const string prefabDir = "Assets/Prefabs";
        const string prefabPath = prefabDir + "/Player.prefab";

        if (!Directory.Exists(prefabDir))
            Directory.CreateDirectory(prefabDir);

        var root = new GameObject("Player");
        root.AddComponent<CharacterController>();
        root.AddComponent<NetworkObject>();
        root.AddComponent<NetworkTransform>();
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
        if (Object.FindFirstObjectByType<NetworkManager>() != null)
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
