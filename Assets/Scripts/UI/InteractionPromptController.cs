using UnityEngine;
using UnityEngine.UI;

// Generic bottom-of-screen interaction prompt ("Hold E to Restore Beacon", live progress
// percentage while holding) — built lazily on first Show(), same runtime-built pattern as the
// HUDs. Reusable by any future E-to-interact object, not just RestoreBeacon.
public static class InteractionPromptController
{
    private static GameObject instance;
    private static Text label;

    public static void Show(string message)
    {
        if (instance == null) Create();
        label.text = message;
        instance.SetActive(true);
    }

    public static void Hide()
    {
        if (instance != null) instance.SetActive(false);
    }

    private static void Create()
    {
        var canvasGO = new GameObject("InteractionPrompt");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var textGO = new GameObject("Text", typeof(Text));
        textGO.transform.SetParent(canvasGO.transform, false);
        RectTransform rect = textGO.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.22f);
        rect.anchorMax = new Vector2(0.5f, 0.22f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(800f, 80f);

        label = textGO.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 32;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = Color.white;

        instance = canvasGO;
        instance.SetActive(false);
    }
}
