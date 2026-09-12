using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Generates the hospital's textures as real PNG assets (albedo + tangent-space normal map per
// surface) under Assets/Art/Environment/Textures. No external art is available in this project,
// and Unity's own asset generation needs a paid subscription, so grime, tiles, rust and wood are
// all synthesized from tileable fbm noise + a few seeded shapes. Generated once, reused by path.
internal static class ProceduralTextures
{
    private const string Dir = "Assets/Art/Environment/Textures";
    private const int Size = 256;

    public readonly struct Surface
    {
        public readonly Texture2D Albedo;
        public readonly Texture2D Normal;
        public Surface(Texture2D albedo, Texture2D normal) { Albedo = albedo; Normal = normal; }
    }

    // ------------------------------------------------------------------ public surfaces

    public static Surface Wall() => Get("Wall", (c, h) =>
    {
        Fill(c, h, (u, v) =>
        {
            float mottle = Fbm(u, v, 4f, 11) - 0.5f;
            float grime = v < 0.35f ? Mathf.SmoothStep(0f, 1f, (0.35f - v) / 0.35f) * (0.4f + 0.6f * Fbm(u, v, 9f, 12)) : 0f;
            float dado = Mathf.Abs(v - 0.55f) < 0.006f ? 1f : 0f;
            Color col = new Color(0.60f, 0.66f, 0.60f) * (1f + mottle * 0.2f) * (1f - grime * 0.5f) * (1f - dado * 0.35f);
            return (col, 0.5f + mottle * 0.3f - dado * 0.4f);
        });
        Cracks(c, h, 1, 21);
    });

    public static Surface FloorTile() => Get("FloorTile", (c, h) =>
    {
        Fill(c, h, (u, v) =>
        {
            const int tiles = 4;
            float fx = u * tiles, fy = v * tiles;
            int tx = (int)fx, ty = (int)fy;
            float gx = fx - tx, gy = fy - ty;
            bool grout = gx < 0.05f || gy < 0.05f;
            float hash = Hash(tx, ty);
            float dirt = Fbm(u, v, 6f, 13);
            if (grout) return (new Color(0.17f, 0.16f, 0.14f), 0f);
            Color tile = Color.Lerp(new Color(0.53f, 0.56f, 0.48f), new Color(0.43f, 0.46f, 0.39f), hash) * (0.82f + 0.3f * dirt);
            if (hash > 0.86f) tile *= 0.7f;
            return (tile, 0.7f + 0.15f * dirt);
        });
        Cracks(c, h, 2, 22);
    });

    public static Surface Ceiling() => Get("Ceiling", (c, h) =>
    {
        Fill(c, h, (u, v) =>
        {
            float gx = Frac(u * 2f), gy = Frac(v * 2f);
            bool line = gx < 0.025f || gy < 0.025f;
            float n = Fbm(u, v, 5f, 14);
            Color col = new Color(0.66f, 0.66f, 0.60f) * (0.8f + 0.3f * n);
            if (line) return (new Color(0.22f, 0.22f, 0.2f), 0f);
            foreach (var (sx, sy, r) in new[] { (0.3f, 0.7f, 0.22f), (0.75f, 0.35f, 0.18f), (0.6f, 0.85f, 0.12f) })
            {
                float d = Mathf.Sqrt((u - sx) * (u - sx) + (v - sy) * (v - sy)) / r;
                if (d < 1f) col = Color.Lerp(col, new Color(0.34f, 0.27f, 0.17f), Mathf.Pow(1f - d, 1.5f) * (0.5f + 0.5f * n));
            }
            return (col, 0.6f + 0.1f * n);
        });
    });

    public static Surface Metal() => Get("Metal", (c, h) =>
    {
        Fill(c, h, (u, v) =>
        {
            float streak = Fbm(u * 0.25f, v * 3f, 8f, 15) - 0.5f;
            Color col = new Color(0.38f, 0.43f, 0.41f) * (1f + streak * 0.25f);
            float rust = 0f;
            foreach (var (sx, sy, r) in new[] { (0.2f, 0.15f, 0.2f), (0.8f, 0.9f, 0.16f), (0.55f, 0.5f, 0.1f), (0.1f, 0.75f, 0.12f) })
            {
                float d = Mathf.Sqrt((u - sx) * (u - sx) + (v - sy) * (v - sy)) / r;
                if (d < 1f) rust = Mathf.Max(rust, Mathf.Pow(1f - d, 1.2f) * Fbm(u, v, 12f, 16));
            }
            col = Color.Lerp(col, new Color(0.42f, 0.2f, 0.09f), rust);
            return (col, 0.6f + streak * 0.1f - rust * 0.3f);
        });
    });

    public static Surface Wood() => Get("Wood", (c, h) =>
    {
        Fill(c, h, (u, v) =>
        {
            const int planks = 3;
            float px = Frac(u * planks);
            int plank = (int)(u * planks);
            if (px < 0.03f) return (new Color(0.12f, 0.08f, 0.05f), 0f);
            float grain = Fbm(u * 6f, v * 0.4f, 7f, 17 + plank) - 0.5f;
            Color col = new Color(0.36f, 0.25f, 0.14f) * (0.85f + 0.3f * Hash(plank, 7)) * (1f + grain * 0.35f);
            return (col, 0.55f + grain * 0.25f);
        });
    });

    public static Surface Concrete() => Get("Concrete", (c, h) =>
    {
        Fill(c, h, (u, v) =>
        {
            float n = Fbm(u, v, 5f, 18);
            float speck = Fbm(u, v, 40f, 19) > 0.62f ? 0.12f : 0f;
            Color col = new Color(0.34f, 0.34f, 0.35f) * (0.8f + 0.35f * n) * (1f - speck);
            return (col, 0.5f + 0.2f * n - speck);
        });
    });

    public static Texture2D BloodSplat() => GetAlpha("BloodSplat", (u, v) =>
    {
        float a = 0f;
        foreach (var (sx, sy, r) in new[] { (0.5f, 0.5f, 0.24f), (0.32f, 0.6f, 0.13f), (0.66f, 0.4f, 0.11f), (0.58f, 0.68f, 0.08f), (0.4f, 0.33f, 0.07f) })
        {
            float d = Mathf.Sqrt((u - sx) * (u - sx) + (v - sy) * (v - sy)) / r;
            float edge = 0.75f + 0.5f * Fbm(u, v, 14f, 23);
            if (d < edge) a = 1f;
        }
        if (u > 0.47f && u < 0.51f && v < 0.5f && v > 0.5f - 0.35f * Fbm(u, 0f, 3f, 24)) a = 1f;
        return new Color(0.30f, 0.015f, 0.015f, a);
    });

    public static Texture2D SoftParticle() => GetAlpha("SoftParticle", (u, v) =>
    {
        float d = Mathf.Clamp01(Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f);
        float a = Mathf.Pow(1f - d, 2.2f);
        return new Color(1f, 1f, 1f, a);
    });

    // ------------------------------------------------------------------ pipeline

    private static Surface Get(string name, Action<Color[], float[]> generate, float normalStrength = 3f)
    {
        string albedoPath = $"{Dir}/{name}.png";
        string normalPath = $"{Dir}/{name}_n.png";
        Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
        Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        if (albedo != null && normal != null) return new Surface(albedo, normal);

        var colors = new Color[Size * Size];
        var heights = new float[Size * Size];
        generate(colors, heights);

        var normals = new Color[Size * Size];
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float hl = heights[Idx(x - 1, y)], hr = heights[Idx(x + 1, y)];
            float hd = heights[Idx(x, y - 1)], hu = heights[Idx(x, y + 1)];
            Vector3 n = new Vector3(-(hr - hl) * normalStrength, -(hu - hd) * normalStrength, 1f).normalized;
            normals[Idx(x, y)] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
        }

        return new Surface(WritePng(albedoPath, colors, false, false), WritePng(normalPath, normals, true, false));
    }

    private static Texture2D GetAlpha(string name, Func<float, float, Color> pixel)
    {
        string path = $"{Dir}/{name}.png";
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;

        var colors = new Color[Size * Size];
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
            colors[Idx(x, y)] = pixel((x + 0.5f) / Size, (y + 0.5f) / Size);
        return WritePng(path, colors, false, true);
    }

    private static Texture2D WritePng(string path, Color[] pixels, bool isNormal, bool hasAlpha)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Art/Environment")) AssetDatabase.CreateFolder("Assets/Art", "Environment");
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Art/Environment", "Textures");

        var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        tex.SetPixels(pixels);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.sRGBTexture = !isNormal;
        importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.alphaIsTransparency = hasAlpha;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ------------------------------------------------------------------ helpers

    private static void Fill(Color[] c, float[] h, Func<float, float, (Color, float)> f)
    {
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            var (col, height) = f((x + 0.5f) / Size, (y + 0.5f) / Size);
            col.a = 1f;
            c[Idx(x, y)] = col;
            h[Idx(x, y)] = height;
        }
    }

    // Random-walk dark hairline cracks, drawn into both albedo and height.
    private static void Cracks(Color[] c, float[] h, int count, int seed)
    {
        var rng = new System.Random(seed);
        for (int i = 0; i < count; i++)
        {
            float x = (float)rng.NextDouble() * Size, y = (float)rng.NextDouble() * Size;
            float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
            int steps = 60 + rng.Next(80);
            for (int s = 0; s < steps; s++)
            {
                angle += ((float)rng.NextDouble() - 0.5f) * 0.9f;
                x += Mathf.Cos(angle); y += Mathf.Sin(angle);
                int idx = Idx(Mathf.RoundToInt(x), Mathf.RoundToInt(y));
                c[idx] *= 0.45f; c[idx].a = 1f;
                h[idx] -= 0.8f;
            }
        }
    }

    private static int Idx(int x, int y) => ((y % Size + Size) % Size) * Size + ((x % Size + Size) % Size);

    private static float Frac(float v) => v - Mathf.Floor(v);

    private static float Hash(int a, int b)
    {
        float v = Mathf.Sin(a * 12.9898f + b * 78.233f) * 43758.5453f;
        return v - Mathf.Floor(v);
    }

    // Tileable fbm: blend four offset samples so the texture repeats without a visible seam.
    private static float Fbm(float u, float v, float scale, int seed)
    {
        float a = Raw(u, v, scale, seed), b = Raw(u - 1f, v, scale, seed), c = Raw(u, v - 1f, scale, seed), d = Raw(u - 1f, v - 1f, scale, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
    }

    private static float Raw(float u, float v, float scale, int seed)
    {
        float sum = 0f, amp = 0.5f, freq = scale;
        for (int i = 0; i < 4; i++)
        {
            sum += amp * Mathf.PerlinNoise(u * freq + seed * 3.17f, v * freq + seed * 1.71f);
            amp *= 0.5f;
            freq *= 2f;
        }
        return sum;
    }
}
