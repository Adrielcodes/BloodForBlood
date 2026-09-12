using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// DBD-style radial skill check: a needle sweeps 360 degrees once over `Duration` seconds around a
// randomly-placed highlighted zone. Pressing E while the needle is inside the zone succeeds;
// pressing outside it, or letting the sweep finish with no press at all, fails. Purely local/
// client-side (no networking here) — the caller (RestoreBeacon) reports only the boolean result to
// the server, the same trust model as owner-written NetworkVariables elsewhere in this project.
public static class SkillCheckController
{
    private const float Duration = 1.1f;
    private const float ZoneDegrees = 30f;

    private static Sprite circleSprite;

    public static IEnumerator Run(Action<bool> onResult)
    {
        var canvasGO = new GameObject("SkillCheckCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var ringGO = new GameObject("Ring", typeof(Image));
        ringGO.transform.SetParent(canvasGO.transform, false);
        RectTransform ringRect = ringGO.GetComponent<RectTransform>();
        ringRect.anchorMin = new Vector2(0.5f, 0.5f);
        ringRect.anchorMax = new Vector2(0.5f, 0.5f);
        ringRect.pivot = new Vector2(0.5f, 0.5f);
        ringRect.sizeDelta = new Vector2(180f, 180f);
        Image ring = ringGO.GetComponent<Image>();
        ring.sprite = GetCircleSprite();
        ring.color = new Color(0f, 0f, 0f, 0.55f);

        // Where the "good" zone sits is randomized per skill check; both the zone's placement and
        // the needle's sweep below use the same negative-Z-rotation convention, so the numeric
        // hit-test at the bottom stays consistent with whatever visually reads as the needle
        // "entering" the zone.
        float zoneStartAngle = UnityEngine.Random.Range(0f, 360f - ZoneDegrees);

        var zoneGO = new GameObject("Zone", typeof(Image));
        zoneGO.transform.SetParent(ringGO.transform, false);
        RectTransform zoneRect = zoneGO.GetComponent<RectTransform>();
        zoneRect.anchorMin = Vector2.zero;
        zoneRect.anchorMax = Vector2.one;
        zoneRect.offsetMin = Vector2.zero;
        zoneRect.offsetMax = Vector2.zero;
        Image zone = zoneGO.GetComponent<Image>();
        zone.sprite = GetCircleSprite();
        zone.color = new Color(0.85f, 0.75f, 0.2f, 0.95f);
        zone.type = Image.Type.Filled;
        zone.fillMethod = Image.FillMethod.Radial360;
        zone.fillOrigin = (int)Image.Origin360.Top;
        zone.fillClockwise = true;
        zone.fillAmount = ZoneDegrees / 360f;
        zoneRect.localRotation = Quaternion.Euler(0f, 0f, -zoneStartAngle);

        var needleGO = new GameObject("Needle", typeof(Image));
        needleGO.transform.SetParent(ringGO.transform, false);
        RectTransform needleRect = needleGO.GetComponent<RectTransform>();
        needleRect.anchorMin = new Vector2(0.5f, 0.5f);
        needleRect.anchorMax = new Vector2(0.5f, 0.5f);
        needleRect.pivot = new Vector2(0.5f, 0f);
        needleRect.anchoredPosition = Vector2.zero;
        needleRect.sizeDelta = new Vector2(5f, 85f);
        Image needle = needleGO.GetComponent<Image>();
        needle.sprite = HudBar.GetSolidSprite();
        needle.color = Color.white;

        Sfx.Play2D("SkillCheckWarn", 0.8f);

        float elapsed = 0f;
        bool resolved = false;
        bool success = false;

        while (elapsed < Duration && !resolved)
        {
            elapsed += Time.deltaTime;
            float angle = (elapsed / Duration) * 360f;
            needleRect.localRotation = Quaternion.Euler(0f, 0f, -angle);

            Keyboard kb = Keyboard.current;
            if (kb != null && kb.eKey.wasPressedThisFrame)
            {
                float diff = Mathf.Abs(Mathf.DeltaAngle(angle % 360f, zoneStartAngle + ZoneDegrees * 0.5f));
                success = diff <= ZoneDegrees * 0.5f;
                resolved = true;
            }

            yield return null;
        }

        UnityEngine.Object.Destroy(canvasGO);
        Sfx.Play2D(success ? "SkillCheckSuccess" : "SkillCheckFail", 0.8f);
        onResult?.Invoke(success);
    }

    // Procedurally generated so the gauge reads as an actual circle rather than a square — none
    // of this project's other runtime UI needed a non-rectangular sprite before.
    private static Sprite GetCircleSprite()
    {
        if (circleSprite == null)
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float radius = size / 2f - 2f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    texture.SetPixel(x, y, dist <= radius ? Color.white : new Color(1f, 1f, 1f, 0f));
                }
            }

            texture.Apply();
            circleSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        }

        return circleSprite;
    }
}
