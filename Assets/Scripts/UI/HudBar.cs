using UnityEngine;
using UnityEngine.UI;

// Shared runtime fill-bar builder — factored out of SurvivorHudController once KillerHudController
// needed the exact same background+fill Image setup.
public static class HudBar
{
    private static Sprite solidSprite;

    public static Image Create(Transform parent, string label, Vector2 anchoredPosition, Color fillColor)
    {
        var background = new GameObject(label + "BarBackground", typeof(Image));
        background.transform.SetParent(parent, false);
        RectTransform bgRect = background.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.zero;
        bgRect.pivot = Vector2.zero;
        bgRect.anchoredPosition = anchoredPosition;
        bgRect.sizeDelta = new Vector2(220f, 22f);
        background.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);

        var fill = new GameObject(label + "BarFill", typeof(Image));
        fill.transform.SetParent(background.transform, false);
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = new Vector2(2f, 2f);
        fillRect.offsetMax = new Vector2(-2f, -2f);

        Image fillImage = fill.GetComponent<Image>();
        fillImage.sprite = GetSolidSprite();
        fillImage.color = fillColor;
        fillImage.type = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImage.fillAmount = 1f;

        return fillImage;
    }

    // Image.Type.Filled generates its fill mesh from the assigned Sprite's rect/UV data — with
    // no Sprite (the default for a code-created Image), that geometry computation has nothing to
    // work from and the Image renders as a full solid rectangle regardless of fillAmount. Public
    // so other runtime-built UI (e.g. SkillCheckController's radial gauge) can reuse it too.
    public static Sprite GetSolidSprite()
    {
        if (solidSprite == null)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            solidSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f));
        }
        return solidSprite;
    }
}
