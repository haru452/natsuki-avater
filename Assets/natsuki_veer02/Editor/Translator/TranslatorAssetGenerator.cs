using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Generates the block, face and sign textures and materials.</summary>
public static class TranslatorAssetGenerator
{
    const string SignPng = "Sign.png";
    const int FaceSize = 256;
    const int GlyphHeight = 64;
    const int GlyphWidth = 32;
    const int SignPadding = 24;
    const int SignCorner = 20;

    /// <summary>Plain coloured cube material (Standard shader, so no lilToon needed).</summary>
    public static Material BlockMaterial(string outputDir)
    {
        var mat = new Material(Shader.Find("Standard")) { color = TranslatorConfig.BlockColor };
        mat.SetFloat("_Glossiness", 0.2f);
        return SaveMaterial(mat, outputDir + "/Pet_Block.mat");
    }

    /// <summary>Two eyes on a transparent background, put on the cube's front face.</summary>
    public static Material FaceMaterial(string outputDir)
    {
        var tex = new Texture2D(FaceSize, FaceSize, TextureFormat.RGBA32, false);
        var eye = new Color(0.12f, 0.1f, 0.1f, 1f);
        var shine = Color.white;
        for (int y = 0; y < FaceSize; y++)
        for (int x = 0; x < FaceSize; x++)
        {
            var c = Color.clear;
            foreach (int cx in new[] { 80, 176 })
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, 140));
                if (d < 22) c = eye;
                if (Vector2.Distance(new Vector2(x, y), new Vector2(cx + 7, 148)) < 7) c = shine;
            }
            tex.SetPixel(x, y, c);
        }
        var saved = SavePng(tex, outputDir + "/Pet_Face.png", overwrite: true);
        return SaveMaterial(UnlitTransparent(saved), outputDir + "/Pet_Face.mat");
    }

    /// <summary>
    /// The "using a translator" sign. The PNG is drawn once from KAT's glyph sheet; an existing
    /// Sign.png is kept so a hand-made replacement survives regenerating.
    /// </summary>
    public static Material SignMaterial(string outputDir)
    {
        string pngPath = outputDir + "/" + SignPng;
        var tex = File.Exists(pngPath)
            ? AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath)
            : SavePng(DrawSign(), pngPath, overwrite: false);
        return SaveMaterial(UnlitTransparent(tex), outputDir + "/Sign.mat");
    }

    /// <summary>Board height for a board <see cref="TranslatorConfig.SignWidth"/> wide.</summary>
    public static float SignAspect(Texture tex) => tex == null ? 0.2f : (float)tex.height / tex.width;

    static Texture2D DrawSign()
    {
        var sheet = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        sheet.LoadImage(File.ReadAllBytes(TranslatorConfig.KatTilesPath));
        try
        {
            var lines = TranslatorConfig.SignLines;
            int cols = 0;
            foreach (var l in lines) cols = Mathf.Max(cols, l.Length);
            int w = cols * GlyphWidth + SignPadding * 2;
            int h = lines.Length * GlyphHeight + SignPadding * 2;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var bg = new Color(0.05f, 0.05f, 0.1f, 0.75f);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, InsideRounded(x, y, w, h) ? bg : Color.clear);

            for (int row = 0; row < lines.Length; row++)
            {
                string line = lines[row];
                int left = SignPadding + (cols - line.Length) * GlyphWidth / 2;
                int top = h - SignPadding - row * GlyphHeight; // texture y grows upward
                for (int i = 0; i < line.Length; i++)
                    DrawGlyph(tex, sheet, line[i] - 32, left + i * GlyphWidth, top - GlyphHeight);
            }
            return tex;
        }
        finally
        {
            Object.DestroyImmediate(sheet);
        }
    }

    static bool InsideRounded(int x, int y, int w, int h)
    {
        int cx = Mathf.Clamp(x, SignCorner, w - 1 - SignCorner);
        int cy = Mathf.Clamp(y, SignCorner, h - 1 - SignCorner);
        return (x - cx) * (x - cx) + (y - cy) * (y - cy) <= SignCorner * SignCorner;
    }

    /// <summary>Copies one 16x16-grid tile (code 0 = top-left) from the sheet, scaled into a glyph cell.</summary>
    static void DrawGlyph(Texture2D dst, Texture2D sheet, int code, int x0, int y0)
    {
        if (code <= 0 || code > 255) return;
        float u0 = (code % 16) / 16f;
        float v1 = 1f - (code / 16) / 16f;
        for (int y = 0; y < GlyphHeight; y++)
        for (int x = 0; x < GlyphWidth; x++)
        {
            float u = u0 + (x + 0.5f) / GlyphWidth / 16f;
            float v = v1 - 1f / 16f + (y + 0.5f) / GlyphHeight / 16f;
            float a = sheet.GetPixelBilinear(u, v).a;
            if (a <= 0f) continue;
            var under = dst.GetPixel(x0 + x, y0 + y);
            dst.SetPixel(x0 + x, y0 + y, Color.Lerp(under, Color.white, a));
        }
    }

    static Material UnlitTransparent(Texture tex) => new Material(Shader.Find("Unlit/Transparent")) { mainTexture = tex };

    static Texture2D SavePng(Texture2D tex, string path, bool overwrite)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        if (overwrite || !File.Exists(path)) File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = true;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material SaveMaterial(Material mat, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
        // Copy into the existing asset so its GUID (and every reference to it) stays the same.
        existing.shader = mat.shader;
        existing.CopyPropertiesFromMaterial(mat);
        Object.DestroyImmediate(mat);
        EditorUtility.SetDirty(existing);
        return existing;
    }
}
