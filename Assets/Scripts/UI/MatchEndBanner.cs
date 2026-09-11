using UnityEngine;
using UnityEngine.UI;

// Built at runtime the same way as the HUDs, but shown on every client (not owner-gated) — see
// MatchManager, which calls Show() from its Result.OnValueChanged handler for both server and
// client instances, since the match outcome matters to everyone watching, not just one side.
public static class MatchEndBanner
{
    private static GameObject instance;

    public static void Show(string message)
    {
        if (instance != null) return;

        var canvasGO = new GameObject("MatchEndBanner");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var overlay = new GameObject("Overlay", typeof(Image));
        overlay.transform.SetParent(canvasGO.transform, false);
        RectTransform overlayRect = overlay.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        overlay.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

        var textGO = new GameObject("Text", typeof(Text));
        textGO.transform.SetParent(canvasGO.transform, false);
        RectTransform textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.5f, 0.5f);
        textRect.anchorMax = new Vector2(0.5f, 0.5f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.sizeDelta = new Vector2(1200f, 220f);

        Text text = textGO.GetComponent<Text>();
        text.text = message;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 80;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;

        instance = canvasGO;
    }
}
