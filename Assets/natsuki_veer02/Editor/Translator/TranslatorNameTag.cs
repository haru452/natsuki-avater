using UnityEditor;
using UnityEngine;
using C = TranslatorConfig;

/// <summary>
/// The humanoid pet's name tag (the picture from Tools/translator/nametag.py) on a quad above her head. Its shader
/// turns it to face whoever looks, like VRChat's own name tags; it rides the head bone so it moves with her.
/// </summary>
public static class TranslatorNameTag
{
    public const string ShaderName = "Translator/NameTagBillboard";

    /// <summary>pet: the model root (Pet_Robot); the tag is NameTagWidth wide and NameTagLift above the head at its scale.</summary>
    public static GameObject Add(Transform head, Transform pet, string outputDir)
    {
        Import(C.NameTagTexture);
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(C.NameTagTexture)
            ?? throw new System.InvalidOperationException("[Translator] 名札の画像がありません: " + C.NameTagTexture);
        var shader = Shader.Find(ShaderName) ?? throw new System.InvalidOperationException("[Translator] シェーダーがありません: " + ShaderName);
        var material = new Material(shader) { mainTexture = texture };
        material = TranslatorAssetGenerator.SaveMaterial(material, outputDir + "/NameTag.mat");

        var tag = GameObject.CreatePrimitive(PrimitiveType.Quad);
        tag.name = C.NameTagName;
        Object.DestroyImmediate(tag.GetComponent<Collider>());
        tag.GetComponent<MeshRenderer>().sharedMaterial = material;
        var mr = tag.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        float s = pet.lossyScale.x;
        tag.transform.SetParent(head, false);
        tag.transform.position = head.position + Vector3.up * (C.NameTagLift * s);
        tag.transform.rotation = pet.rotation;
        var want = new Vector3(C.NameTagWidth, C.NameTagWidth * texture.height / texture.width, 1f) * s;
        var parent = head.lossyScale;
        tag.transform.localScale = new Vector3(want.x / parent.x, want.y / parent.y, want.z / parent.z);
        return tag;
    }

    /// <summary>Keep the picture's own size (not stretched to a power of two), sharp and see-through.</summary>
    static void Import(string path)
    {
        if (!(AssetImporter.GetAtPath(path) is TextureImporter ti)) return;
        if (ti.npotScale == TextureImporterNPOTScale.None && ti.alphaIsTransparency && ti.wrapMode == TextureWrapMode.Clamp) return;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.alphaIsTransparency = true;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.mipmapEnabled = true;
        ti.textureCompression = TextureImporterCompression.CompressedHQ;
        ti.SaveAndReimport();
    }
}
