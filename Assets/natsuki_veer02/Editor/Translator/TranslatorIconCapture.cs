using System.IO;
using UnityEditor;
using UnityEngine;
using C = TranslatorConfig;

/// <summary>
/// A face photo of the pet (Homare) for her name tag: the model alone in front of a plain background,
/// a camera a little in front of her head. Saved as NameTagIconSource (square PNG).
/// </summary>
public static class TranslatorIconCapture
{
    const int Size = 512;

    [MenuItem("Tools/Translator Pet/Capture Name Tag Icon")]
    public static void Capture() => Capture(C.NameTagAvatarPrefab, C.NameTagIconSource);

    /// <summary>A check shot of the built pet with her name tag (PetPreview*.png in the system temp folder), front and side.</summary>
    public static void PreviewPet()
    {
        Capture(TranslatorSetup.PrefabPath, Path.Combine(Path.GetTempPath(), "PetPreview.png"), "Pet_Robot", 3.2f, 30f, text: "Hello! Nice to meet you.");
        Capture(TranslatorSetup.PrefabPath, Path.Combine(Path.GetTempPath(), "PetPreviewSide.png"), "Pet_Robot", 3.2f, 30f, yaw: 70f,
            text: "Let's show them, \"I did it! I did it!\" This is a longer one.");
    }

    /// <summary>Switches the subtitle on and writes text into it, as KAT would (ASCII only: code = char - 32).</summary>
    static void ShowText(Transform subject, string text)
    {
        var quad = TranslatorPetModel.FindDeep(subject, C.KatTextName);
        if (quad == null) return;
        for (var t = quad; t != null && t != subject; t = t.parent) t.gameObject.SetActive(true);
        var mat = quad.GetComponent<MeshRenderer>().material;
        for (int i = 0; i < 128; i++) mat.SetFloat("_Char" + i, i < text.Length ? text[i] - 32 : 0);
    }

    public static void Capture(string avatarPrefab, string outputPath) => Capture(avatarPrefab, outputPath, null, 1.0f, 24f);

    /// <summary>focus: a child to frame (its Head bone, else itself); null frames the avatar's own head.</summary>
    static void Capture(string avatarPrefab, string outputPath, string focus, float distance, float fov, float yaw = 0f, string text = null)
    {
        var avatar = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(avatarPrefab)
            ?? throw new System.InvalidOperationException("[Translator] アバターがありません: " + avatarPrefab));
        var camGo = new GameObject("IconCamera");
        var lightGo = new GameObject("IconLight");
        var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        try
        {
            avatar.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var subject = focus == null ? avatar.transform : TranslatorPetModel.FindDeep(avatar.transform, focus);
            for (var t = subject; t != null; t = t.parent) t.gameObject.SetActive(true); // the pet waits switched off
            if (text != null) ShowText(subject, text);
            var animator = subject.GetComponent<Animator>();
            var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : TranslatorPetModel.Head(subject);
            var face = head != null ? head.position + Vector3.up * 0.09f : subject.position + new Vector3(0, 1.4f, 0);

            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.transform.rotation = Quaternion.Euler(20f, 160f, 0f); // from the front, a little above
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);

            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.98f, 0.8f, 0.88f); // soft sakura pink
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            cam.transform.position = face + Quaternion.Euler(0, yaw, 0) * Vector3.forward * distance;
            cam.transform.LookAt(face);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            File.WriteAllBytes(outputPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            if (outputPath.StartsWith("Assets/")) AssetDatabase.ImportAsset(outputPath);
            Debug.Log("[Translator] アイコンを撮りました: " + outputPath);
        }
        finally
        {
            Object.DestroyImmediate(avatar);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(lightGo);
            rt.Release();
        }
    }
}
