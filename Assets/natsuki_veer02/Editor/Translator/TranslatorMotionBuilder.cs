using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using C = TranslatorConfig;

/// <summary>
/// The pet's own motion: hopping while the wearer moves. Merged on Pet_Follow, so paths are relative to it.
/// Only Pet_Hop (block and face) bounces; the text stays still so it is easy to read.
/// </summary>
public static class TranslatorMotionBuilder
{
    public const string HopControllerName = "Translator_Hop.controller";
    public const string HopLayerName = "Translator_Hop";
    /// <summary>Checked once per loop, just before landing, so the pet never stops mid-air.</summary>
    const float StopExitTime = 0.95f;

    public static AnimatorController BuildHopController(string outputDir)
    {
        var controller = TranslatorAnimationBuilder.NewController(outputDir + "/" + HopControllerName);
        controller.AddParameter(C.VelocityParam, AnimatorControllerParameterType.Float);

        var layer = controller.layers[0];
        layer.name = HopLayerName;
        layer.defaultWeight = 1f;
        controller.layers = new[] { layer };
        var sm = layer.stateMachine;

        var idle = sm.AddState("Idle", new Vector3(300, 100));
        var hop = sm.AddState("Hop", new Vector3(300, 200));
        sm.defaultState = idle;
        idle.motion = TranslatorAnimationBuilder.SaveClip(IdleClip(), outputDir + "/Translator_Idle.anim");
        hop.motion = TranslatorAnimationBuilder.SaveClip(HopClip(), outputDir + "/Translator_Hop.anim");
        idle.writeDefaultValues = false;
        hop.writeDefaultValues = false;

        // Different start and stop speeds, so walking slowly does not flicker between the two.
        var start = idle.AddTransition(hop);
        start.hasExitTime = false;
        start.duration = 0f;
        start.AddCondition(AnimatorConditionMode.Greater, C.HopStartSpeed, C.VelocityParam);

        var stop = hop.AddTransition(idle);
        stop.hasExitTime = true;
        stop.exitTime = StopExitTime;
        stop.duration = 0.05f;
        stop.AddCondition(AnimatorConditionMode.Less, C.HopStopSpeed, C.VelocityParam);

        EditorUtility.SetDirty(controller);
        return controller;
    }

    /// <summary>Rest pose. Write Defaults is off, so Idle must put the pose back itself.</summary>
    static AnimationClip IdleClip()
    {
        var clip = new AnimationClip();
        Set(clip, "m_LocalPosition.y", AnimationCurve.Constant(0f, 1f / 60f, 0f));
        foreach (var axis in new[] { "x", "y", "z" })
            Set(clip, "m_LocalScale." + axis, AnimationCurve.Constant(0f, 1f / 60f, 1f));
        return clip;
    }

    /// <summary>One hop: squash on the ground, stretch at the top, squash again on landing. Loops.</summary>
    static AnimationClip HopClip()
    {
        float t = C.HopSeconds;
        var clip = new AnimationClip();

        // Height follows a parabola: launch fast, slow at the top, fall fast.
        float launch = 4f * C.HopHeight / t;
        Set(clip, "m_LocalPosition.y", new AnimationCurve(
            new Keyframe(0f, 0f, launch, launch),
            new Keyframe(t / 2, C.HopHeight, 0f, 0f),
            new Keyframe(t, 0f, -launch, -launch)));

        for (int i = 0; i < 3; i++)
        {
            string axis = "xyz"[i].ToString();
            Set(clip, "m_LocalScale." + axis, new AnimationCurve(
                new Keyframe(0f, C.HopSquash[i]),
                new Keyframe(t * 0.15f, 1f),
                new Keyframe(t / 2, C.HopStretch[i]),
                new Keyframe(t * 0.85f, 1f),
                new Keyframe(t, C.HopSquash[i])));
        }

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    static void Set(AnimationClip clip, string property, AnimationCurve curve) =>
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(C.PetHopName, typeof(Transform), property), curve);
}
