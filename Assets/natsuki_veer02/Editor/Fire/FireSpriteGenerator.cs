using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws the textures for the realistic fire: a grayscale flipbook of soft, noise-distorted flame
/// tongues (colour is applied by the particle's Color over Lifetime) and a soft dot for embers.
/// </summary>
public static class FireSpriteGenerator
{
    const string SheetName = "FireSheet.png";
    const string EmberName = "Ember.png";

    public static Texture2D Generate(string outputDir)
    {
        int s = FireConfig.SpriteSize;
        int tx = FireConfig.SpriteTilesX;
        int ty = FireConfig.SpriteTilesY;
        int frames = tx * ty;
        int w = s * tx;
        var pixels = new Color32[w * s * ty];

        for (int f = 0; f < frames; f++)
        {
            // Unity's texture sheet animation reads frames left-to-right, top-to-bottom.
            int ox = (f % tx) * s;
            int oy = (ty - 1 - f / tx) * s;
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float u = (x + 0.5f) / s * 2f - 1f;
                    float v = (y + 0.5f) / s;
                    byte g = (byte)Mathf.RoundToInt(Mathf.Clamp01(Flame(u, v, f)) * 255f);
                    pixels[(oy + y) * w + ox + x] = new Color32(g, g, g, g);
                }
            }
        }
        return Save(outputDir, SheetName, w, s * ty, pixels);
    }

    public static Texture2D GenerateEmber(string outputDir)
    {
        int s = FireConfig.EmberSize;
        var pixels = new Color32[s * s];
        for (int y = 0; y < s; y++)
        {
            for (int x = 0; x < s; x++)
            {
                float dx = (x + 0.5f) / s * 2f - 1f;
                float dy = (y + 0.5f) / s * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = r >= 1f ? 0f : Mathf.Pow(1f - Smooth(0f, 1f, r), 2f);
                byte b = (byte)Mathf.RoundToInt(a * 255f);
                pixels[y * s + x] = new Color32(255, 255, 255, b);
            }
        }
        return Save(outputDir, EmberName, s, s, pixels);
    }

    /// <summary>Intensity 0..1 of one flame tongue at (u in -1..1, v in 0..1 bottom-up) for frame f.</summary>
    static float Flame(float u, float v, int f)
    {
        // Noise scrolls upward frame by frame so the tongue flickers and licks.
        float t = f * 0.35f;
        float n = Fbm(u * 2.5f + 11.3f, v * 3f - t);
        float n2 = Fbm(u * 5f + 3.7f, v * 6f - t * 1.7f);

        // Lateral wobble grows towards the tip.
        float uu = u + (n - 0.5f) * 0.6f * v;

        // Wide rounded base narrowing to a tip.
        float rise = Mathf.Sqrt(Mathf.Clamp01(v / 0.15f));
        float width = 0.55f * Mathf.Pow(Mathf.Clamp01(1f - v), 0.8f) * rise + 1e-3f;
        float body = 1f - Smooth(0.5f, 1f, Mathf.Abs(uu) / width);

        // Ragged top edge and soft base.
        float top = 1f - Smooth(0.45f, 1f, v + (n2 - 0.5f) * 0.35f);
        float bottom = Smooth(0f, 0.1f, v);
        // Hot bright centre, cooler dimmer edges.
        float core = 1f - 0.5f * Mathf.Clamp01(Mathf.Abs(uu) / width);
        float i = body * top * bottom * core;
        return Mathf.Pow(i, 0.85f);
    }

    /// <summary>GLSL-style smoothstep: 0 below edge0, 1 above edge1. (Mathf.SmoothStep interpolates values instead.)</summary>
    static float Smooth(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    static float Fbm(float x, float y)
    {
        float sum = 0f, amp = 0.5f, freq = 1f;
        for (int o = 0; o < 4; o++)
        {
            sum += amp * Mathf.PerlinNoise(x * freq, y * freq);
            amp *= 0.5f;
            freq *= 2f;
        }
        return sum / 0.9375f;
    }

    static Texture2D Save(string outputDir, string fileName, int width, int height, Color32[] pixels)
    {
        Directory.CreateDirectory(outputDir);
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.SetPixels32(pixels);
        tex.Apply();
        string path = Path.Combine(outputDir, fileName).Replace("\\", "/");
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = true;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
