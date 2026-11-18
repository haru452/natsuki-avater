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
    public const string LiftProperty = "_LiftY";

    /// <summary>The bubble's bottom above the head bone, in text-quad heights: NameTagLift + ChatBubbleLift at her scale.</summary>
    public static float Lift => (C.NameTagLift + C.ChatBubbleLift) / C.TextSize.y;

    /// <summary>KAT's text material (its letters and layout) on the bubble shader.</summary>
    public static Material Material(string outputDir)
    {
        var kat = Resources.Load<Material>(C.KatTextMaterialResource)
            ?? throw new System.InvalidOperationException("[Translator] KAT のマテリアルがありません: " + C.KatTextMaterialResource);
        var shader = Shader.Find(ShaderName) ?? throw new System.InvalidOperationException("[Translator] シェーダーがありません: " + ShaderName);
        var mat = new Material(kat) { shader = shader };
        // Lifted straight up in the shader (not by its place on the head bone, which swung it round as her head
        // turned and nodded), and a fixed size (KAT's growing with distance made it wobble).
        mat.SetFloat(LiftProperty, Lift);
        mat.SetFloat("_GrowEnabled", 0f);
        return TranslatorAssetGenerator.SaveMaterial(mat, outputDir + "/ChatBubble.mat");
    }

    /// <summary>
    /// The subtitle's parent: on the head bone itself (the shader lifts the bubble straight up from there), so it
    /// follows her head as she sits or bends without swinging round when the head turns.
    /// </summary>
    public static GameObject AddAnchor(Transform head, Transform pet)
    {
        float s = pet.lossyScale.x;
        var anchor = new GameObject(C.TextName);
        anchor.transform.SetParent(head, false);
        anchor.transform.localPosition = Vector3.zero;
        anchor.transform.rotation = pet.rotation;
        var parent = head.lossyScale;
        anchor.transform.localScale = new Vector3(s / parent.x, s / parent.y, s / parent.z);
        return anchor;
    }
}
