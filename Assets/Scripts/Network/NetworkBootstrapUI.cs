using Unity.Netcode;
using UnityEngine;

public class NetworkBootstrapUI : MonoBehaviour
{
    private void OnGUI()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null) return;

        GUILayout.BeginArea(new Rect(10, 10, 200, 150));

        if (!nm.IsClient && !nm.IsServer)
        {
            if (GUILayout.Button("Host")) nm.StartHost();
            if (GUILayout.Button("Client")) nm.StartClient();
            if (GUILayout.Button("Server")) nm.StartServer();
        }
        else
        {
            string mode = nm.IsHost ? "Host" : nm.IsServer ? "Server" : "Client";
            GUILayout.Label($"Mode: {mode}");
        }

        GUILayout.EndArea();
    }
}
