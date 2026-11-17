using UnityEditor;
using UnityEngine;
using C = TranslatorConfig;

/// <summary>
/// The humanoid pet's subtitle as a chat bubble over her head (above the name tag): KAT's text quad with the
/// Translator/KATChatBubble shader (Tools/translator/chat_bubble_shader.py), which faces whoever looks and shrinks
/// the bubble to the text. KAT's animations write the characters exactly as before.
/// </summary>
public static class TranslatorChatBubble
{
    public const string ShaderName = "Translator/KATChatBubble";

    /// <summary>KAT's text material (its letters and layout) on the bubble shader.</summary>
    public static Material Material(string outputDir)
    {
        var kat = Resources.Load<Material>(C.KatTextMaterialResource)
            ?? throw new System.InvalidOperationException("[Translator] KAT のマテリアルがありません: " + C.KatTextMaterialResource);
        var shader = Shader.Find(ShaderName) ?? throw new System.InvalidOperationException("[Translator] シェーダーがありません: " + ShaderName);
        var mat = new Material(kat) { shader = shader };
        return TranslatorAssetGenerator.SaveMaterial(mat, outputDir + "/ChatBubble.mat");
    }

    /// <summary>The subtitle's parent on her head: the bubble's bottom NameTagLift + ChatBubbleLift above the head bone.</summary>
    public static GameObject AddAnchor(Transform head, Transform pet)
    {
        float s = pet.lossyScale.x;
        var anchor = new GameObject(C.TextName);
        anchor.transform.SetParent(head, false);
        anchor.transform.position = head.position + Vector3.up * ((C.NameTagLift + C.ChatBubbleLift) * s);
        anchor.transform.rotation = pet.rotation;
        var parent = head.lossyScale;
        anchor.transform.localScale = new Vector3(s / parent.x, s / parent.y, s / parent.z);
        return anchor;
    }
}
