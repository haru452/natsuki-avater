using System.IO;
using UnityEditor;
using UnityEngine;
using C = TranslatorConfig;

/// <summary>
/// The pet: a small round robot built from Unity primitives (no imported model, so it is ours to
/// redistribute). White body and big head, a glossy black face screen with glowing ring eyes,
/// ear discs, stubby arms and feet, and a little chest screen with a dotted smile.
/// Origin is on the ground between the feet; the robot faces +Z like the avatar.
/// </summary>
public static class TranslatorRobotBuilder
{
    public const string EyeLeftName = "EyeL";
    public const string EyeRightName = "EyeR";
    public const string HeadName = "Head";
    public const string VisorName = "Visor";
    public const string BodyName = "Body";

    public static readonly Color White = new Color(0.97f, 0.97f, 0.98f);
    public static readonly Color Dark = new Color(0.04f, 0.05f, 0.07f);
    public static readonly Color Grey = new Color(0.62f, 0.65f, 0.7f);
    public static readonly Color Glow = new Color(0.45f, 0.92f, 1f);

    public struct Materials
    {
        public Material White, Dark, Grey, Eye, Smile;
    }

    public static Materials CreateMaterials(string outputDir) => new Materials
    {
        White = TranslatorAssetGenerator.SaveMaterial(Lit(White, 0.55f), outputDir + "/Robot_White.mat"),
        Dark = TranslatorAssetGenerator.SaveMaterial(Lit(Dark, 0.92f), outputDir + "/Robot_Screen.mat"),
        Grey = TranslatorAssetGenerator.SaveMaterial(Lit(Grey, 0.4f), outputDir + "/Robot_Grey.mat"),
        Eye = Unlit(DrawEye(), outputDir + "/Robot_Eye"),
        Smile = Unlit(DrawSmile(), outputDir + "/Robot_Smile"),
    };

    public static GameObject Build(Transform parent, string outputDir)
    {
        var m = CreateMaterials(outputDir);
        var robot = new GameObject(C.RobotName);
        robot.transform.SetParent(parent, false);
        var t = robot.transform;

        foreach (var side in new[] { -1f, 1f })
        {
            string s = side < 0 ? "L" : "R";
            Part(PrimitiveType.Sphere, "Foot" + s, t, new Vector3(0.055f * side, 0.0225f, 0.01f), new Vector3(0.07f, 0.045f, 0.09f), m.Grey);
            Part(PrimitiveType.Capsule, "Arm" + s, t, new Vector3(0.105f * side, 0.115f, 0f), new Vector3(0.045f, 0.05f, 0.045f), m.White,
                Quaternion.Euler(0, 0, 25f * side));
            Part(PrimitiveType.Cylinder, "Ear" + s, t, new Vector3(0.137f * side, 0.29f, 0f), new Vector3(0.06f, 0.012f, 0.06f), m.Grey,
                Quaternion.Euler(0, 0, 90f));
        }

        Part(PrimitiveType.Sphere, BodyName, t, new Vector3(0, 0.11f, 0), new Vector3(0.19f, 0.17f, 0.16f), m.White);
        Part(PrimitiveType.Sphere, "ChestScreen", t, new Vector3(0, 0.12f, 0.075f), new Vector3(0.075f, 0.055f, 0.02f), m.Dark);
        Face(PrimitiveType.Quad, "Smile", t, new Vector3(0, 0.118f, 0.0865f), 0.05f, m.Smile);

        Part(PrimitiveType.Sphere, HeadName, t, new Vector3(0, 0.29f, 0), new Vector3(0.27f, 0.22f, 0.22f), m.White);
        Part(PrimitiveType.Sphere, VisorName, t, new Vector3(0, 0.285f, 0.088f), new Vector3(0.21f, 0.135f, 0.06f), m.Dark);
        Face(PrimitiveType.Quad, EyeLeftName, t, new Vector3(0.045f, 0.29f, 0.1195f), 0.05f, m.Eye);
        Face(PrimitiveType.Quad, EyeRightName, t, new Vector3(-0.045f, 0.29f, 0.1195f), 0.05f, m.Eye);
        return robot;
    }

    static GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat,
        Quaternion? rot = null)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot ?? Quaternion.identity;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    /// <summary>A flat decal facing forward. Quads show their face toward -Z, so turn them around.</summary>
    static void Face(PrimitiveType type, string name, Transform parent, Vector3 pos, float size, Material mat) =>
        Part(type, name, parent, pos, Vector3.one * size, mat, Quaternion.Euler(0, 180, 0));

    static Material Lit(Color color, float smoothness)
    {
        var mat = new Material(Shader.Find("Standard")) { color = color };
        mat.SetFloat("_Glossiness", smoothness);
        return mat;
    }

    static Material Unlit(Texture2D tex, string pathWithoutExtension)
    {
        var saved = TranslatorAssetGenerator.SavePng(tex, pathWithoutExtension + ".png", overwrite: true);
        var mat = new Material(Shader.Find("Unlit/Transparent")) { mainTexture = saved };
        return TranslatorAssetGenerator.SaveMaterial(mat, pathWithoutExtension + ".mat");
    }

    /// <summary>A glowing ring with soft edges on a transparent background.</summary>
    public static Texture2D DrawEye()
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var center = new Vector2(size / 2f, size / 2f);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
            float a = Mathf.Clamp01(Mathf.Min(d - 38f, 58f - d) / 2f);
            tex.SetPixel(x, y, new Color(Glow.r, Glow.g, Glow.b, a));
        }
        return tex;
    }

    /// <summary>Five glowing dots in a smile.</summary>
    public static Texture2D DrawSmile()
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var dots = new Vector2[5];
        for (int i = 0; i < 5; i++)
        {
            float x = 24 + i * 20;
            dots[i] = new Vector2(x, 52f + 0.012f * (x - 64) * (x - 64)); // texture y grows upward
        }
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float a = 0f;
            foreach (var d in dots)
                a = Mathf.Max(a, Mathf.Clamp01((9f - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), d)) / 1.5f));
            tex.SetPixel(x, y, new Color(Glow.r, Glow.g, Glow.b, a));
        }
        return tex;
    }
}
