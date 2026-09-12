using System.IO;
using UnityEditor;
using UnityEngine;

// Rasterizes text into a stylized logo sprite (gritty blood-red title with drips and glow; clean
// pale studio mark with an underline) so the boot screens don't have to rely on plain UI Text.
// Glyphs are composed by hand from the built-in font's dynamic atlas — blitting the atlas through
// a RenderTexture sidesteps its non-readable flag, and avoids rendering a legacy GUI/Text shader
// through a URP camera, which isn't reliable. Output PNGs live in Assets/Art/UI (generated once).
internal static class LogoRenderer
{
    private const string Dir = "Assets/Art/UI";

    public static Sprite TitleLogo() => Get("TitleLogo", "BLOOD FOR BLOOD", 1024, 320, true);
    public static Sprite StudioLogo() => Get("StudioLogo", "AGAPE FORGE", 1024, 220, false);

    private static Sprite Get(string name, string text, int w, int h, bool bloody)
    {
        string path = $"{Dir}/{name}.png";
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing != null) return existing;

        float[] mask = RenderTextMask(text, w, h, bloody ? 0.9f : 0.78f, bloody ? 0.06f : 0.16f);
        if (mask == null) return null;

        Color[] px = bloody ? StylizeBloody(mask, w, h, name.GetHashCode()) : StylizeStudio(mask, w, h);

        if (!AssetDatabase.IsValidFolder("Assets/Art")) AssetDatabase.CreateFolder("Assets", "Art");
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Art", "UI");
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.SetPixels(px);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ------------------------------------------------------------------ text rasterization

    private static float[] RenderTextMask(string text, int w, int h, float fillWidth, float letterSpacing)
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        const FontStyle style = FontStyle.Bold;

        int size = 100;
        font.RequestCharactersInTexture(text, size, style);
        float width = MeasureWidth(font, text, size, style, letterSpacing);
        if (width <= 0f) return null;
        size = Mathf.Clamp(Mathf.RoundToInt(size * (w * fillWidth) / width), 20, 240);
        font.RequestCharactersInTexture(text, size, style);
        width = MeasureWidth(font, text, size, style, letterSpacing);

        Texture atlas = font.material.mainTexture;
        var rt = RenderTexture.GetTemporary(atlas.width, atlas.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(atlas, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        var readable = new Texture2D(atlas.width, atlas.height, TextureFormat.RGBA32, false);
        readable.ReadPixels(new Rect(0, 0, atlas.width, atlas.height), 0, 0);
        readable.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        Color[] atlasPx = readable.GetPixels();
        int aw = atlas.width, ah = atlas.height;
        Object.DestroyImmediate(readable);

        var mask = new float[w * h];
        float penX = (w - width) * 0.5f;
        float baseline = h * 0.5f - size * 0.32f;
        bool any = false;

        foreach (char c in text)
        {
            if (!font.GetCharacterInfo(c, out CharacterInfo ci, size, style)) continue;
            int gx0 = Mathf.RoundToInt(penX + ci.minX), gx1 = Mathf.RoundToInt(penX + ci.maxX);
            int gy0 = Mathf.RoundToInt(baseline + ci.minY), gy1 = Mathf.RoundToInt(baseline + ci.maxY);
            for (int y = gy0; y < gy1; y++)
            for (int x = gx0; x < gx1; x++)
            {
                if (x < 0 || y < 0 || x >= w || y >= h) continue;
                float fx = (x + 0.5f - gx0) / Mathf.Max(1, gx1 - gx0);
                float fy = (y + 0.5f - gy0) / Mathf.Max(1, gy1 - gy0);
                Vector2 uv = ci.uvBottomLeft + (ci.uvBottomRight - ci.uvBottomLeft) * fx + (ci.uvTopLeft - ci.uvBottomLeft) * fy;
                int ax = Mathf.Clamp((int)(uv.x * aw), 0, aw - 1), ay = Mathf.Clamp((int)(uv.y * ah), 0, ah - 1);
                Color s = atlasPx[ay * aw + ax];
                float v = Mathf.Max(s.a, s.r);
                if (v > 0.05f) any = true;
                mask[y * w + x] = Mathf.Max(mask[y * w + x], v);
            }
            penX += ci.advance + size * letterSpacing;
        }
        return any ? mask : null;
    }

    private static float MeasureWidth(Font font, string text, int size, FontStyle style, float letterSpacing)
    {
        float width = 0f;
        foreach (char c in text)
            if (font.GetCharacterInfo(c, out CharacterInfo ci, size, style)) width += ci.advance + size * letterSpacing;
        return width;
    }

    // ------------------------------------------------------------------ styles

    private static Color[] StylizeBloody(float[] mask, int w, int h, int seed)
    {
        var rng = new System.Random(seed);
        var letters = (float[])mask.Clone();

        // Drips: from the bottom edge of glyphs, a few columns run downward and taper.
        for (int x = 2; x < w - 2; x++)
        {
            if (rng.NextDouble() > 0.045) continue;
            int bottom = -1;
            for (int y = 0; y < h; y++) if (mask[y * w + x] > 0.5f) { bottom = y; break; }
            if (bottom < 0) continue;
            int length = 12 + rng.Next(70);
            int width = 2 + rng.Next(3);
            for (int d = 0; d < length; d++)
            {
                int y = bottom - d;
                if (y < 0) break;
                float taper = 1f - (float)d / length;
                int ww = Mathf.Max(1, Mathf.RoundToInt(width * taper));
                for (int k = -ww; k <= ww; k++)
                    if (x + k >= 0 && x + k < w) letters[y * w + x + k] = Mathf.Max(letters[y * w + x + k], 0.95f);
            }
            int by = bottom - length;
            if (by >= 1) for (int k = -2; k <= 2; k++) if (x + k >= 0 && x + k < w) letters[by * w + x + k] = 1f;
        }

        float[] glow = Blur(letters, w, h, 7);
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = y * w + x;
            float m = letters[i];
            float grunge = 0.72f + 0.28f * Mathf.PerlinNoise(x * 0.06f, y * 0.06f) * Mathf.PerlinNoise(x * 0.013f + 4f, y * 0.013f);
            float t = (float)y / h;
            Color body = Color.Lerp(new Color(0.42f, 0.01f, 0.02f), new Color(0.82f, 0.08f, 0.06f), t) * grunge;
            float edge = Mathf.Clamp01((m - 0.35f) / 0.2f);
            Color dark = new Color(0.12f, 0f, 0f);
            Color letter = Color.Lerp(dark, body, edge);
            float g = glow[i] * 0.75f;
            Color glowC = new Color(0.25f, 0.01f, 0.01f, g);
            float a = Mathf.Max(m, g);
            Color outC = m > 0.02f ? Color.Lerp(glowC, letter, Mathf.Clamp01(m / 0.6f)) : glowC;
            outC.a = a;
            px[i] = outC;
        }
        return px;
    }

    private static Color[] StylizeStudio(float[] mask, int w, int h)
    {
        float[] glow = Blur(mask, w, h, 9);
        int minX = w, maxX = 0, minY = h;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            if (mask[y * w + x] > 0.4f) { minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); }

        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = y * w + x;
            float m = mask[i];
            float g = glow[i] * 0.45f;
            float line = 0f;
            if (minY < h && y >= minY - 18 && y <= minY - 15 && x >= minX && x <= maxX)
            {
                float ex = Mathf.Min(x - minX, maxX - x) / 80f;
                line = Mathf.Clamp01(ex);
            }
            Color c = new Color(0.9f, 0.91f, 0.94f);
            Color glowC = new Color(0.55f, 0.8f, 0.85f, g);
            Color outC = Color.Lerp(glowC, c, Mathf.Clamp01(m / 0.5f));
            outC.a = Mathf.Max(Mathf.Max(m, g), line * 0.8f);
            if (line > 0f && m < 0.1f) outC = new Color(0.75f, 0.1f, 0.1f, line * 0.9f);
            px[i] = outC;
        }
        return px;
    }

    private static float[] Blur(float[] src, int w, int h, int radius)
    {
        var tmp = new float[w * h];
        var dst = new float[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float sum = 0f; int n = 0;
            for (int k = -radius; k <= radius; k++) { int xx = x + k; if (xx >= 0 && xx < w) { sum += src[y * w + xx]; n++; } }
            tmp[y * w + x] = sum / n;
        }
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float sum = 0f; int n = 0;
            for (int k = -radius; k <= radius; k++) { int yy = y + k; if (yy >= 0 && yy < h) { sum += tmp[yy * w + x]; n++; } }
            dst[y * w + x] = sum / n;
        }
        return dst;
    }
}
