using UnityEngine;
using UnityEngine.UI;

// Simple centered reticle for the locally owned character — both roles attack via left-click
// with no other aim feedback currently on screen. Created once per owner from
// NetworkedCharacterMotor.OnNetworkSpawn, same runtime-built approach as the HUDs.
public static class CrosshairController
{
    private static GameObject instance;

    public static void Create()
    {
        if (instance != null) return;

        var canvasGO = new GameObject("Crosshair");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var dot = new GameObject("Dot", typeof(Image));
        dot.transform.SetParent(canvasGO.transform, false);
        RectTransform dotRect = dot.GetComponent<RectTransform>();
        dotRect.anchorMin = new Vector2(0.5f, 0.5f);
        dotRect.anchorMax = new Vector2(0.5f, 0.5f);
        dotRect.pivot = new Vector2(0.5f, 0.5f);
        dotRect.sizeDelta = new Vector2(6f, 6f);
        dot.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.85f);

        instance = canvasGO;
    }
}
